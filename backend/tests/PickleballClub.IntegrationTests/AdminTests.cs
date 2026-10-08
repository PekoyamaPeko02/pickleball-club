using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PickleballClub.Api.Features.Admin;
using PickleballClub.Api.Features.Bookings;
using PickleballClub.IntegrationTests.Infrastructure;

namespace PickleballClub.IntegrationTests;

[Collection(ApiCollection.Name)]
public class AdminTests(ApiFactory api)
{
    private static async Task AssertError(HttpResponseMessage res, HttpStatusCode status, string code)
    {
        var body = await res.Content.ReadAsStringAsync();
        Assert.True(res.StatusCode == status, $"expected {(int)status} {code}, got {(int)res.StatusCode}: {body}");
        Assert.Equal(code, JsonDocument.Parse(body).RootElement.GetProperty("code").GetString());
    }

    private static async Task<JsonElement> Ok(Task<HttpResponseMessage> call, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var res = await call;
        var body = await res.Content.ReadAsStringAsync();
        Assert.True(res.StatusCode == expected, $"expected {(int)expected}, got {(int)res.StatusCode}: {body}");
        return JsonDocument.Parse(body).RootElement;
    }

    private static object Place(Pick p, int? startHour = null) => new { courtId = p.CourtId, date = p.Date.ToString("yyyy-MM-dd"), startHour = startHour ?? p.StartHour };

    private static Task<HttpResponseMessage> Block(TestUser admin, Pick p, int hours = 2, string reason = "Resurfacing") =>
        admin.Http.PostAsJsonAsync("/api/v1/admin/blocks", new { courtId = p.CourtId, date = p.Date.ToString("yyyy-MM-dd"), startHour = p.StartHour, hours, reason });

    private Task<JsonElement> Schedule(TestUser admin, DateOnly date) =>
        admin.Http.GetFromJsonAsync<JsonElement>($"/api/v1/admin/schedule?date={date:yyyy-MM-dd}");

    // ---------------------------------------------------------------- access

    [Fact]
    public async Task The_back_office_is_for_admins_only()
    {
        var customer = await api.RegisterAsync();
        var pick = SlotPicker.Next(api);

        await AssertError(await api.Anonymous().GetAsync("/api/v1/admin/schedule"), HttpStatusCode.Unauthorized, "unauthorized");
        await AssertError(await customer.Http.GetAsync("/api/v1/admin/schedule"), HttpStatusCode.Forbidden, "forbidden");
        // 403 before any validation detail leaks
        await AssertError(await customer.Http.PostAsJsonAsync("/api/v1/admin/bookings/walk-in", new { }), HttpStatusCode.Forbidden, "forbidden");
        await AssertError(await customer.WalkInAsync(pick.CourtId, pick.Date, pick.StartHour), HttpStatusCode.Forbidden, "forbidden");
        await AssertError(await customer.Http.GetAsync("/api/v1/admin/refunds"), HttpStatusCode.Forbidden, "forbidden");
        Assert.Equal("available", await api.SlotStatusAsync(pick));
    }

    // ---------------------------------------------------------------- walk-in

