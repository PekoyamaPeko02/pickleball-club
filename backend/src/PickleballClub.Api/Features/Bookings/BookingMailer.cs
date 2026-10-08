using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PickleballClub.Api.Data;
using PickleballClub.Api.Domain;
using PickleballClub.Api.Services;

namespace PickleballClub.Api.Features.Bookings;

public class NotificationOptions
{
    /// <summary>Where "new booking" notices go. Empty = every Admin account's email address.</summary>
    public string AdminEmail { get; set; } = "";
    /// <summary>The reminder goes out this many hours before play.</summary>
    public int ReminderHoursBefore { get; set; } = 24;
}

/// <summary>Where a Booking was before it moved.</summary>
public record Placement(string CourtName, DateTime StartAt, DateTime EndAt);

/// <summary>
/// Emails about Bookings. Always called after the change is saved, and never lets a failed email undo or fail the
/// change: problems are logged and swallowed.
/// </summary>
public class BookingMailer(
    AppDbContext db, IEmailSender email, TimeProvider clock, IOptions<AppOptions> app, IOptions<NotificationOptions> options, ILogger<BookingMailer> log)
{
    private sealed record Facts(Booking Booking, string CourtName, ClubSettings Club, User? Customer)
    {
        public string When => Describe(Booking.StartAt, Booking.EndAt, Club.Timezone);
    }

    /// <summary>"Saturday 24 October 2026, 18:00–20:00" in the Club's local time.</summary>
    public static string Describe(DateTime startAt, DateTime endAt, string timezone)
    {
        var start = ClubClock.ToLocal(startAt, timezone);
        var hours = (int)Math.Round((endAt - startAt).TotalHours);
        return $"{start.ToString("dddd d MMMM yyyy", CultureInfo.InvariantCulture)}, {ClubClock.Label(start.Hour)}–{ClubClock.Label(start.Hour + hours)}";
    }

    private async Task<Facts?> LoadAsync(Guid bookingId, CancellationToken ct)
    {
        var booking = await db.Bookings.AsNoTracking().FirstOrDefaultAsync(b => b.Id == bookingId, ct);
        if (booking is null) return null;
        var court = await db.Courts.AsNoTracking().Where(c => c.Id == booking.CourtId).Select(c => c.Name).FirstAsync(ct);
        var club = await db.ClubSettings.AsNoTracking().SingleAsync(ct);
        var customer = booking.UserId is { } uid ? await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == uid, ct) : null;
        return new Facts(booking, court, club, customer);
    }

    private string LinkTo(Booking b) => $"{app.Value.WebUrl.TrimEnd('/')}/bookings/{b.Id}";

    private async Task SafelyAsync(string what, Guid bookingId, Func<Task> send)
    {
        try { await send(); }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            log.LogError(e, "Could not send the {What} email for booking {Booking}", what, bookingId);
        }
    }

    /// <summary>Payment arrived: tell the Customer, and tell the Club there is a new Booking.</summary>
    public Task ConfirmedAsync(Guid bookingId, CancellationToken ct) => SafelyAsync("confirmation", bookingId, async () =>
    {
        if (await LoadAsync(bookingId, ct) is not { Customer: { } customer } f) return;
        await email.SendAsync(EmailTemplates.BookingConfirmed(customer.Email, f.Club.Name, f.Booking.Code, f.CourtName, f.When, f.Booking.Total,
            f.Club.RescheduleNoticeHours, LinkTo(f.Booking)), ct);

        foreach (var to in await AdminAddressesAsync(ct))
            await email.SendAsync(EmailTemplates.AdminNewBooking(to, f.Club.Name, f.Booking.Code, f.CourtName, f.When, f.Booking.Total,
                customer.DisplayName ?? customer.Email, customer.Phone, app.Value.AdminUrl), ct);
    });

    /// <summary>The Customer used their Reschedule.</summary>
    public Task RescheduledAsync(Guid bookingId, Placement before, CancellationToken ct) => SafelyAsync("reschedule", bookingId, async () =>
    {
        if (await LoadAsync(bookingId, ct) is not { Customer: { } customer } f) return;
        var was = $"{before.CourtName}, {Describe(before.StartAt, before.EndAt, f.Club.Timezone)}";
        await email.SendAsync(EmailTemplates.BookingRescheduled(customer.Email, f.Club.Name, f.Booking.Code, was, $"{f.CourtName}, {f.When}", LinkTo(f.Booking)), ct);

        foreach (var to in await AdminAddressesAsync(ct))
            await email.SendAsync(EmailTemplates.AdminBookingMoved(to, f.Club.Name, f.Booking.Code, was, $"{f.CourtName}, {f.When}",
                customer.DisplayName ?? customer.Email, app.Value.AdminUrl), ct);
    });

    /// <summary>Admin Move: the Club changed the Customer's Court or time.</summary>
    public Task MovedByClubAsync(Guid bookingId, Placement before, CancellationToken ct) => SafelyAsync("admin move", bookingId, async () =>
    {
        if (await LoadAsync(bookingId, ct) is not { Customer: { } customer } f) return;
        var was = $"{before.CourtName}, {Describe(before.StartAt, before.EndAt, f.Club.Timezone)}";
        await email.SendAsync(EmailTemplates.BookingMovedByClub(customer.Email, f.Club.Name, f.Booking.Code, was, $"{f.CourtName}, {f.When}", LinkTo(f.Booking)), ct);
    });

    /// <summary>Admin Cancellation.</summary>
    public Task CancelledByClubAsync(Guid bookingId, CancellationToken ct) => SafelyAsync("cancellation", bookingId, async () =>
    {
        if (await LoadAsync(bookingId, ct) is not { Customer: { } customer } f) return;
        await email.SendAsync(EmailTemplates.BookingCancelledByClub(customer.Email, f.Club.Name, f.Booking.Code, f.CourtName, f.When, f.Booking.Total), ct);
    });

    /// <summary>
    /// Reminds Customers whose confirmed Booking starts within the reminder time, once each. A Booking made after its own
    /// reminder time (a last-minute Booking) gets none: its confirmation email is recent enough. Returns the number sent.
    /// </summary>
    public async Task<int> SendRemindersAsync(CancellationToken ct)
    {
        var hours = options.Value.ReminderHoursBefore;
        if (hours <= 0) return 0;
        var now = clock.GetUtcNow().UtcDateTime;
        var until = now.AddHours(hours);
        var due = await db.Bookings
            .Where(b => b.Status == BookingStatus.Confirmed && b.UserId != null && b.ReminderSentAt == null && b.StartAt > now && b.StartAt <= until)
            .OrderBy(b => b.StartAt).Take(200).ToListAsync(ct);

        var sent = 0;
        foreach (var booking in due)
        {
            // Claim it first, so a crash or a second instance cannot send it twice.
            if (await db.Bookings.Where(b => b.Id == booking.Id && b.ReminderSentAt == null)
                    .ExecuteUpdateAsync(s => s.SetProperty(b => b.ReminderSentAt, now), ct) == 0) continue;
            if (booking.CreatedAt > booking.StartAt.AddHours(-hours)) continue; // booked at the last minute

            await SafelyAsync("reminder", booking.Id, async () =>
            {
                if (await LoadAsync(booking.Id, ct) is not { Customer: { } customer } f) return;
                await email.SendAsync(EmailTemplates.BookingReminder(customer.Email, f.Club.Name, f.Booking.Code, f.CourtName, f.When, LinkTo(f.Booking)), ct);
                sent++;
            });
        }
        return sent;
    }

    private async Task<List<string>> AdminAddressesAsync(CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(options.Value.AdminEmail)) return [options.Value.AdminEmail.Trim()];
        return await db.Users.AsNoTracking().Where(u => u.Role == Roles.Admin).OrderBy(u => u.CreatedAt).Select(u => u.Email).ToListAsync(ct);
    }
}
