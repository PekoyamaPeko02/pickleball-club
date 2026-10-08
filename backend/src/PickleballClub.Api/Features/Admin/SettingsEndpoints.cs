using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PickleballClub.Api.Data;
using PickleballClub.Api.Domain;
using PickleballClub.Api.Services;

namespace PickleballClub.Api.Features.Admin;

public record ClubSettingsDto(string Name, string Timezone, int BookingWindowDays, int RescheduleNoticeHours);

public record ClubSettingsRequest(
    [property: NotEmpty, MaxLength(80)] string Name,
    [property: Range(1, 365)] int BookingWindowDays,
    [property: Range(0, 720)] int RescheduleNoticeHours);

public record CourtSettingsDto(Guid Id, string Name, bool Indoor, int SortOrder, bool IsActive);

public record CourtRequest(
    [property: NotEmpty, MaxLength(40)] string Name,
    bool Indoor,
    [property: Range(0, 1000)] int SortOrder,
    bool IsActive = true);

/// <param name="DayOfWeek">0 = Sunday … 6 = Saturday.</param>
/// <param name="CloseHour">24 = midnight.</param>
public record HoursDay([property: Range(0, 6)] int DayOfWeek, [property: Range(0, 23)] int OpenHour, [property: Range(1, 24)] int CloseHour);

/// <summary>The whole week; a day that is left out is closed.</summary>
public record HoursRequest([property: Required, MaxLength(7)] List<HoursDay> Days);

public record PriceRuleDto(Guid Id, Guid? CourtId, string DayType, int StartHour, int EndHour, decimal PricePerHour, string? Label);

/// <param name="CourtId">Null = every Court.</param>
public record PriceRuleRequest(
    Guid? CourtId,
    [property: Required, RegularExpression("^(weekday|holiday)$")] string DayType,
    [property: Range(0, 23)] int StartHour,
    [property: Range(1, 24)] int EndHour,
    [property: Range(0, 1_000_000)] decimal PricePerHour,
    [property: MaxLength(60)] string? Label);

public record HolidayDto(DateOnly Date, string? Note);

public record HolidayRequest(DateOnly Date, [property: MaxLength(120)] string? Note);

public record SettingsDto(
    ClubSettingsDto Club, List<CourtSettingsDto> Courts, List<HoursDay> OperatingHours, List<PriceRuleDto> PriceRules, List<HolidayDto> Holidays);

/// <summary>What the Admin sets up: the Club's rules, Courts, Operating Hours, Price Rules and holidays.</summary>
public static class SettingsEndpoints
{
    public static void MapSettings(this RouteGroupBuilder admin)
    {
        admin.MapGet("/settings", (AppDbContext db, CancellationToken ct) => LoadAsync(db, ct));

        admin.MapPut("/settings/club", async (ClubSettingsRequest req, AppDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            var club = await db.ClubSettings.SingleAsync(ct);
            club.Name = req.Name.Trim();
            club.BookingWindowDays = req.BookingWindowDays;
            club.RescheduleNoticeHours = req.RescheduleNoticeHours;
            club.UpdatedAt = clock.GetUtcNow().UtcDateTime;
            await db.SaveChangesAsync(ct);
            return ToDto(club);
        }).WithValidation();

        // ---- Courts. A Court is switched off, never deleted, so old Bookings keep their Court.
        admin.MapPost("/courts", async (CourtRequest req, AppDbContext db, CancellationToken ct) =>
        {
            var court = new Court { Id = Guid.NewGuid(), Name = req.Name.Trim(), Indoor = req.Indoor, SortOrder = req.SortOrder, IsActive = req.IsActive };
            db.Courts.Add(court);
            await SaveCourtAsync(db, ct);
            return Results.Created($"/api/v1/admin/courts/{court.Id}", ToDto(court));
        }).WithValidation();

        admin.MapPut("/courts/{id:guid}", async (Guid id, CourtRequest req, AppDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            var court = await db.Courts.FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw new ApiException(404, "court_not_found", "That court does not exist.");
            if (court.IsActive && !req.IsActive)
            {
                var now = clock.GetUtcNow().UtcDateTime;
                if (await db.Bookings.AnyAsync(b => b.CourtId == id && BookingStatus.Live.Contains(b.Status) && b.EndAt > now, ct))
                    throw new ApiException(409, "court_has_bookings", "This court still has bookings ahead. Move or cancel them before switching it off.");
            }
            court.Name = req.Name.Trim();
            court.Indoor = req.Indoor;
            court.SortOrder = req.SortOrder;
            court.IsActive = req.IsActive;
            await SaveCourtAsync(db, ct);
            return Results.Ok(ToDto(court));
        }).WithValidation();

        // ---- Operating Hours: the request replaces the whole week.
        admin.MapPut("/hours", async (HoursRequest req, AppDbContext db, CancellationToken ct) =>
        {
            if (req.Days.Select(d => d.DayOfWeek).Distinct().Count() != req.Days.Count)
                throw new ApiException(400, "duplicate_day", "Each day of the week can be given once.");
            if (req.Days.Any(d => d.CloseHour <= d.OpenHour))
                throw new ApiException(400, "bad_hours", "Closing time must be after opening time.");

            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await db.OperatingHours.ExecuteDeleteAsync(ct);
            db.OperatingHours.AddRange(req.Days.Select(d => new OperatingHours { DayOfWeek = (short)d.DayOfWeek, OpenHour = (short)d.OpenHour, CloseHour = (short)d.CloseHour }));
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return req.Days.OrderBy(d => d.DayOfWeek).ToList();
        }).WithValidation();

        // ---- Price Rules. A change never touches Bookings already made: they keep the price they were sold at.
        admin.MapPost("/price-rules", async (PriceRuleRequest req, AppDbContext db, CancellationToken ct) =>
        {
            var rule = new PriceRule { Id = Guid.NewGuid() };
            await ApplyAsync(db, rule, req, ct);
            db.PriceRules.Add(rule);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/v1/admin/price-rules/{rule.Id}", ToDto(rule));
        }).WithValidation();

        admin.MapPut("/price-rules/{id:guid}", async (Guid id, PriceRuleRequest req, AppDbContext db, CancellationToken ct) =>
        {
            var rule = await db.PriceRules.FirstOrDefaultAsync(r => r.Id == id, ct) ?? throw new ApiException(404, "price_rule_not_found", "That price rule does not exist.");
            await ApplyAsync(db, rule, req, ct);
            await db.SaveChangesAsync(ct);
            return Results.Ok(ToDto(rule));
        }).WithValidation();

        admin.MapDelete("/price-rules/{id:guid}", async (Guid id, AppDbContext db, CancellationToken ct) =>
        {
            if (await db.PriceRules.Where(r => r.Id == id).ExecuteDeleteAsync(ct) == 0)
                throw new ApiException(404, "price_rule_not_found", "That price rule does not exist.");
            return Results.NoContent();
        });

        // ---- Holidays: extra days priced as a holiday. Saturdays and Sundays are holidays already.
        admin.MapPut("/holidays", async (HolidayRequest req, AppDbContext db, CancellationToken ct) =>
        {
            var note = string.IsNullOrWhiteSpace(req.Note) ? null : req.Note.Trim();
            var holiday = await db.Holidays.FirstOrDefaultAsync(h => h.HolidayDate == req.Date, ct);
            if (holiday is null) db.Holidays.Add(holiday = new Holiday { HolidayDate = req.Date });
            holiday.Note = note;
            await db.SaveChangesAsync(ct);
            return new HolidayDto(holiday.HolidayDate, holiday.Note);
        }).WithValidation();

        admin.MapDelete("/holidays/{date}", async (DateOnly date, AppDbContext db, CancellationToken ct) =>
        {
            await db.Holidays.Where(h => h.HolidayDate == date).ExecuteDeleteAsync(ct);
            return Results.NoContent();
        });
    }