    [Fact]
    public async Task A_walk_in_booking_is_confirmed_at_once_for_a_guest_without_an_account()
    {
        var admin = await api.AdminAsync();
        var pick = SlotPicker.Next(api, weekday: true, startHour: 10);

        var b = await Ok(admin.WalkInAsync(pick.CourtId, pick.Date, pick.StartHour, hours: 2, guestName: "  Khun Somsak ", guestPhone: "089-000-1111", payment: "transfer"), HttpStatusCode.Created);
        Assert.Equal("confirmed", b.Status());
        Assert.Equal("walk_in", b.GetProperty("source").GetString());
        Assert.Equal("Khun Somsak", b.GetProperty("customerName").GetString());
        Assert.Equal("0890001111", b.GetProperty("customerPhone").GetString());
        Assert.Equal(JsonValueKind.Null, b.GetProperty("customerEmail").ValueKind);
        Assert.Equal("counter", b.GetProperty("paymentState").GetString());
        Assert.Equal("transfer", b.GetProperty("paymentMethod").GetString());
        Assert.Equal(600m, b.GetProperty("total").GetDecimal()); // priced by the same rules as an online Booking
        Assert.Equal("booked", await api.SlotStatusAsync(pick));

        await AssertError(await admin.WalkInAsync(pick.CourtId, pick.Date, pick.StartHour + 1), HttpStatusCode.Conflict, "slot_taken");
        await AssertError(await admin.WalkInAsync(pick.CourtId, pick.Date, 5), HttpStatusCode.BadRequest, "outside_hours");
        await AssertError(await admin.WalkInAsync(pick.CourtId, pick.Date, 12, guestPhone: "n/a"), HttpStatusCode.BadRequest, "bad_phone");
        await AssertError(await admin.WalkInAsync(pick.CourtId, pick.Date, 12, guestName: " "), HttpStatusCode.BadRequest, "validation_failed");
        await AssertError(await admin.WalkInAsync(pick.CourtId, pick.Date, 12, payment: "credit"), HttpStatusCode.BadRequest, "validation_failed");
    }

    [Fact]
    public async Task A_walk_in_booking_is_not_bound_by_the_booking_window()
    {
        var admin = await api.AdminAsync();
        var customer = await api.RegisterAsync();
        var far = new Pick(SlotPicker.Courts[3], api.ClubToday().AddDays(40), 10);

        await AssertError(await customer.HoldAsync(far), HttpStatusCode.BadRequest, "outside_booking_window");
        Assert.Equal("confirmed", (await Ok(admin.WalkInAsync(far.CourtId, far.Date, far.StartHour, hours: 3), HttpStatusCode.Created)).Status());
    }

    // ---------------------------------------------------------------- schedule

    [Fact]
    public async Task The_schedule_shows_who_has_each_court_and_which_are_blocked()
    {
        var admin = await api.AdminAsync();
        var customer = await api.RegisterAsync(displayName: "Nok Online", phone: "0812223333");
        var day = SlotPicker.NextRun(api, blocks: 4); // four blocks on one Court and day
        var online = await api.BookAndPayAsync(customer, day);
        var walkIn = await Ok(admin.WalkInAsync(day.CourtId, day.Date, day.StartHour + 2), HttpStatusCode.Created);
        var block = await Ok(Block(admin, day with { StartHour = day.StartHour + 4 }, reason: "Tournament"), HttpStatusCode.Created);
        var stranger = await api.RegisterAsync();
        var ranOut = await stranger.BookAsync(day with { StartHour = day.StartHour + 6 });
        await stranger.Http.PostAsync($"/api/v1/me/bookings/{ranOut.Id()}/release", null);

        var schedule = await Schedule(admin, day.Date);
        Assert.Equal(day.Date.ToString("yyyy-MM-dd"), schedule.GetProperty("availability").GetProperty("date").GetString());

        // Other tests may have Bookings elsewhere on this Court and day: look at the eight hours this test owns.
        bool InRun(JsonElement x) => x.GetProperty("courtId").GetGuid() == day.CourtId
                                     && x.GetProperty("startAt").GetDateTime().ToUniversalTime() is var at && at >= day.StartAt && at < day.StartAt.AddHours(8);
        var mine = schedule.GetProperty("bookings").EnumerateArray().Where(InRun).ToList();
        Assert.Equal(new[] { online.Id(), walkIn.Id() }, mine.Select(b => b.Id()).ToArray()); // by start time; the released Hold is left out

        var first = mine[0];
        Assert.Equal("Nok Online", first.GetProperty("customerName").GetString());
        Assert.Equal("0812223333", first.GetProperty("customerPhone").GetString());
        Assert.Equal(customer.Email, first.GetProperty("customerEmail").GetString());
        Assert.Equal("paid", first.GetProperty("paymentState").GetString());
        Assert.Equal("promptpay", first.GetProperty("paymentMethod").GetString());
        Assert.Equal("online", first.GetProperty("source").GetString());

        var blocks = schedule.GetProperty("blocks").EnumerateArray().Where(InRun).ToList();
        Assert.Equal(block.GetProperty("id").GetGuid(), Assert.Single(blocks).GetProperty("id").GetGuid());
        Assert.Equal("Tournament", blocks[0].GetProperty("reason").GetString());

        // Looking a Booking up by the code the customer shows.
        var code = online.GetProperty("code").GetString()!;
        Assert.Equal(online.Id(), (await Ok(admin.Http.GetAsync($"/api/v1/admin/bookings/by-code/{code.ToLowerInvariant()}"))).Id());
        await AssertError(await admin.Http.GetAsync("/api/v1/admin/bookings/by-code/PB0"), HttpStatusCode.NotFound, "booking_not_found");
        await AssertError(await admin.Http.GetAsync($"/api/v1/admin/bookings/{Guid.NewGuid()}"), HttpStatusCode.NotFound, "booking_not_found");
    }

