using PickleballClub.Api.Features.Auth;

namespace PickleballClub.Tests;

public class AuthHelpersTests
{
    [Theory]
    [InlineData("0812345678", "0812345678")]
    [InlineData("081-234-5678", "0812345678")]
    [InlineData("+66 81 234 5678", "+66812345678")]
    [InlineData("+1 (415) 555-2671", "+14155552671")]
    [InlineData(" 02.123.4567 ", "021234567")]
    public void Phone_numbers_keep_digits_and_a_leading_plus(string raw, string expected) =>
        Assert.Equal(expected, AuthEndpoints.NormalizePhone(raw));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("call me")]
    [InlineData("12345")]               // too short
    [InlineData("1234567890123456")]    // too long
    [InlineData("08+12345678")]         // + only at the start
    [InlineData("0812345678 ext 2")]
    public void Anything_else_is_not_a_phone_number(string? raw) =>
        Assert.Null(AuthEndpoints.NormalizePhone(raw));

    [Fact]
    public void Emails_are_trimmed_and_lower_cased() =>
        Assert.Equal("nok@example.com", AuthEndpoints.NormalizeEmail("  Nok@Example.COM "));

    [Fact]
    public void The_password_stamp_changes_with_every_password_change()
    {
        Assert.Equal("0", Sessions.Stamp(null));
        var t = new DateTime(2026, 10, 8, 3, 0, 0, DateTimeKind.Utc);
        Assert.NotEqual(Sessions.Stamp(t), Sessions.Stamp(t.AddMilliseconds(1)));
    }
}
