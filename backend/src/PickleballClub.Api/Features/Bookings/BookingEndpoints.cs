using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using PickleballClub.Api.Data;
using PickleballClub.Api.Domain;
using PickleballClub.Api.Features.Auth;
using PickleballClub.Api.Services;

namespace PickleballClub.Api.Features.Bookings;

/// <param name="QrPayload">PromptPay: the text to draw as a QR. Only while the Booking is held.</param>
/// <param name="CheckoutUrl">Card: the hosted payment page. Only while the Booking is held.</param>
public record PaymentDto(string Method, decimal Amount, string Status, string? QrPayload, string? CheckoutUrl, DateTime? ExpiresAt);

/// <param name="Date">The Club's local date of play.</param>
/// <param name="Start">Wall-clock time at the Club, <c>HH:mm</c>.</param>
/// <param name="Hours">Number of Slots.</param>
/// <param name="RescheduleUntil">The last moment the Customer may still Reschedule; null when they cannot (not confirmed, or already moved once).</param>
/// <param name="Payment">The newest payment attempt, if any (a Walk-in Booking has none).</param>
public record BookingDto(
    Guid Id, string Code, string Status, string Source, Guid CourtId, string CourtName,
    DateOnly Date, string Start, string End, int Hours, DateTime StartAt, DateTime EndAt,
    decimal Total, DateTime? HoldExpiresAt, DateTime CreatedAt, bool Rescheduled, DateTime? RescheduleUntil, PaymentDto? Payment);

public static class BookingEndpoints
{
    public static RouteGroupBuilder MapBookings(this RouteGroupBuilder api)
    {
        // Start a Booking: holds the Slots and opens a payment. Send an Idempotency-Key header so a retry cannot book twice.
        api.MapPost("/bookings", async (CreateBookingRequest req, HttpRequest http, ClaimsPrincipal user,
            BookingService bookings, AppDbContext db, CancellationToken ct) =>
        {
            var key = http.Headers["Idempotency-Key"].FirstOrDefault();
            if (key is { Length: > 100 }) throw new ApiException(400, "bad_request", "The Idempotency-Key is too long.");
            var booking = await bookings.CreateHoldAsync(user.UserId(), req, string.IsNullOrWhiteSpace(key) ? null : key, ct);
            return Results.Created($"/api/v1/me/bookings/{booking.Id}", await LoadDtoAsync(db, booking.Id, ct));
        }).RequireAuthorization(Policies.Customer).WithValidation().WithTags("Bookings");

        var me = api.MapGroup("/me/bookings").RequireAuthorization(Policies.Customer).WithTags("Bookings");

        // Newest first; Holds that ran out are left out.
        me.MapGet("/", async (ClaimsPrincipal user, AppDbContext db, CancellationToken ct) =>
        {
            var uid = user.UserId();
            var ids = await db.Bookings.AsNoTracking()
                .Where(b => b.UserId == uid && b.Status != BookingStatus.Expired)
                .OrderByDescending(b => b.StartAt)
                .Select(b => b.Id)
                .Take(100)
                .ToListAsync(ct);
            var list = new List<BookingDto>();
            foreach (var id in ids) list.Add((await LoadDtoAsync(db, id, ct))!);
            return list;
        });

        me.MapGet("/{id:guid}", async (Guid id, ClaimsPrincipal user, AppDbContext db, CancellationToken ct) =>
        {
            await EnsureOwnAsync(db, id, user.UserId(), ct);
            return await LoadDtoAsync(db, id, ct);
        });

        // Give an unpaid Hold back before its time runs out.
        me.MapPost("/{id:guid}/release", async (Guid id, ClaimsPrincipal user, BookingService bookings, AppDbContext db, CancellationToken ct) =>
        {
            await bookings.ReleaseHoldAsync(user.UserId(), id, ct);
            return await LoadDtoAsync(db, id, ct);
        });

        // Pay for the same Hold another way (e.g. card instead of the QR).
        me.MapPost("/{id:guid}/payment", async (Guid id, NewPaymentRequest req, ClaimsPrincipal user, BookingService bookings, AppDbContext db, CancellationToken ct) =>
        {
            await bookings.NewPaymentAsync(user.UserId(), id, req.Method, ct);
            return await LoadDtoAsync(db, id, ct);
        }).WithValidation();

        // Move a confirmed Booking: once, to the same number of Slots, at the same price or less.
        me.MapPost("/{id:guid}/reschedule", async (Guid id, RescheduleRequest req, ClaimsPrincipal user, BookingService bookings, AppDbContext db, CancellationToken ct) =>
        {
            await bookings.RescheduleAsync(user.UserId(), id, req, ct);
            return await LoadDtoAsync(db, id, ct);
        }).WithValidation();

        return api;
    }

    /// <summary>404 unless the Booking is this Customer's (someone else's looks the same as a missing one).</summary>
    private static async Task EnsureOwnAsync(AppDbContext db, Guid id, Guid userId, CancellationToken ct)
    {
        if (!await db.Bookings.AsNoTracking().AnyAsync(b => b.Id == id && b.UserId == userId, ct))
            throw new ApiException(404, "booking_not_found", "That booking does not exist.");
    }

    public static async Task<BookingDto?> LoadDtoAsync(AppDbContext db, Guid id, CancellationToken ct)
    {
        var b = await db.Bookings.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (b is null) return null;
        var courtName = await db.Courts.AsNoTracking().Where(c => c.Id == b.CourtId).Select(c => c.Name).FirstAsync(ct);
        var club = await db.ClubSettings.AsNoTracking().SingleAsync(ct);
        var pay = await db.Payments.AsNoTracking().Where(p => p.BookingId == id).OrderByDescending(p => p.CreatedAt).FirstOrDefaultAsync(ct);
        return ToDto(b, courtName, club, pay);
    }

    public static BookingDto ToDto(Booking b, string courtName, ClubSettings club, Payment? pay)
    {
        var start = ClubClock.ToLocal(b.StartAt, club.Timezone);
        var canReschedule = b.Status == BookingStatus.Confirmed && b.Source == BookingSource.Online && b.RescheduledAt is null;
        var hours = (int)Math.Round((b.EndAt - b.StartAt).TotalHours);
        var held = b.Status == BookingStatus.Held;
        return new BookingDto(
            b.Id, b.Code, b.Status, b.Source, b.CourtId, courtName,
            DateOnly.FromDateTime(start), ClubClock.Label(start.Hour), ClubClock.Label(start.Hour + hours), hours, b.StartAt, b.EndAt,
            b.Total, b.HoldExpiresAt, b.CreatedAt, b.RescheduledAt is not null,
            canReschedule ? b.StartAt.AddHours(-club.RescheduleNoticeHours) : null,
            pay is null ? null : new PaymentDto(pay.Method, pay.Amount, pay.Status, held ? pay.QrPayload : null, held ? pay.CheckoutUrl : null, pay.ExpiresAt));
    }
}