    // ---------------------------------------------------------------- court block

    [Fact]
    public async Task A_court_block_takes_the_court_out_of_sale_until_it_is_removed()
    {
        var admin = await api.AdminAsync();
        var customer = await api.RegisterAsync();
        var pick = SlotPicker.NextRun(api, blocks: 2);

        var block = await Ok(Block(admin, pick), HttpStatusCode.Created);
        Assert.Equal("blocked", await api.SlotStatusAsync(pick));
        Assert.Equal("blocked", await api.SlotStatusAsync(pick, pick.StartHour + 1));
        Assert.Equal("available", await api.SlotStatusAsync(pick, pick.StartHour + 2));
        await AssertError(await customer.HoldAsync(pick), HttpStatusCode.Conflict, "slot_blocked");
        await AssertError(await admin.WalkInAsync(pick.CourtId, pick.Date, pick.StartHour), HttpStatusCode.Conflict, "slot_blocked");
        await AssertError(await Block(admin, pick with { StartHour = pick.StartHour + 1 }), HttpStatusCode.Conflict, "block_overlap");
        await AssertError(await Block(admin, pick, reason: " "), HttpStatusCode.BadRequest, "validation_failed");

        Assert.Equal(HttpStatusCode.NoContent, (await admin.Http.DeleteAsync($"/api/v1/admin/blocks/{block.GetProperty("id").GetGuid()}")).StatusCode);
        Assert.Equal("available", await api.SlotStatusAsync(pick));
        await AssertError(await admin.Http.DeleteAsync($"/api/v1/admin/blocks/{block.GetProperty("id").GetGuid()}"), HttpStatusCode.NotFound, "block_not_found");

        // A Court with a live Booking cannot be blocked over it: the Admin moves or cancels the Booking first.
        await api.BookAndPayAsync(customer, pick);
        await AssertError(await Block(admin, pick with { StartHour = pick.StartHour + 1 }), HttpStatusCode.Conflict, "block_conflict");
        Assert.Equal(HttpStatusCode.Created, (await Block(admin, pick with { StartHour = pick.StartHour + 2 })).StatusCode); // right after it is fine
    }

    // ---------------------------------------------------------------- admin move

