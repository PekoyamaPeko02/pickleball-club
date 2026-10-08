namespace PickleballClub.Api.Services;

/// <summary>Converts between the Club's local wall-clock time and UTC.</summary>
public static class ClubClock
{
    public static TimeZoneInfo Zone(string tz)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(tz); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.CreateCustomTimeZone(tz, TimeSpan.FromHours(7), tz, tz); }
    }

    /// <summary>UTC instant of <paramref name="hour"/>:00 on the Club's local <paramref name="date"/>; hour 24 is midnight at the end of that day.</summary>
    public static DateTime ToUtc(DateOnly date, int hour, string tz)
    {
        var local = DateTime.SpecifyKind(date.ToDateTime(TimeOnly.MinValue).AddHours(hour), DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(local, Zone(tz));
    }

    public static DateTime ToLocal(DateTime utc, string tz) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Zone(tz));

    public static DateOnly Today(string tz, DateTime nowUtc) =>
        DateOnly.FromDateTime(ToLocal(nowUtc, tz));

    /// <summary>"18:00"; hour 24 reads "00:00".</summary>
    public static string Label(int hour) => $"{hour % 24:00}:00";
}
