namespace PickleballClub.Api.Services;

/// <summary>
/// A demo site (section <c>Demo</c>): a public copy of the Club's site for showing people, where nobody is charged.
/// It lets the mock payment provider and the simulate-payment endpoint run outside Development and tells the apps to say so.
/// Everything else stays as in production: no Swagger, no <c>/dev/emails</c>, real secrets required.
/// Never switch it on for a Club that takes money: anyone can mark a Booking as paid.
/// </summary>
public class DemoOptions
{
    public bool Enabled { get; set; }
}