    [Fact]
    public async Task An_admin_move_ignores_price_and_does_not_use_up_the_customers_reschedule()
    {
        var admin = await api.AdminAsync();
        var customer = await api.RegisterAsync();
        var cheap = SlotPicker.Next(api, weekday: true, startHour: 10);  // 600
        var dear = SlotPicker.Next(api, weekday: true, startHour: 18);   // 800 or 900: a Customer could not move here
        var booked = await api.BookAndPayAsync(customer, cheap);

        var moved = await Ok(admin.Http.PostAsJsonAsync($"/api/v1/admin/bookings/{booked.Id()}/move", Place(dear)));
        Assert.Equal(dear.CourtId, moved.GetProperty("courtId").GetGuid());
        Assert.Equal("18:00", moved.GetProperty("start").GetString());
        Assert.Equal(600m, moved.GetProperty("total").GetDecimal());       // what was paid; nothing more is charged
        Assert.False(moved.GetProperty("rescheduled").GetBoolean());
        Assert.Equal("available", await api.SlotStatusAsync(cheap));
        Assert.Equal("booked", await api.SlotStatusAsync(dear));

        // The Customer still has their own one Reschedule.
        var mine = await customer.Http.GetFromJsonAsync<JsonElement>($"/api/v1/me/bookings/{booked.Id()}");
        Assert.NotEqual(JsonValueKind.Null, mine.GetProperty("rescheduleUntil").ValueKind);
        var back = await customer.Http.PostAsJsonAsync($"/api/v1/me/bookings/{booked.Id()}/reschedule", Place(cheap));
        Assert.Equal(HttpStatusCode.OK, back.StatusCode);
    }

    [Fact]
    public async Task An_admin_move_still_needs_a_free_court_inside_the_opening_hours()
    {
        var admin = await api.AdminAsync();
        var (customer, other) = (await api.RegisterAsync(), await api.RegisterAsync());
        var (origin, taken, blocked) = (SlotPicker.Next(api), SlotPicker.Next(api), SlotPicker.Next(api));
        var booked = await api.BookAndPayAsync(customer, origin);
        await api.BookAndPayAsync(other, taken);
        await Ok(Block(admin, blocked), HttpStatusCode.Created);

        Task<HttpResponseMessage> Move(object to) => admin.Http.PostAsJsonAsync($"/api/v1/admin/bookings/{booked.Id()}/move", to);
        await AssertError(await Move(Place(taken)), HttpStatusCode.Conflict, "slot_taken");
        await AssertError(await Move(Place(blocked)), HttpStatusCode.Conflict, "slot_blocked");
        await AssertError(await Move(Place(origin, 22)), HttpStatusCode.BadRequest, "outside_hours");
        await AssertError(await Move(Place(origin)), HttpStatusCode.BadRequest, "same_time");
        Assert.Equal("booked", await api.SlotStatusAsync(origin));

        // Only a confirmed Booking moves.
        var held = await other.BookAsync(SlotPicker.Next(api));
        await AssertError(await admin.Http.PostAsJsonAsync($"/api/v1/admin/bookings/{held.Id()}/move", Place(SlotPicker.Next(api))), HttpStatusCode.Conflict, "not_movable");
    }

    // ---------------------------------------------------------------- admin cancellation

    [Fact]
    public async Task An_admin_cancellation_frees_the_court_and_records_the_refund()
    {
        var admin = await api.AdminAsync();
        var customer = await api.RegisterAsync();
        var pick = SlotPicker.Next(api);
        var booked = await api.BookAndPayAsync(customer, pick);
        Task<HttpResponseMessage> Cancel(Guid id, string note) => admin.Http.PostAsJsonAsync($"/api/v1/admin/bookings/{id}/cancel", new { refundNote = note });

        await AssertError(await Cancel(booked.Id(), "  "), HttpStatusCode.BadRequest, "validation_failed"); // the refund must be written down

        var cancelled = await Ok(Cancel(booked.Id(), "Transferred 800 THB back, roof leak"));
        Assert.Equal("cancelled", cancelled.Status());
        Assert.Equal("Transferred 800 THB back, roof leak", cancelled.GetProperty("refundNote").GetString());
        Assert.NotEqual(JsonValueKind.Null, cancelled.GetProperty("cancelledAt").ValueKind);
        Assert.Equal("available", await api.SlotStatusAsync(pick));

        // The Customer sees it, and cannot bring it back.
        Assert.Equal("cancelled", (await customer.Http.GetFromJsonAsync<JsonElement>($"/api/v1/me/bookings/{booked.Id()}")).Status());
        await AssertError(await customer.Http.PostAsJsonAsync($"/api/v1/me/bookings/{booked.Id()}/reschedule", Place(SlotPicker.Next(api))), HttpStatusCode.Conflict, "not_reschedulable");

        // Repeating it changes nothing; an unpaid Hold is not something to cancel.
        Assert.Equal("Transferred 800 THB back, roof leak", (await Ok(Cancel(booked.Id(), "another note"))).GetProperty("refundNote").GetString());
        var held = await customer.BookAsync(SlotPicker.Next(api));
        await AssertError(await Cancel(held.Id(), "n/a"), HttpStatusCode.Conflict, "not_cancellable");
        await AssertError(await Cancel(Guid.NewGuid(), "n/a"), HttpStatusCode.NotFound, "booking_not_found");
    }

