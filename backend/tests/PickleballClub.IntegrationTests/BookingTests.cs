using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PickleballClub.Api.Features.Bookings;
using PickleballClub.Api.Services;
using PickleballClub.IntegrationTests.Infrastructure;

namespace PickleballClub.IntegrationTests;

[Collection(ApiCollection.Name)]
public class BookingTests(ApiFactory api)
{
    private static async Task AssertError(HttpResponseMessage res, HttpStatusCode status, string code)
    {
        var body = await res.Content.ReadAsStringAsync();
        Assert.True(res.StatusCode == status, $"expected {(int)status} {code}, got {(int)res.StatusCode}: {body}");
        Assert.Equal(code, JsonDocument.Parse(body).RootElement.GetProperty("code").GetString());
    }

    private Task<int> ExpireHolds() => api.WithScope(sp => sp.GetRequiredService<BookingService>().ExpireHoldsAsync(default));

    private Task<JsonElement> Get(TestUser user, Guid id) => user.Http.GetFromJsonAsync<JsonElement>($"/api/v1/me/bookings/{id}");

    private static List<(string Status, bool RefundDue, string? Reason)> Payments(params (string, bool, string?)[] rows) => rows.ToList();

    private Task<List<(string Status, bool RefundDue, string? Reason)>> PaymentsOf(Guid bookingId) =>
        api.WithScope(async sp =>
        {
            var db = sp.GetRequiredService<PickleballClub.Api.Data.AppDbContext>();
            var rows = await db.Payments.AsNoTracking().Where(p => p.BookingId == bookingId).OrderBy(p => p.CreatedAt).ToListAsync();
            return rows.Select(p => (p.Status, p.RefundDue, p.RefundReason)).ToList();
        });

    // ---------------------------------------------------------------- hold

    [Fact]
    public async Task A_hold_keeps_the_slots_for_ten_minutes_and_opens_a_payment()
    {
        var user = await api.RegisterAsync();
        var pick = SlotPicker.Next(api);
        var before = api.Clock.GetUtcNow().UtcDateTime;

        var res = await user.HoldAsync(pick);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var b = await res.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("held", b.Status());
        Assert.Equal("online", b.GetProperty("source").GetString());
        Assert.StartsWith("PB", b.GetProperty("code").GetString());
        Assert.Equal(pick.CourtId, b.GetProperty("courtId").GetGuid());
        Assert.Equal(pick.Date.ToString("yyyy-MM-dd"), b.GetProperty("date").GetString());
        Assert.Equal(ClubClock.Label(pick.StartHour), b.GetProperty("start").GetString());
        Assert.Equal(ClubClock.Label(pick.StartHour + 2), b.GetProperty("end").GetString());
        Assert.Equal(2, b.GetProperty("hours").GetInt32());
        Assert.Equal(pick.StartAt, b.GetProperty("startAt").GetDateTime().ToUniversalTime());
        Assert.Equal(before.AddMinutes(10), b.GetProperty("holdExpiresAt").GetDateTime().ToUniversalTime(), TimeSpan.FromSeconds(5));

        var pay = b.GetProperty("payment");
        Assert.Equal("promptpay", pay.GetProperty("method").GetString());
        Assert.Equal("pending", pay.GetProperty("status").GetString());
        Assert.Equal(b.GetProperty("total").GetDecimal(), pay.GetProperty("amount").GetDecimal());
        Assert.StartsWith("000201", pay.GetProperty("qrPayload").GetString()); // a PromptPay QR payload

        Assert.Equal("held", await api.SlotStatusAsync(pick));
        Assert.Equal("held", await api.SlotStatusAsync(pick, pick.StartHour + 1));
    }

