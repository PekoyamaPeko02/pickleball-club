using Microsoft.EntityFrameworkCore;
using PickleballClub.Api.Data;
using PickleballClub.Api.Domain;

namespace PickleballClub.Api.Services;

public record SlotDto(string Start, string End, DateTime StartAt, DateTime EndAt, decimal? Price, string? Label, string Status);

public record CourtAvailabilityDto(Guid CourtId, string Name, bool Indoor, List<SlotDto> Slots);

/// <param name="WithinBookingWindow">False when the date is outside today .. today + Booking Window, so a Customer cannot book it.</param>
public record AvailabilityDto(DateOnly Date, string DayType, bool WithinBookingWindow, List<CourtAvailabilityDto> Courts);

/// <summary>
/// The Slots of every active Court on one day of the Club.
/// Slot status: available | booked | held | blocked | past | closed (no Price Rule covers it).
/// "held" is shown separately so the UI can say "someone is paying for this".
/// </summary>
public class AvailabilityService(AppDbContext db, TimeProvider clock)
{
    public async Task<AvailabilityDto> ForDateAsync(DateOnly date, CancellationToken ct = default)
    {
        var club = await db.ClubSettings.AsNoTracking().SingleAsync(ct);
        var courts = await db.Courts.AsNoTracking().Where(c => c.IsActive).OrderBy(c => c.SortOrder).ThenBy(c => c.Name).ToListAsync(ct);
        var isAddedHoliday = await db.Holidays.AsNoTracking().AnyAsync(h => h.HolidayDate == date, ct);
        var dayType = Pricing.DayTypeOf(date, isAddedHoliday ? new HashSet<DateOnly> { date } : new HashSet<DateOnly>());

        var now = clock.GetUtcNow().UtcDateTime;
        var today = ClubClock.Today(club.Timezone, now);
        var withinWindow = date >= today && date <= today.AddDays(club.BookingWindowDays);

        var hours = await db.OperatingHours.AsNoTracking().FirstOrDefaultAsync(h => h.DayOfWeek == (short)date.DayOfWeek, ct);
        if (hours is null) // closed all day
            return new AvailabilityDto(date, dayType, withinWindow, courts.Select(c => ToCourt(c, [])).ToList());

        var dayStart = ClubClock.ToUtc(date, hours.OpenHour, club.Timezone);
        var dayEnd = ClubClock.ToUtc(date, hours.CloseHour, club.Timezone);
        var courtIds = courts.Select(c => c.Id).ToList();
        var rules = await db.PriceRules.AsNoTracking().Where(r => r.DayType == dayType).ToListAsync(ct);

        var taken = await db.Bookings.AsNoTracking()
            .Where(b => courtIds.Contains(b.CourtId)
                        && BookingStatus.Live.Contains(b.Status)
                        && b.StartAt < dayEnd && b.EndAt > dayStart)
            .Select(b => new { b.CourtId, b.StartAt, b.EndAt, b.Status })
            .ToListAsync(ct);

        var blocks = await db.CourtBlocks.AsNoTracking()
            .Where(b => courtIds.Contains(b.CourtId) && b.StartAt < dayEnd && b.EndAt > dayStart)
            .Select(b => new { b.CourtId, b.StartAt, b.EndAt })
            .ToListAsync(ct);

        var result = new List<CourtAvailabilityDto>();
        foreach (var court in courts)
        {
            var slots = new List<SlotDto>();
            for (int h = hours.OpenHour; h < hours.CloseHour; h++)
            {
                var startUtc = ClubClock.ToUtc(date, h, club.Timezone);
                var endUtc = ClubClock.ToUtc(date, h + 1, club.Timezone);
                var price = Pricing.PriceFor(rules, court.Id, dayType, h);

                var hit = taken.FirstOrDefault(b => b.CourtId == court.Id && b.StartAt < endUtc && b.EndAt > startUtc);
                var status =
                    startUtc <= now ? "past"
                    : blocks.Any(b => b.CourtId == court.Id && b.StartAt < endUtc && b.EndAt > startUtc) ? "blocked"
                    : hit is not null ? (hit.Status == BookingStatus.Held ? "held" : "booked")
                    : price is null ? "closed"
                    : "available";

                slots.Add(new SlotDto(ClubClock.Label(h), ClubClock.Label(h + 1), startUtc, endUtc, price?.Price, price?.Label, status));
            }
            result.Add(ToCourt(court, slots));
        }

        return new AvailabilityDto(date, dayType, withinWindow, result);
    }

    private static CourtAvailabilityDto ToCourt(Court c, List<SlotDto> slots) => new(c.Id, c.Name, c.Indoor, slots);
}
