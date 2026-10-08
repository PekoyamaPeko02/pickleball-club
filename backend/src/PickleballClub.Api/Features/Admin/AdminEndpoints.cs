using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using PickleballClub.Api.Data;
using PickleballClub.Api.Domain;
using PickleballClub.Api.Features.Auth;
using PickleballClub.Api.Services;

namespace PickleballClub.Api.Features.Admin;

/// <param name="CustomerName">The Customer's name, or the Walk-in Guest's.</param>
/// <param name="PaymentState">paid (online) | counter (Walk-in Booking) | pending (still held) | none.</param>
public record AdminBookingDto(
    Guid Id, string Code, string Status, string Source, Guid CourtId, string CourtName,
    DateOnly Date, string Start, string End, int Hours, DateTime StartAt, DateTime EndAt, decimal Total,
    string CustomerName, string? CustomerPhone, string? CustomerEmail,
    string PaymentState, string? PaymentMethod, bool Rescheduled, DateTime? HoldExpiresAt,
    DateTime? CheckedInAt, DateTime? CancelledAt, string? RefundNote, DateTime CreatedAt);

public record CourtBlockDto(Guid Id, Guid CourtId, string CourtName, DateOnly Date, string Start, string End, DateTime StartAt, DateTime EndAt, string Reason);

/// <summary>One day at the Club: the public grid plus who has which Court and which Courts are blocked.</summary>
public record ScheduleDto(AvailabilityDto Availability, List<AdminBookingDto> Bookings, List<CourtBlockDto> Blocks);

/// <summary>A payment whose money arrived but bought nothing: the Club must refund it by hand.</summary>
public record RefundDueDto(
    Guid PaymentId, Guid BookingId, string BookingCode, string CustomerName, string? CustomerPhone, string? CustomerEmail,
    string Method, decimal Amount, string? Reason, DateTime? PaidAt, DateTime? RefundedAt, string? RefundNote);

public static class AdminEndpoints
{
    public static RouteGroupBuilder MapAdmin(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/admin").RequireAuthorization(Policies.Admin).WithTags("Admin");

        // ?date=yyyy-MM-dd (the Club's local date); defaults to today. Every Booking that touches the day, except Holds that ran out.
        g.MapGet("/schedule", async (DateOnly? date, AvailabilityService availability, AppDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            var club = await db.ClubSettings.AsNoTracking().SingleAsync(ct);
            var day = date ?? ClubClock.Today(club.Timezone, clock.GetUtcNow().UtcDateTime);
            var (from, to) = (ClubClock.ToUtc(day, 0, club.Timezone), ClubClock.ToUtc(day, 24, club.Timezone));

            var ids = await db.Bookings.AsNoTracking()
                .Where(b => b.Status != BookingStatus.Expired && b.StartAt < to && b.EndAt > from)
                .OrderBy(b => b.StartAt).Select(b => b.Id).ToListAsync(ct);
            var blocks = await db.CourtBlocks.AsNoTracking().Where(b => b.StartAt < to && b.EndAt > from).OrderBy(b => b.StartAt).ToListAsync(ct);
            var courts = await db.Courts.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Name, ct);

            return new ScheduleDto(
                await availability.ForDateAsync(day, ct),
                await LoadManyAsync(db, ids, ct),
                blocks.Select(b => ToDto(b, courts.GetValueOrDefault(b.CourtId, ""), club.Timezone)).ToList());
        });

        // Look a Booking up by the code the customer shows (case-insensitive).
        g.MapGet("/bookings/by-code/{code}", async (string code, AppDbContext db, CancellationToken ct) =>
        {
            var wanted = code.Trim().ToUpperInvariant();
            var id = await db.Bookings.AsNoTracking().Where(b => b.Code == wanted).Select(b => (Guid?)b.Id).FirstOrDefaultAsync(ct)
                     ?? throw new ApiException(404, "booking_not_found", "No booking has that code.");
            return (await LoadManyAsync(db, [id], ct)).Single();
        });

        g.MapGet("/bookings/{id:guid}", async (Guid id, AppDbContext db, CancellationToken ct) =>
            (await LoadManyAsync(db, [id], ct)).SingleOrDefault() ?? throw new ApiException(404, "booking_not_found", "That booking does not exist."));

        g.MapPost("/bookings/walk-in", async (WalkInRequest req, ClaimsPrincipal admin, AdminBookingService service, AppDbContext db, CancellationToken ct) =>
        {
            var booking = await service.CreateWalkInAsync(admin.UserId(), req, ct);
            return Results.Created($"/api/v1/admin/bookings/{booking.Id}", (await LoadManyAsync(db, [booking.Id], ct)).Single());
        }).WithValidation();

        g.MapPost("/bookings/{id:guid}/move", async (Guid id, AdminMoveRequest req, ClaimsPrincipal admin, AdminBookingService service, AppDbContext db, CancellationToken ct) =>
        {
            await service.MoveAsync(admin.UserId(), id, req, ct);
            return (await LoadManyAsync(db, [id], ct)).Single();
        }).WithValidation();

        g.MapPost("/bookings/{id:guid}/cancel", async (Guid id, AdminCancelRequest req, ClaimsPrincipal admin, AdminBookingService service, AppDbContext db, CancellationToken ct) =>
        {
            await service.CancelAsync(admin.UserId(), id, req, ct);
            return (await LoadManyAsync(db, [id], ct)).Single();
        }).WithValidation();