    [Fact]
    public async Task The_server_prices_every_slot()
    {
        var user = await api.RegisterAsync();

        // Weekday, indoor Court, 15:00–17:00 crosses from off-peak (300) into the Court's own evening rule (450).
        var crossing = SlotPicker.NextRun(api, blocks: 2, weekday: true, startHour: 14, indoor: true) with { StartHour = 15 };
        var b1 = await user.BookAsync(crossing);
        Assert.Equal(750m, b1.GetProperty("total").GetDecimal());
        await user.Http.PostAsync($"/api/v1/me/bookings/{b1.Id()}/release", null);

        // Weekday, outdoor Court, evening: the Club-wide peak rule (400).
        var b2 = await user.BookAsync(SlotPicker.Next(api, weekday: true, startHour: 18, indoor: false));
        Assert.Equal(800m, b2.GetProperty("total").GetDecimal());
        await user.Http.PostAsync($"/api/v1/me/bookings/{b2.Id()}/release", null);

        // Saturday or Sunday: the holiday rule (400), whatever the Court or hour.
        var b3 = await user.BookAsync(SlotPicker.Next(api, weekday: false, startHour: 8, indoor: true), hours: 1);
        Assert.Equal(400m, b3.GetProperty("total").GetDecimal());
    }

    [Fact]
    public async Task Booking_needs_a_signed_in_customer_with_a_complete_profile()
    {
        var pick = SlotPicker.Next(api);
        var body = new { courtId = pick.CourtId, date = pick.Date.ToString("yyyy-MM-dd"), startHour = pick.StartHour, hours = 1, method = "promptpay" };

        await AssertError(await api.Anonymous().PostAsJsonAsync("/api/v1/bookings", body), HttpStatusCode.Unauthorized, "unauthorized");
        await AssertError(await (await api.AdminAsync()).Http.PostAsJsonAsync("/api/v1/bookings", body), HttpStatusCode.Forbidden, "forbidden");

        var noPhone = await api.RegisterAsync(phone: null);
        await AssertError(await noPhone.HoldAsync(pick), HttpStatusCode.Conflict, "profile_incomplete");
        await noPhone.Http.PutAsJsonAsync("/api/v1/auth/me", new { displayName = "Nok", phone = "0812345678" });
        Assert.Equal(HttpStatusCode.Created, (await noPhone.HoldAsync(pick)).StatusCode);
    }

    [Fact]
    public async Task A_hold_must_fit_the_booking_window_the_opening_hours_and_the_clock()
    {
        var user = await api.RegisterAsync();
        var pick = SlotPicker.Next(api);
        var today = api.ClubToday();

        await AssertError(await user.HoldAsync(pick with { Date = today.AddDays(15) }), HttpStatusCode.BadRequest, "outside_booking_window");
        await AssertError(await user.HoldAsync(pick with { Date = today.AddDays(-1) }), HttpStatusCode.BadRequest, "outside_booking_window");
        await AssertError(await user.HoldAsync(pick with { StartHour = 5 }), HttpStatusCode.BadRequest, "outside_hours");     // opens 06:00
        await AssertError(await user.HoldAsync(pick with { StartHour = 22 }), HttpStatusCode.BadRequest, "outside_hours");    // 22:00–24:00, closes 23:00
        await AssertError(await user.HoldAsync(pick with { CourtId = Guid.NewGuid() }), HttpStatusCode.BadRequest, "court_not_found");
        await AssertError(await user.HoldAsync(pick, hours: 0), HttpStatusCode.BadRequest, "validation_failed");
        await AssertError(await user.HoldAsync(pick, method: "bitcoin"), HttpStatusCode.BadRequest, "validation_failed");

        // A Slot that has started cannot be booked online: move the clock to 12:30 today at the Club if it is still morning.
        var local = ClubClock.ToLocal(api.Clock.GetUtcNow().UtcDateTime, Pick.Bangkok);
        if (local.Hour < 12) api.Clock.Advance(TimeSpan.FromHours(12 - local.Hour) + TimeSpan.FromMinutes(30));
        var nowHour = ClubClock.ToLocal(api.Clock.GetUtcNow().UtcDateTime, Pick.Bangkok).Hour;
        if (nowHour < 22) // late in the evening every remaining Slot of today may also be outside the hours
            await AssertError(await user.HoldAsync(pick with { Date = api.ClubToday(), StartHour = nowHour }, hours: 1), HttpStatusCode.BadRequest, "slot_in_past");
    }

