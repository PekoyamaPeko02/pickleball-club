using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PickleballClub.Api.Features.Bookings;
using PickleballClub.Api.Services;
using PickleballClub.IntegrationTests.Infrastructure;

namespace PickleballClub.IntegrationTests;

[Collection(ApiCollection.Name)]
public class EmailTests(ApiFactory api)
{
    private string AdminEmail => api.Services.GetRequiredService<IConfiguration>()["Admin:Email"]!;

    private List<EmailMessage> About(string code) => api.Emails.Where(e => e.TextBody.Contains(code) || e.Subject.Contains(code)).ToList();

    [Fact]
    public async Task Paying_sends_the_customer_a_confirmation_and_tells_the_club()
    {
        var customer = await api.RegisterAsync(displayName: "Nok Mail", phone: "0817778888");
        var pick = SlotPicker.Next(api, weekday: true, startHour: 10);

        var held = await customer.BookAsync(pick);
        var code = held.GetProperty("code").GetString()!;
        Assert.Empty(About(code)); // nothing is sent for a Hold

        await api.PayAsync(held.Id());
        var mine = Assert.Single(About(code), e => e.Kind == "booking_confirmed");
        Assert.Equal(customer.Email, mine.To);
        Assert.Contains(code, mine.Subject);
        Assert.Contains(BookingMailer.Describe(pick.StartAt, pick.StartAt.AddHours(2), Pick.Bangkok), mine.TextBody); // e.g. "Thursday 15 October 2026, 10:00–12:00"
        Assert.Contains("10:00–12:00", mine.TextBody);
        Assert.Contains("THB 600", mine.TextBody);
        Assert.Contains("non-refundable", mine.TextBody);
        Assert.Contains($"http://localhost:9010/bookings/{held.Id()}", mine.TextBody);

        var club = Assert.Single(About(code), e => e.Kind == "admin_new_booking");
        Assert.Equal(AdminEmail, club.To);
        Assert.Contains("Nok Mail", club.TextBody);
        Assert.Contains("0817778888", club.TextBody);

        // A repeated webhook does not send them again.
        await api.Anonymous().PostAsync($"/api/v1/dev/bookings/{held.Id()}/simulate-payment", null);
        Assert.Equal(2, About(code).Count);
    }

    [Fact]
    public async Task Moving_and_cancelling_are_each_announced_to_the_customer()
    {
        var admin = await api.AdminAsync();
        var customer = await api.RegisterAsync();
        var (a, b, c) = (SlotPicker.Next(api, weekday: false), SlotPicker.Next(api, weekday: false), SlotPicker.Next(api, weekday: false));
        var booked = await api.BookAndPayAsync(customer, a);
        var code = booked.GetProperty("code").GetString()!;
        object Place(Pick p) => new { courtId = p.CourtId, date = p.Date.ToString("yyyy-MM-dd"), startHour = p.StartHour };

        // the Customer's own Reschedule: to them, and a notice to the Club
        Assert.Equal(HttpStatusCode.OK, (await customer.Http.PostAsJsonAsync($"/api/v1/me/bookings/{booked.Id()}/reschedule", Place(b))).StatusCode);
        var moved = Assert.Single(About(code), e => e.Kind == "booking_rescheduled");
        Assert.Equal(customer.Email, moved.To);
        Assert.Contains("New time: " + booked.GetProperty("courtName").GetString()![..5], moved.TextBody); // "Court"
        Assert.Contains(BookingMailer.Describe(b.StartAt, b.StartAt.AddHours(2), Pick.Bangkok), moved.TextBody);
        Assert.Contains("Was: ", moved.TextBody);
        Assert.Contains(BookingMailer.Describe(a.StartAt, a.StartAt.AddHours(2), Pick.Bangkok), moved.TextBody);
        Assert.Equal(AdminEmail, Assert.Single(About(code), e => e.Kind == "admin_booking_moved").To);

        // an Admin Move
        Assert.Equal(HttpStatusCode.OK, (await admin.Http.PostAsJsonAsync($"/api/v1/admin/bookings/{booked.Id()}/move", Place(c))).StatusCode);
        var byClub = Assert.Single(About(code), e => e.Kind == "booking_moved_by_club");
        Assert.Equal(customer.Email, byClub.To);
        Assert.Contains(BookingMailer.Describe(c.StartAt, c.StartAt.AddHours(2), Pick.Bangkok), byClub.TextBody);

        // an Admin Cancellation
        Assert.Equal(HttpStatusCode.OK, (await admin.Http.PostAsJsonAsync($"/api/v1/admin/bookings/{booked.Id()}/cancel", new { refundNote = "cash back at the counter" })).StatusCode);
        var cancelled = Assert.Single(About(code), e => e.Kind == "booking_cancelled");
        Assert.Equal(customer.Email, cancelled.To);
        Assert.Contains("THB 800", cancelled.TextBody);
        Assert.DoesNotContain("cash back at the counter", cancelled.TextBody); // the Admin's note is not for the customer
    }