    // ---------------------------------------------------------------- check-in, no-show, completed

    [Fact]
    public async Task Check_in_opens_an_hour_before_and_a_finished_booking_completes()
    {
        var admin = await api.AdminAsync();
        var customer = await api.RegisterAsync();
        Task<HttpResponseMessage> CheckIn(Guid id) => admin.Http.PostAsync($"/api/v1/admin/bookings/{id}/check-in", null);

        var future = await api.BookAndPayAsync(customer, SlotPicker.Next(api));
        await AssertError(await CheckIn(future.Id()), HttpStatusCode.Conflict, "too_early");
        var held = await customer.BookAsync(SlotPicker.Next(api));
        await AssertError(await CheckIn(held.Id()), HttpStatusCode.Conflict, "not_checkin_able");

        // A Walk-in Booking for the hour that is running now.
        var (today, hour) = api.EnsureOpenNow();
        var now = await Ok(admin.WalkInAsync(SlotPicker.Courts[0], today, hour), HttpStatusCode.Created);
        var late = await Ok(admin.WalkInAsync(SlotPicker.Courts[1], today, hour), HttpStatusCode.Created);

        var checkedIn = await Ok(CheckIn(now.Id()));
        Assert.Equal("checked_in", checkedIn.Status());
        Assert.NotEqual(JsonValueKind.Null, checkedIn.GetProperty("checkedInAt").ValueKind);
        Assert.Equal("checked_in", (await Ok(CheckIn(now.Id()))).Status()); // again: no change

        // After the hour is over: too late to check the other one in, and the checked-in one completes.
        api.Clock.Advance(TimeSpan.FromMinutes(61));
        await AssertError(await CheckIn(late.Id()), HttpStatusCode.Conflict, "too_late");
        Assert.True(await api.WithScope(sp => sp.GetRequiredService<AdminBookingService>().CompleteFinishedAsync(default)) >= 1);
        Assert.Equal("completed", (await Ok(admin.Http.GetAsync($"/api/v1/admin/bookings/{now.Id()}"))).Status());
        Assert.Equal("confirmed", (await Ok(admin.Http.GetAsync($"/api/v1/admin/bookings/{late.Id()}"))).Status()); // never checked in: left for the Admin
    }

