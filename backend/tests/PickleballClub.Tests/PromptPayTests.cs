using PickleballClub.Api.Services;

namespace PickleballClub.Tests;

public class PromptPayTests
{
    [Fact]
    public void Crc16_matches_ccitt_false_check_value() =>
        Assert.Equal(0x29B1, PromptPay.Crc16("123456789"));

    [Fact]
    public void Payload_for_mobile_with_amount()
    {
        var p = PromptPay.Payload("081-234-5678", 1250m);
        Assert.StartsWith("000201010212", p);                 // dynamic QR
        Assert.Contains("29370016A000000677010111011300668123456785303764", p);
        Assert.Contains("54071250.00", p);
        Assert.Contains("5802TH6304", p);
        Assert.Equal(PromptPay.Crc16(p[..^4]).ToString("X4"), p[^4..]);
    }
}
