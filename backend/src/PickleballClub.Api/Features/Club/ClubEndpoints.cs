using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PickleballClub.Api.Data;
using PickleballClub.Api.Services;

namespace PickleballClub.Api.Features.Club;

public record CourtDto(Guid Id, string Name, bool Indoor);

public record OperatingHoursDto(int DayOfWeek, int OpenHour, int CloseHour);

/// <param name="Today">Today's date at the Club.</param>
/// <param name="LastBookableDate">The last day inside the Booking Window.</param>
/// <param name="Demo">True on a demo site: payments are simulated and nothing booked here is real. The apps say so.</param>
public record ClubDto(
    string Name, string Timezone, int BookingWindowDays, int RescheduleNoticeHours,
    DateOnly Today, DateOnly LastBookableDate, List<CourtDto> Courts, List<OperatingHoursDto> OperatingHours, bool Demo);

/// <summary>Public, read-only information: anyone may see the Club's Courts, hours, free Slots and prices without signing in.</summary>
public static class ClubEndpoints
{
    public static void MapClub(this RouteGroupBuilder api)
    {
        api.MapGet("/club", async (AppDbContext db, TimeProvider clock, IOptions<DemoOptions> demo, CancellationToken ct) =>
        {
            var club = await db.ClubSettings.AsNoTracking().SingleAsync(ct);
            var courts = await db.Courts.AsNoTracking().Where(c => c.IsActive).OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
                .Select(c => new CourtDto(c.Id, c.Name, c.Indoor)).ToListAsync(ct);
            var hours = await db.OperatingHours.AsNoTracking().OrderBy(h => h.DayOfWeek)
                .Select(h => new OperatingHoursDto(h.DayOfWeek, h.OpenHour, h.CloseHour)).ToListAsync(ct);
            var today = ClubClock.Today(club.Timezone, clock.GetUtcNow().UtcDateTime);

            return new ClubDto(club.Name, club.Timezone, club.BookingWindowDays, club.RescheduleNoticeHours,
                today, today.AddDays(club.BookingWindowDays), courts, hours, demo.Value.Enabled);
        });

        // ?date=yyyy-MM-dd (the Club's local date); defaults to today at the Club.
        api.MapGet("/availability", async (DateOnly? date, AvailabilityService availability, AppDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            if (date is null)
            {
                var tz = await db.ClubSettings.AsNoTracking().Select(c => c.Timezone).SingleAsync(ct);
                date = ClubClock.Today(tz, clock.GetUtcNow().UtcDateTime);
            }
            return await availability.ForDateAsync(date.Value, ct);
        });
    }
}