        g.MapPost("/bookings/{id:guid}/check-in", async (Guid id, AdminBookingService service, AppDbContext db, CancellationToken ct) =>
        {
            await service.CheckInAsync(id, ct);
            return (await LoadManyAsync(db, [id], ct)).Single();
        });

        g.MapPost("/bookings/{id:guid}/no-show", async (Guid id, AdminBookingService service, AppDbContext db, CancellationToken ct) =>
        {
            await service.MarkNoShowAsync(id, ct);
            return (await LoadManyAsync(db, [id], ct)).Single();
        });

        g.MapPost("/blocks", async (CourtBlockRequest req, ClaimsPrincipal admin, AdminBookingService service, AppDbContext db, CancellationToken ct) =>
        {
            var block = await service.CreateBlockAsync(admin.UserId(), req, ct);
            var tz = await db.ClubSettings.AsNoTracking().Select(c => c.Timezone).SingleAsync(ct);
            var court = await db.Courts.AsNoTracking().Where(c => c.Id == block.CourtId).Select(c => c.Name).FirstAsync(ct);
            return Results.Created($"/api/v1/admin/blocks/{block.Id}", ToDto(block, court, tz));
        }).WithValidation();

        g.MapDelete("/blocks/{id:guid}", async (Guid id, AdminBookingService service, CancellationToken ct) =>
        {
            await service.DeleteBlockAsync(id, ct);
            return Results.NoContent();
        });

        // Payments that need a manual refund; ?all=true also lists the ones already refunded.
        g.MapGet("/refunds", async (bool? all, AppDbContext db, CancellationToken ct) =>
        {
            var rows = await (
                from p in db.Payments.AsNoTracking()
                join b in db.Bookings.AsNoTracking() on p.BookingId equals b.Id
                join u in db.Users.AsNoTracking() on b.UserId equals u.Id into users
                from u in users.DefaultIfEmpty()
                where p.RefundDue && (all == true || p.RefundedAt == null)
                orderby p.PaidAt descending
                select new RefundDueDto(p.Id, b.Id, b.Code, (u != null ? u.DisplayName : b.GuestName) ?? "", u != null ? u.Phone : b.GuestPhone, u != null ? u.Email : null,
                    p.Method, p.Amount, p.RefundReason, p.PaidAt, p.RefundedAt, p.RefundNote)).Take(200).ToListAsync(ct);
            return rows;
        });

        g.MapPost("/refunds/{paymentId:guid}/refunded", async (Guid paymentId, RefundedRequest req, ClaimsPrincipal admin, AdminBookingService service, CancellationToken ct) =>
        {
            await service.MarkRefundedAsync(admin.UserId(), paymentId, req.Note, ct);
            return Results.NoContent();
        }).WithValidation();

        g.MapSettings();
        g.MapReports();

        return api;
    }

    public static async Task<List<AdminBookingDto>> LoadManyAsync(AppDbContext db, IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return [];
        var tz = await db.ClubSettings.AsNoTracking().Select(c => c.Timezone).SingleAsync(ct);
        var rows = await (
            from b in db.Bookings.AsNoTracking()
            join c in db.Courts.AsNoTracking() on b.CourtId equals c.Id
            join u in db.Users.AsNoTracking() on b.UserId equals u.Id into users
            from u in users.DefaultIfEmpty()
            where ids.Contains(b.Id)
            orderby b.StartAt, c.SortOrder
            select new { Booking = b, CourtName = c.Name, User = u }).ToListAsync(ct);
        var paidMethods = await db.Payments.AsNoTracking()
            .Where(p => ids.Contains(p.BookingId) && p.Status == PaymentStatus.Succeeded && !p.RefundDue)
            .ToDictionaryAsync(p => p.BookingId, p => p.Method, ct);

        return rows.Select(r =>
        {
            var b = r.Booking;
            var start = ClubClock.ToLocal(b.StartAt, tz);
            var hours = (int)Math.Round((b.EndAt - b.StartAt).TotalHours);
            var method = b.Source == BookingSource.WalkIn ? b.CounterPayment : paidMethods.GetValueOrDefault(b.Id);
            var state = b.Source == BookingSource.WalkIn ? "counter" : method is not null ? "paid" : b.Status == BookingStatus.Held ? "pending" : "none";
            return new AdminBookingDto(
                b.Id, b.Code, b.Status, b.Source, b.CourtId, r.CourtName,
                DateOnly.FromDateTime(start), ClubClock.Label(start.Hour), ClubClock.Label(start.Hour + hours), hours, b.StartAt, b.EndAt, b.Total,
                (r.User is not null ? r.User.DisplayName : b.GuestName) ?? "", r.User is not null ? r.User.Phone : b.GuestPhone, r.User?.Email,
                state, method, b.RescheduledAt is not null, b.HoldExpiresAt, b.CheckedInAt, b.CancelledAt, b.RefundNote, b.CreatedAt);
        }).ToList();
    }

    private static CourtBlockDto ToDto(CourtBlock b, string courtName, string tz)
    {
        var start = ClubClock.ToLocal(b.StartAt, tz);
        var hours = (int)Math.Round((b.EndAt - b.StartAt).TotalHours);
        return new CourtBlockDto(b.Id, b.CourtId, courtName, DateOnly.FromDateTime(start), ClubClock.Label(start.Hour), ClubClock.Label(start.Hour + hours), b.StartAt, b.EndAt, b.Reason);
    }
}