    [Fact]
    public async Task A_no_show_is_marked_by_the_admin_after_the_grace_time_and_frees_the_court()
    {
        var admin = await api.AdminAsync();
        Task<HttpResponseMessage> NoShow(Guid id) => admin.Http.PostAsync($"/api/v1/admin/bookings/{id}/no-show", null);

        var customer = await api.RegisterAsync();
        var future = await api.BookAndPayAsync(customer, SlotPicker.Next(api));
        await AssertError(await NoShow(future.Id()), HttpStatusCode.Conflict, "too_early");

        var (today, hour) = api.EnsureOpenNow();
        var court = SlotPicker.Courts[2];
        var b = await Ok(admin.WalkInAsync(court, today, hour), HttpStatusCode.Created);
        // Make sure at least 15 minutes of the hour have passed, without leaving the hour.
        var minute = PickleballClub.Api.Services.ClubClock.ToLocal(api.Clock.GetUtcNow().UtcDateTime, Pick.Bangkok).Minute;
        if (minute < 16) api.Clock.Advance(TimeSpan.FromMinutes(16 - minute));

        Assert.Equal("no_show", (await Ok(NoShow(b.Id()))).Status());
        Assert.Equal("no_show", (await Ok(NoShow(b.Id()))).Status()); // again: no change
        await AssertError(await admin.Http.PostAsync($"/api/v1/admin/bookings/{b.Id()}/check-in", null), HttpStatusCode.Conflict, "not_checkin_able");

        // The Court can be sold again for what is left of the hour.
        Assert.Equal(HttpStatusCode.Created, (await admin.WalkInAsync(court, today, hour)).StatusCode);
    }

    // ---------------------------------------------------------------- manual refunds

    [Fact]
    public async Task Money_that_bought_nothing_is_listed_until_the_admin_records_the_refund()
    {
        var admin = await api.AdminAsync();
        var (late, quick) = (await api.RegisterAsync(displayName: "Late Payer", phone: "0815556666"), await api.RegisterAsync());
        var pick = SlotPicker.Next(api);
        var held = await late.BookAsync(pick);
        api.Clock.Advance(TimeSpan.FromMinutes(11));
        await api.WithScope(sp => sp.GetRequiredService<BookingService>().ExpireHoldsAsync(default));
        await api.BookAndPayAsync(quick, pick);
        await api.PayAsync(held.Id()); // the money arrives after someone else took the Court

        var due = (await admin.Http.GetFromJsonAsync<JsonElement>("/api/v1/admin/refunds")).EnumerateArray()
            .Single(r => r.GetProperty("bookingId").GetGuid() == held.Id());
        Assert.Equal("court_taken", due.GetProperty("reason").GetString());
        Assert.Equal("Late Payer", due.GetProperty("customerName").GetString());
        Assert.Equal("0815556666", due.GetProperty("customerPhone").GetString());
        Assert.Equal(late.Email, due.GetProperty("customerEmail").GetString());
        Assert.Equal(held.GetProperty("total").GetDecimal(), due.GetProperty("amount").GetDecimal());
        var paymentId = due.GetProperty("paymentId").GetGuid();

        await AssertError(await admin.Http.PostAsJsonAsync($"/api/v1/admin/refunds/{paymentId}/refunded", new { note = "" }), HttpStatusCode.BadRequest, "validation_failed");
        Assert.Equal(HttpStatusCode.NoContent, (await admin.Http.PostAsJsonAsync($"/api/v1/admin/refunds/{paymentId}/refunded", new { note = "PromptPay back to 081-555-6666" })).StatusCode);

        Assert.DoesNotContain((await admin.Http.GetFromJsonAsync<JsonElement>("/api/v1/admin/refunds")).EnumerateArray(), r => r.GetProperty("paymentId").GetGuid() == paymentId);
        var done = (await admin.Http.GetFromJsonAsync<JsonElement>("/api/v1/admin/refunds?all=true")).EnumerateArray().Single(r => r.GetProperty("paymentId").GetGuid() == paymentId);
        Assert.Equal("PromptPay back to 081-555-6666", done.GetProperty("refundNote").GetString());
        Assert.NotEqual(JsonValueKind.Null, done.GetProperty("refundedAt").ValueKind);

        await AssertError(await admin.Http.PostAsJsonAsync($"/api/v1/admin/refunds/{Guid.NewGuid()}/refunded", new { note = "x" }), HttpStatusCode.NotFound, "payment_not_found");
    }
}
