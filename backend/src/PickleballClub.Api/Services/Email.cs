using System.Collections.Concurrent;
using System.Net;

namespace PickleballClub.Api.Services;

/// <param name="Kind">A stable name for the kind of email (e.g. "verify_email"), for logs and tests.</param>
public record EmailMessage(string To, string Subject, string TextBody, string HtmlBody, string Kind);

public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken ct = default);
}

/// <summary>
/// Development sender: nothing leaves the machine. Each email is logged and the last 200 are kept in memory so the
/// dev-only endpoint <c>GET /api/v1/dev/emails</c> can show them (e.g. to copy a reset link).
/// TODO(provider): the Club has not chosen an email provider yet; a real sender replaces this through DI.
/// </summary>
public sealed class DevEmailSender(ILogger<DevEmailSender> log) : IEmailSender
{
    private const int Keep = 200;
    private readonly ConcurrentQueue<SentEmail> _sent = new();

    public record SentEmail(DateTime SentAt, string To, string Subject, string Kind, string TextBody);

    public IReadOnlyCollection<SentEmail> Sent => _sent.Reverse().ToList();

    public Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        _sent.Enqueue(new SentEmail(DateTime.UtcNow, message.To, message.Subject, message.Kind, message.TextBody));
        while (_sent.Count > Keep) _sent.TryDequeue(out _);
        log.LogInformation("Email ({Kind}) to {To}: {Subject}\n{Body}", message.Kind, message.To, message.Subject, message.TextBody);
        return Task.CompletedTask;
    }
}

public class AppOptions
{
    /// <summary>Public address of the customer site; links in emails point here.</summary>
    public string WebUrl { get; set; } = "http://localhost:9010";
    /// <summary>Address of the back office.</summary>
    public string AdminUrl { get; set; } = "http://localhost:9011";
}

/// <summary>The words of every email the system sends. English, plain and short; the HTML is the same text with a button.</summary>
public static class EmailTemplates
{
    public static EmailMessage VerifyEmail(string to, string clubName, string link) => Build(to, "verify_email",
        $"Confirm your email for {clubName}",
        ["Welcome! Please confirm that this is your email address.", "This link works for 48 hours."],
        "Confirm my email", link);

    public static EmailMessage ResetPassword(string to, string clubName, string link) => Build(to, "reset_password",
        $"Reset your {clubName} password",
        ["We received a request to reset your password. Use the link below to choose a new one.",
         "This link works for 2 hours and only once. If you did not ask for it, you can ignore this email."],
        "Choose a new password", link);

    // ---------------------------------------------------------------- bookings

    private static string Baht(decimal amount) => $"THB {amount:N0}";

    public static EmailMessage BookingConfirmed(string to, string clubName, string code, string court, string when, decimal total, int noticeHours, string link) =>
        Build(to, "booking_confirmed", $"You are booked — {code}",
            [$"Your court at {clubName} is confirmed.",
             $"Booking code: {code}\n{court}\n{when}\nPaid: {Baht(total)}",
             "Show the booking code at the club when you arrive.",
             $"Bookings are non-refundable. You can move this booking once, to a time of the same length that costs the same or less, up to {noticeHours} hours before it starts."],
            "See or move your booking", link);

    public static EmailMessage BookingRescheduled(string to, string clubName, string code, string was, string now, string link) =>
        Build(to, "booking_rescheduled", $"Your booking has been moved — {code}",
            [$"Your booking {code} at {clubName} has been moved, as you asked.",
             $"New time: {now}\nWas: {was}",
             "A booking can be moved only once, so this one cannot be moved again."],
            "See your booking", link);

    public static EmailMessage BookingMovedByClub(string to, string clubName, string code, string was, string now, string link) =>
        Build(to, "booking_moved_by_club", $"{clubName} has moved your booking — {code}",
            [$"We had to move your booking {code}. We are sorry for the change.",
             $"New time: {now}\nWas: {was}",
             "If the new time does not suit you, please contact the club."],
            "See your booking", link);

    public static EmailMessage BookingCancelledByClub(string to, string clubName, string code, string court, string when, decimal total) =>
        Build(to, "booking_cancelled", $"{clubName} has cancelled your booking — {code}",
            [$"We are sorry: we had to cancel your booking {code}.",
             $"{court}\n{when}",
             $"The club is refunding {Baht(total)} to you directly. Please contact the club if you have any questions."]);

    public static EmailMessage BookingReminder(string to, string clubName, string code, string court, string when, string link) =>
        Build(to, "booking_reminder", $"See you soon at {clubName} — {code}",
            [$"A reminder of your booking at {clubName}.",
             $"Booking code: {code}\n{court}\n{when}",
             "Show the booking code at the club when you arrive."],
            "See your booking", link);

    public static EmailMessage AdminNewBooking(string to, string clubName, string code, string court, string when, decimal total, string customer, string? phone, string adminUrl) =>
        Build(to, "admin_new_booking", $"New booking {code}: {court}, {when}",
            [$"A new online booking at {clubName} has been paid.",
             $"Booking code: {code}\n{court}\n{when}\nPaid: {Baht(total)}\nCustomer: {customer}{(phone is null ? "" : $", {phone}")}"],
            "Open the schedule", adminUrl);

    public static EmailMessage AdminBookingMoved(string to, string clubName, string code, string was, string now, string customer, string adminUrl) =>
        Build(to, "admin_booking_moved", $"Booking {code} moved by the customer",
            [$"{customer} has moved booking {code} at {clubName}.",
             $"New time: {now}\nWas: {was}"],
            "Open the schedule", adminUrl);

    internal static EmailMessage Build(string to, string kind, string subject, IReadOnlyList<string> paragraphs, string? action = null, string? link = null)
    {
        var text = string.Join("\n\n", paragraphs) + (link is null ? "" : $"\n\n{action}: {link}");
        var html = string.Concat(paragraphs.Select(p => $"<p>{WebUtility.HtmlEncode(p).Replace("\n", "<br>")}</p>"))
                   + (link is null ? "" : $"<p><a href=\"{WebUtility.HtmlEncode(link)}\">{WebUtility.HtmlEncode(action)}</a></p>");
        return new EmailMessage(to, subject, text, html, kind);
    }
}