    [Fact]
    public async Task A_walk_in_guest_has_no_email_address_so_nothing_is_sent()
    {
        var admin = await api.AdminAsync();
        var pick = SlotPicker.Next(api);
        var res = await admin.WalkInAsync(pick.CourtId, pick.Date, pick.StartHour);
        var booking = await res.Content.ReadFromJsonAsync<JsonElement>();
        await admin.Http.PostAsJsonAsync($"/api/v1/admin/bookings/{booking.Id()}/cancel", new { refundNote = "cash back" });
        Assert.Empty(About(booking.GetProperty("code").GetString()!));
    }
}

[Collection(SettingsCollection.Name)]
public class ReminderTests(ApiFactory api)
{
    private Task<int> SendReminders() => api.WithScope(sp => sp.GetRequiredService<BookingMailer>().SendRemindersAsync(default));

    [Fact]
    public async Task A_reminder_goes_out_once_a_day_before_play_but_not_for_a_last_minute_booking()
    {
        var admin = await api.AdminAsync();
        var customer = await api.RegisterAsync();
        // Its own Court, so the clock can be moved by days without meeting another test's Bookings.
        var court = (await (await admin.Http.PostAsJsonAsync("/api/v1/admin/courts", new { name = "Reminder " + Guid.NewGuid().ToString("N")[..6], indoor = false, sortOrder = 90 }))
            .Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var pick = new Pick(court, api.ClubToday().AddDays(3), 10);
        var booked = await api.BookAndPayAsync(customer, pick);
        var code = booked.GetProperty("code").GetString()!;
        List<EmailMessage> Reminders() => api.Emails.Where(e => e.Kind == "booking_reminder" && e.TextBody.Contains(code)).ToList();

        await SendReminders();
        Assert.Empty(Reminders()); // three days away

        // 23 hours before play
        api.Clock.Advance(pick.StartAt.AddHours(-23) - api.Clock.GetUtcNow().UtcDateTime);
        Assert.True(await SendReminders() >= 1);
        var reminder = Assert.Single(Reminders());
        Assert.Equal(customer.Email, reminder.To);
        Assert.Contains(BookingMailer.Describe(pick.StartAt, pick.StartAt.AddHours(2), Pick.Bangkok), reminder.TextBody);

        await SendReminders();
        Assert.Single(Reminders()); // only once

        // A Booking made now for later today: its confirmation email is the reminder.
        var (today, hour) = api.EnsureOpenNow();
        if (hour < 20)
        {
            var late = await api.BookAndPayAsync(await api.RegisterAsync(), new Pick(court, today, hour + 2), hours: 1);
            await SendReminders();
            Assert.DoesNotContain(api.Emails, e => e.Kind == "booking_reminder" && e.TextBody.Contains(late.GetProperty("code").GetString()!));
        }
    }
}
