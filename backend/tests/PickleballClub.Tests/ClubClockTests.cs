using PickleballClub.Api.Services;

namespace PickleballClub.Tests;

public class ClubClockTests
{
    private const string Bangkok = "Asia/Bangkok";
    private static readonly DateOnly Day = new(2026, 10, 23);

    [Fact]
    public void Bangkok_is_seven_hours_ahead_of_utc()
    {
        Assert.Equal(new DateTime(2026, 10, 23, 11, 0, 0, DateTimeKind.Utc), ClubClock.ToUtc(Day, 18, Bangkok));
        Assert.Equal(DateTimeKind.Utc, ClubClock.ToUtc(Day, 18, Bangkok).Kind);
    }

    [Fact]
    public void Early_morning_falls_on_the_previous_utc_day()
    {
        Assert.Equal(new DateTime(2026, 10, 22, 23, 0, 0, DateTimeKind.Utc), ClubClock.ToUtc(Day, 6, Bangkok));
    }

    [Fact]
    public void Hour_24_is_midnight_at_the_end_of_the_day()
    {
        Assert.Equal(ClubClock.ToUtc(Day.AddDays(1), 0, Bangkok), ClubClock.ToUtc(Day, 24, Bangkok));
    }

    [Fact]
    public void Today_is_the_clubs_local_date()
    {
        // 18:30 UTC on the 22nd is already 01:30 on the 23rd in Bangkok
        Assert.Equal(Day, ClubClock.Today(Bangkok, new DateTime(2026, 10, 22, 18, 30, 0, DateTimeKind.Utc)));
        Assert.Equal(Day, ClubClock.Today(Bangkok, new DateTime(2026, 10, 23, 16, 59, 0, DateTimeKind.Utc)));
        Assert.Equal(Day.AddDays(1), ClubClock.Today(Bangkok, new DateTime(2026, 10, 23, 17, 0, 0, DateTimeKind.Utc)));
    }

    [Theory]
    [InlineData(6, "06:00")]
    [InlineData(18, "18:00")]
    [InlineData(24, "00:00")]
    public void Labels_are_wall_clock_hours(int hour, string expected) => Assert.Equal(expected, ClubClock.Label(hour));
}
