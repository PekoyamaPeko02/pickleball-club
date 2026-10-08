using PickleballClub.Api.Domain;

namespace PickleballClub.Api.Services;

public readonly record struct SlotPrice(decimal Price, string? Label);

/// <summary>Pure pricing logic — kept free of EF so it is easy to unit test.</summary>
public static class Pricing
{
    /// <summary>Saturdays, Sundays and the days the Admin added are holidays; every other day is a weekday.</summary>
    public static string DayTypeOf(DateOnly date, IReadOnlySet<DateOnly> holidays) =>
        date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday || holidays.Contains(date)
            ? DayTypes.Holiday
            : DayTypes.Weekday;

    /// <summary>
    /// Price of the one-hour Slot starting at <paramref name="startHour"/>. A rule applies when it covers the
    /// Slot's start hour, matches the Day Type and is either Club-wide or for this Court. A rule for the Court
    /// beats a Club-wide one. Returns null when no rule covers the Slot.
    /// </summary>
    public static SlotPrice? PriceFor(IEnumerable<PriceRule> rules, Guid courtId, string dayType, int startHour)
    {
        var rule = rules
            .Where(r => r.CourtId is null || r.CourtId == courtId)
            .Where(r => r.DayType == dayType)
            .Where(r => startHour >= r.StartHour && startHour < r.EndHour)
            .OrderByDescending(r => r.CourtId.HasValue)
            .FirstOrDefault();

        return rule is null ? null : new SlotPrice(rule.PricePerHour, rule.Label);
    }

    /// <summary>
    /// True when <paramref name="a"/> and <paramref name="b"/> have the same scope (same Court, or both Club-wide)
    /// and Day Type and share at least one hour — <see cref="PriceFor"/> would then pick one arbitrarily, so such
    /// a pair must not be stored.
    /// </summary>
    public static bool Overlap(PriceRule a, PriceRule b) =>
        a.CourtId == b.CourtId
        && a.DayType == b.DayType
        && a.StartHour < b.EndHour && b.StartHour < a.EndHour;
}