    [Fact]
    public async Task A_taken_time_cannot_be_held_by_someone_else()
    {
        var (first, second) = (await api.RegisterAsync(), await api.RegisterAsync());
        var pick = SlotPicker.NextRun(api, blocks: 2, startHour: 8) with { StartHour = 10 }; // owns 08:00–12:00, books 10:00–12:00
        await first.BookAsync(pick);

        await AssertError(await second.HoldAsync(pick), HttpStatusCode.Conflict, "slot_taken");
        await AssertError(await second.HoldAsync(pick with { StartHour = 11 }, hours: 1), HttpStatusCode.Conflict, "slot_taken"); // overlaps the second hour
        await AssertError(await second.HoldAsync(pick with { StartHour = 9 }), HttpStatusCode.Conflict, "slot_taken");           // 09–11 overlaps the first hour

        Assert.Equal(HttpStatusCode.Created, (await second.HoldAsync(pick with { StartHour = 9 }, hours: 1)).StatusCode);        // 09–10 touches, does not overlap
    }

    [Fact]
    public async Task A_court_block_stops_a_hold()
    {
        var user = await api.RegisterAsync();
        var pick = SlotPicker.Next(api);
        await api.WithDb(db => db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO court_blocks (court_id, start_at, end_at, reason) VALUES ({pick.CourtId}, {ClubClock.ToUtc(pick.Date, pick.StartHour + 1, Pick.Bangkok)}, {ClubClock.ToUtc(pick.Date, pick.StartHour + 2, Pick.Bangkok)}, 'maintenance')"));

        await AssertError(await user.HoldAsync(pick), HttpStatusCode.Conflict, "slot_blocked");
        Assert.Equal(HttpStatusCode.Created, (await user.HoldAsync(pick, hours: 1)).StatusCode); // the hour before the block is free
    }

    [Fact]
    public async Task A_customer_has_one_open_hold_at_a_time()
    {
        var user = await api.RegisterAsync();
        var (a, b, c) = (SlotPicker.Next(api), SlotPicker.Next(api), SlotPicker.Next(api));

        var first = await user.BookAsync(a);
        await AssertError(await user.HoldAsync(b), HttpStatusCode.Conflict, "open_hold_exists");

        // Releasing gives the Slots back and frees the Customer to hold again.
        var released = await user.Http.PostAsync($"/api/v1/me/bookings/{first.Id()}/release", null);
        Assert.Equal("expired", (await released.Content.ReadFromJsonAsync<JsonElement>()).Status());
        Assert.Equal("available", await api.SlotStatusAsync(a));
        var second = await user.BookAsync(b);

        // A Hold that ran out counts for nothing, even before the sweep has released it.
        api.Clock.Advance(TimeSpan.FromMinutes(11));
        Assert.Equal(HttpStatusCode.Created, (await user.HoldAsync(c)).StatusCode);
        Assert.Equal("expired", (await Get(user, second.Id())).Status());

        // A paid Booking is not a Hold: the Customer can go on booking.
        var paid = await api.BookAndPayAsync(await api.RegisterAsync(), SlotPicker.Next(api));
        Assert.Equal("confirmed", paid.Status());
    }

    [Fact]
    public async Task The_same_idempotency_key_returns_the_same_booking()
    {
        var user = await api.RegisterAsync();
        var pick = SlotPicker.Next(api);
        var key = Guid.NewGuid().ToString();

        var first = await (await user.HoldAsync(pick, key: key)).Content.ReadFromJsonAsync<JsonElement>();
        var again = await user.HoldAsync(pick, key: key);
        Assert.Equal(HttpStatusCode.Created, again.StatusCode);
        Assert.Equal(first.Id(), (await again.Content.ReadFromJsonAsync<JsonElement>()).Id());
    }

    // ---------------------------------------------------------------- payment

    [Fact]
    public async Task Payment_confirms_the_booking()
    {
        var user = await api.RegisterAsync();
        var pick = SlotPicker.Next(api);
        var held = await user.BookAsync(pick);

        var paid = await api.PayAsync(held.Id());
        Assert.Equal("confirmed", paid.Status());
        Assert.Equal(JsonValueKind.Null, paid.GetProperty("holdExpiresAt").ValueKind);
        Assert.Equal("succeeded", paid.GetProperty("payment").GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, paid.GetProperty("payment").GetProperty("qrPayload").ValueKind); // nothing left to scan

        Assert.Equal("booked", await api.SlotStatusAsync(pick));
        Assert.Equal("confirmed", (await Get(user, held.Id())).Status());

        // Time passing does not touch a confirmed Booking.
        api.Clock.Advance(TimeSpan.FromMinutes(11));
        await ExpireHolds();
        Assert.Equal("confirmed", (await Get(user, held.Id())).Status());
    }

    [Fact]
    public async Task Only_a_signed_webhook_confirms_and_repeats_change_nothing()
    {
        var user = await api.RegisterAsync();
        var held = await user.BookAsync(SlotPicker.Next(api));
        var providerRef = await api.WithScope(async sp =>
            (await sp.GetRequiredService<PickleballClub.Api.Data.AppDbContext>().Payments.AsNoTracking().SingleAsync(p => p.BookingId == held.Id())).ProviderRef);

        HttpRequestMessage Webhook(string secret, string status = "succeeded") => new(HttpMethod.Post, "/api/v1/payments/webhook/mock")
        {
            Headers = { { "X-Webhook-Secret", secret } },
            Content = JsonContent.Create(new { providerRef, status }),
        };

        Assert.Equal(HttpStatusCode.Unauthorized, (await api.Anonymous().SendAsync(Webhook("wrong"))).StatusCode);
        Assert.Equal("held", (await Get(user, held.Id())).Status());
        Assert.Equal(HttpStatusCode.NotFound, (await api.Anonymous().PostAsJsonAsync("/api/v1/payments/webhook/other", new { })).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await api.Anonymous().SendAsync(Webhook("dev-webhook-secret"))).StatusCode);
        Assert.Equal("confirmed", (await Get(user, held.Id())).Status());

        // The gateway may deliver the same event again, or even a contradicting one: the first outcome stands.
        Assert.Equal(HttpStatusCode.OK, (await api.Anonymous().SendAsync(Webhook("dev-webhook-secret"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await api.Anonymous().SendAsync(Webhook("dev-webhook-secret", "failed"))).StatusCode);
        Assert.Equal("confirmed", (await Get(user, held.Id())).Status());
        Assert.Equal(Payments(("succeeded", false, null)), await PaymentsOf(held.Id()));
    }

    [Fact]
    public async Task An_unpaid_hold_runs_out_and_the_slots_go_back_on_sale()
    {
        var user = await api.RegisterAsync();
        var pick = SlotPicker.Next(api);
        var held = await user.BookAsync(pick);

        api.Clock.Advance(TimeSpan.FromMinutes(9));
        await ExpireHolds(); // not yet for this one
        Assert.Equal("held", (await Get(user, held.Id())).Status());

        api.Clock.Advance(TimeSpan.FromMinutes(2));
        Assert.True(await ExpireHolds() >= 1);
        Assert.Equal("expired", (await Get(user, held.Id())).Status());
        Assert.Equal("available", await api.SlotStatusAsync(pick));

        // Expired Holds are left out of the Customer's list.
        var mine = await user.Http.GetFromJsonAsync<JsonElement>("/api/v1/me/bookings");
        Assert.DoesNotContain(mine.EnumerateArray(), b => b.Id() == held.Id());
    }

    [Fact]
    public async Task Money_arriving_after_the_hold_ran_out_still_buys_the_court_if_it_is_free()
    {
        var user = await api.RegisterAsync();
        var pick = SlotPicker.Next(api);
        var held = await user.BookAsync(pick);
        api.Clock.Advance(TimeSpan.FromMinutes(11));
        await ExpireHolds();

        var paid = await api.PayAsync(held.Id());
        Assert.Equal("confirmed", paid.Status());
        Assert.Equal("booked", await api.SlotStatusAsync(pick));
        Assert.Equal(Payments(("succeeded", false, null)), await PaymentsOf(held.Id()));
    }

    [Fact]
    public async Task Money_arriving_after_someone_else_took_the_court_is_flagged_for_a_manual_refund()
    {
        var (late, quick) = (await api.RegisterAsync(), await api.RegisterAsync());
        var pick = SlotPicker.Next(api);
        var held = await late.BookAsync(pick);
        api.Clock.Advance(TimeSpan.FromMinutes(11));
        await ExpireHolds();
        var winner = await api.BookAndPayAsync(quick, pick);

        var after = await api.PayAsync(held.Id());
        Assert.Equal("expired", after.Status());                       // the late Customer did not get the Court
        Assert.Equal("confirmed", (await Get(quick, winner.Id())).Status());
        Assert.Equal(Payments(("succeeded", true, "court_taken")), await PaymentsOf(held.Id()));
        Assert.Empty(api.ServerErrors);
    }

    [Fact]
    public async Task Paying_twice_for_one_booking_flags_the_second_payment()
    {
        var user = await api.RegisterAsync();
        var held = await user.BookAsync(SlotPicker.Next(api));

        // The Customer switches to card: a second payment for the same Hold, same deadline.
        var switched = await user.Http.PostAsJsonAsync($"/api/v1/me/bookings/{held.Id()}/payment", new { method = "card" });
        Assert.Equal(HttpStatusCode.OK, switched.StatusCode);
        var b = await switched.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("card", b.GetProperty("payment").GetProperty("method").GetString());
        Assert.Equal(held.GetProperty("holdExpiresAt").GetDateTime(), b.GetProperty("holdExpiresAt").GetDateTime());

        Assert.Equal("confirmed", (await api.PayAsync(held.Id())).Status()); // pays the card one (newest)
        await api.PayAsync(held.Id());                                       // …and then the QR as well
        Assert.Equal(Payments(("succeeded", true, "already_confirmed"), ("succeeded", false, null)), await PaymentsOf(held.Id()));

        await AssertError(await user.Http.PostAsJsonAsync($"/api/v1/me/bookings/{held.Id()}/payment", new { method = "card" }), HttpStatusCode.Conflict, "not_held");
        await AssertError(await user.Http.PostAsync($"/api/v1/me/bookings/{held.Id()}/release", null), HttpStatusCode.Conflict, "not_held");
    }

    // ---------------------------------------------------------------- my bookings

    [Fact]
    public async Task A_customer_sees_only_their_own_bookings()
    {
        var (mine, other) = (await api.RegisterAsync(), await api.RegisterAsync());
        var a = await api.BookAndPayAsync(mine, SlotPicker.Next(api));
        var b = await api.BookAndPayAsync(other, SlotPicker.Next(api));

        var list = await mine.Http.GetFromJsonAsync<JsonElement>("/api/v1/me/bookings");
        Assert.Equal(new[] { a.Id() }, list.EnumerateArray().Select(x => x.Id()).ToArray());

        await AssertError(await mine.Http.GetAsync($"/api/v1/me/bookings/{b.Id()}"), HttpStatusCode.NotFound, "booking_not_found");
        await AssertError(await mine.Http.PostAsync($"/api/v1/me/bookings/{b.Id()}/release", null), HttpStatusCode.NotFound, "booking_not_found");
        await AssertError(await api.Anonymous().GetAsync("/api/v1/me/bookings"), HttpStatusCode.Unauthorized, "unauthorized");
    }

    // ---------------------------------------------------------------- concurrency

    [Fact]
    public async Task Twenty_customers_racing_for_one_slot_produce_exactly_one_hold()
    {
        var users = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => api.RegisterAsync()));
        var pick = SlotPicker.Next(api);

        var results = await Task.WhenAll(users.Select(u => u.HoldAsync(pick)));
        Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.Created));
        foreach (var r in results.Where(r => r.StatusCode != HttpStatusCode.Created))
            await AssertError(r, HttpStatusCode.Conflict, "slot_taken");
        Assert.Empty(api.ServerErrors);
    }

    [Fact]
    public async Task One_customer_double_tapping_gets_one_booking()
    {
        var user = await api.RegisterAsync();
        var pick = SlotPicker.Next(api);
        var key = Guid.NewGuid().ToString();

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => user.HoldAsync(pick, key: key)));
        Assert.All(results, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));
        var ids = new HashSet<Guid>();
        foreach (var r in results) ids.Add((await r.Content.ReadFromJsonAsync<JsonElement>()).Id());
        Assert.Single(ids);
        Assert.Empty(api.ServerErrors);
    }

    [Fact]
    public async Task One_customer_cannot_open_two_holds_by_firing_them_together()
    {
        var user = await api.RegisterAsync();
        var picks = Enumerable.Range(0, 6).Select(_ => SlotPicker.Next(api)).ToList();

        var results = await Task.WhenAll(picks.Select(p => user.HoldAsync(p)));
        Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.Created));
        foreach (var r in results.Where(r => r.StatusCode != HttpStatusCode.Created))
            await AssertError(r, HttpStatusCode.Conflict, "open_hold_exists");
        Assert.Empty(api.ServerErrors);
    }
}
