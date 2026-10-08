using PickleballClub.Api.Domain;
using PickleballClub.Api.Services;

namespace PickleballClub.Tests;

public class PricingTests
{
    private static readonly Guid Court1 = Guid.NewGuid();
    private static readonly Guid Court2 = Guid.NewGuid();
    private static readonly HashSet<DateOnly> NoHolidays = [];

    private static PriceRule Rule(int start, int end, decimal price, string dayType = DayTypes.Weekday,
        Guid? court = null, string? label = null) => new()
    {
        Id = Guid.NewGuid(), StartHour = (short)start, EndHour = (short)end,
        PricePerHour = price, DayType = dayType, CourtId = court, Label = label,
    };

    private static readonly List<PriceRule> Club =
    [
        Rule(6, 16, 300, label: "Off-peak"),
        Rule(16, 23, 400, label: "Peak"),
        Rule(6, 23, 400, DayTypes.Holiday, label: "Weekend & holiday"),
        Rule(16, 23, 450, court: Court1, label: "Indoor peak"),
    ];

    [Theory]
    [InlineData(2026, 10, 23, DayTypes.Weekday)] // Friday
    [InlineData(2026, 10, 24, DayTypes.Holiday)] // Saturday
    [InlineData(2026, 10, 25, DayTypes.Holiday)] // Sunday
    [InlineData(2026, 10, 26, DayTypes.Weekday)] // Monday
    public void Saturday_and_Sunday_are_holidays(int y, int m, int d, string expected) =>
        Assert.Equal(expected, Pricing.DayTypeOf(new DateOnly(y, m, d), NoHolidays));

    [Fact]
    public void A_day_the_admin_added_is_a_holiday_even_on_a_weekday()
    {
        var songkran = new DateOnly(2026, 4, 13); // a Monday
        Assert.Equal(DayTypes.Weekday, Pricing.DayTypeOf(songkran, NoHolidays));
        Assert.Equal(DayTypes.Holiday, Pricing.DayTypeOf(songkran, new HashSet<DateOnly> { songkran }));
    }

    [Theory]
    [InlineData(6, 300, "Off-peak")]
    [InlineData(15, 300, "Off-peak")]
    [InlineData(16, 400, "Peak")]
    [InlineData(22, 400, "Peak")]
    public void Weekday_price_follows_the_hour_band(int hour, decimal expected, string label)
    {
        var p = Pricing.PriceFor(Club, Court2, DayTypes.Weekday, hour);
        Assert.NotNull(p);
        Assert.Equal(expected, p!.Value.Price);
        Assert.Equal(label, p.Value.Label);
    }

    [Fact]
    public void A_band_ends_before_its_end_hour()
    {
        Assert.Null(Pricing.PriceFor(Club, Court2, DayTypes.Weekday, 23));
        Assert.Null(Pricing.PriceFor(Club, Court2, DayTypes.Weekday, 5));
    }

    [Fact]
    public void Holiday_uses_only_holiday_rules()
    {
        Assert.Equal(400m, Pricing.PriceFor(Club, Court2, DayTypes.Holiday, 8)!.Value.Price);
        // the Court 1 evening rule is a weekday rule, so it does not apply on a holiday
        Assert.Equal(400m, Pricing.PriceFor(Club, Court1, DayTypes.Holiday, 18)!.Value.Price);
    }

    [Fact]
    public void A_court_rule_beats_the_club_wide_rule()
    {
        Assert.Equal(450m, Pricing.PriceFor(Club, Court1, DayTypes.Weekday, 18)!.Value.Price);
        Assert.Equal(400m, Pricing.PriceFor(Club, Court2, DayTypes.Weekday, 18)!.Value.Price);
        // outside the Court rule's hours the Club-wide rule still applies to that Court
        Assert.Equal(300m, Pricing.PriceFor(Club, Court1, DayTypes.Weekday, 10)!.Value.Price);
    }

    [Fact]
    public void A_rule_can_run_until_midnight()
    {
        var late = new[] { Rule(20, 24, 500) };
        Assert.Equal(500m, Pricing.PriceFor(late, Court1, DayTypes.Weekday, 23)!.Value.Price);
    }

    [Fact]
    public void Rules_overlap_only_with_the_same_scope_and_day_type()
    {
        Assert.True(Pricing.Overlap(Rule(6, 16, 300), Rule(15, 20, 350)));
        Assert.False(Pricing.Overlap(Rule(6, 16, 300), Rule(16, 23, 400)));                       // touching, not overlapping
        Assert.False(Pricing.Overlap(Rule(6, 16, 300), Rule(6, 16, 400, DayTypes.Holiday)));       // different Day Type
        Assert.False(Pricing.Overlap(Rule(6, 16, 300), Rule(6, 16, 350, court: Court1)));          // Club-wide vs one Court
        Assert.True(Pricing.Overlap(Rule(6, 16, 300, court: Court1), Rule(10, 12, 350, court: Court1)));
        Assert.False(Pricing.Overlap(Rule(6, 16, 300, court: Court1), Rule(10, 12, 350, court: Court2)));
    }
}