    private static async Task<SettingsDto> LoadAsync(AppDbContext db, CancellationToken ct) => new(
        ToDto(await db.ClubSettings.AsNoTracking().SingleAsync(ct)),
        (await db.Courts.AsNoTracking().OrderBy(c => c.SortOrder).ThenBy(c => c.Name).ToListAsync(ct)).Select(ToDto).ToList(),
        await db.OperatingHours.AsNoTracking().OrderBy(h => h.DayOfWeek).Select(h => new HoursDay(h.DayOfWeek, h.OpenHour, h.CloseHour)).ToListAsync(ct),
        (await db.PriceRules.AsNoTracking().OrderBy(r => r.DayType == DayTypes.Holiday).ThenBy(r => r.CourtId.HasValue).ThenBy(r => r.StartHour).ToListAsync(ct)).Select(ToDto).ToList(),
        await db.Holidays.AsNoTracking().OrderBy(h => h.HolidayDate).Select(h => new HolidayDto(h.HolidayDate, h.Note)).ToListAsync(ct));

    private static async Task SaveCourtAsync(AppDbContext db, CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ApiException(409, "court_name_taken", "Another court already has that name.");
        }
    }

    /// <summary>Copies the request onto the rule after checking it does not clash with another rule of the same scope and Day Type.</summary>
    private static async Task ApplyAsync(AppDbContext db, PriceRule rule, PriceRuleRequest req, CancellationToken ct)
    {
        if (req.EndHour <= req.StartHour) throw new ApiException(400, "bad_hours", "The end hour must be after the start hour.");
        if (req.CourtId is { } courtId && !await db.Courts.AnyAsync(c => c.Id == courtId, ct))
            throw new ApiException(400, "court_not_found", "That court does not exist.");

        rule.CourtId = req.CourtId;
        rule.DayType = req.DayType;
        rule.StartHour = (short)req.StartHour;
        rule.EndHour = (short)req.EndHour;
        rule.PricePerHour = req.PricePerHour;
        rule.Label = string.IsNullOrWhiteSpace(req.Label) ? null : req.Label.Trim();

        var others = await db.PriceRules.AsNoTracking().Where(r => r.Id != rule.Id && r.DayType == rule.DayType && r.CourtId == rule.CourtId).ToListAsync(ct);
        if (others.Any(o => Pricing.Overlap(o, rule)))
            throw new ApiException(409, "price_rule_overlap", "Another price rule already covers some of those hours for the same court and day type.");
    }

    private static ClubSettingsDto ToDto(ClubSettings c) => new(c.Name, c.Timezone, c.BookingWindowDays, c.RescheduleNoticeHours);
    private static CourtSettingsDto ToDto(Court c) => new(c.Id, c.Name, c.Indoor, c.SortOrder, c.IsActive);
    private static PriceRuleDto ToDto(PriceRule r) => new(r.Id, r.CourtId, r.DayType, r.StartHour, r.EndHour, r.PricePerHour, r.Label);
}
