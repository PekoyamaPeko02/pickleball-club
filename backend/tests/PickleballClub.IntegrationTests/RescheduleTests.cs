using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PickleballClub.Api.Services;
using PickleballClub.IntegrationTests.Infrastructure;

namespace PickleballClub.IntegrationTests;

[Collection(ApiCollection.Name)]
public class RescheduleTests(ApiFactory api)
{
    private static async Task AssertError(HttpResponseMessage res, HttpStatusCode status, string code)
    {
        var body = await res.Content.ReadAsStringAsync();
        Assert.True(res.StatusCode == status, $"expected {(int)status} {code}, got {(int)res.StatusCode}: {body}");
        Assert.Equal(code, JsonDocument.Parse(body).RootElement.GetProperty("code").GetString());
    }

    private static Task<HttpResponseMessage> Move(TestUser user, Guid bookingId, Pick to) =>
        user.Http.PostAsJsonAsync($"/api/v1/me/bookings/{bookingId}/reschedule",
            new { courtId = to.CourtId, date = to.Date.ToString("yyyy-MM-dd"), startHour = to.StartHour });

    /// <summary>Weekend blocks all cost 400/hour whatever the Court or hour, so any weekend-to-weekend move is "same price".</summary>
    private Pick Weekend() => SlotPicker.Next(api, weekday: false);

    [Fact]
    public async Task A_confirmed_booking_moves_once_to_another_court_and_day()
    {
        var user = await api.RegisterAsync();
        var (origin, to) = (Weekend(), Weekend());
        var booked = await api.BookAndPayAsync(user, origin);
        var notice = (await api.Anonymous().GetFromJsonAsync<JsonElement>("/api/v1/club")).GetProperty("rescheduleNoticeHours").GetInt32();
        Assert.False(booked.GetProperty("rescheduled").GetBoolean());
        Assert.Equal(origin.StartAt.AddHours(-notice), booked.GetProperty("rescheduleUntil").GetDateTime().ToUniversalTime());

        var res = await Move(user, booked.Id(), to);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var moved = await res.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(booked.Id(), moved.Id());                                             // the same Booking, same code
        Assert.Equal(booked.GetProperty("code").GetString(), moved.GetProperty("code").GetString());
        Assert.Equal("confirmed", moved.Status());
        Assert.Equal(to.CourtId, moved.GetProperty("courtId").GetGuid());
        Assert.Equal(to.Date.ToString("yyyy-MM-dd"), moved.GetProperty("date").GetString());
        Assert.Equal(ClubClock.Label(to.StartHour), moved.GetProperty("start").GetString());
        Assert.Equal(2, moved.GetProperty("hours").GetInt32());
        Assert.Equal(booked.GetProperty("total").GetDecimal(), moved.GetProperty("total").GetDecimal()); // nothing charged, nothing refunded
        Assert.True(moved.GetProperty("rescheduled").GetBoolean());
        Assert.Equal(JsonValueKind.Null, moved.GetProperty("rescheduleUntil").ValueKind);

        Assert.Equal("available", await api.SlotStatusAsync(origin));
        Assert.Equal("booked", await api.SlotStatusAsync(to));
        Assert.Equal("booked", await api.SlotStatusAsync(to, to.StartHour + 1));

        // The one Reschedule is used up.
        await AssertError(await Move(user, booked.Id(), Weekend()), HttpStatusCode.Conflict, "already_rescheduled");
    }

    [Fact]
    public async Task A_booking_can_slide_onto_part_of_its_own_time()
    {
        var user = await api.RegisterAsync();
        var block = SlotPicker.NextRun(api, blocks: 2, weekday: false); // owns four hours on one Court
        var booked = await api.BookAndPayAsync(user, block);

        var res = await Move(user, booked.Id(), block with { StartHour = block.StartHour + 1 });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("available", await api.SlotStatusAsync(block));
        Assert.Equal("booked", await api.SlotStatusAsync(block, block.StartHour + 1));
        Assert.Equal("booked", await api.SlotStatusAsync(block, block.StartHour + 2));
    }

    [Fact]
    public async Task A_move_may_cost_the_same_or_less_but_never_more()
    {
        var user = await api.RegisterAsync();
        var cheap = SlotPicker.Next(api, weekday: true, startHour: 10);                   // 2 × 300
        var booked = await api.BookAndPayAsync(user, cheap);
        Assert.Equal(600m, booked.GetProperty("total").GetDecimal());

        await AssertError(await Move(user, booked.Id(), SlotPicker.Next(api, weekday: true, startHour: 18)), HttpStatusCode.BadRequest, "costs_more"); // 800 or 900
        await AssertError(await Move(user, booked.Id(), Weekend()), HttpStatusCode.BadRequest, "costs_more");                                           // 800

        // A refused move does not use up the Reschedule.
        var sameprice = await Move(user, booked.Id(), SlotPicker.Next(api, weekday: true, startHour: 8));
        Assert.Equal(HttpStatusCode.OK, sameprice.StatusCode);

        // Cheaper is allowed, and the Customer keeps having paid what they paid.
        var other = await api.RegisterAsync();
        var dear = await api.BookAndPayAsync(other, SlotPicker.Next(api, weekday: true, startHour: 18));
        var down = await Move(other, dear.Id(), SlotPicker.Next(api, weekday: true, startHour: 12));
        Assert.Equal(HttpStatusCode.OK, down.StatusCode);
        Assert.Equal(dear.GetProperty("total").GetDecimal(), (await down.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("total").GetDecimal());
    }

    [Fact]
    public async Task A_move_must_be_asked_for_early_enough()
    {
        var user = await api.RegisterAsync();
        var booked = await api.BookAndPayAsync(user, Weekend());
        var to = Weekend();

        // Every Booking in these tests is at least 5 days away, so make the notice longer than that for a moment.
        await api.WithDb(db => db.Database.ExecuteSqlRawAsync("UPDATE club_settings SET reschedule_notice_hours = 24 * 30"));
        try
        {
            await AssertError(await Move(user, booked.Id(), to), HttpStatusCode.Conflict, "too_late_to_reschedule");
        }
        finally
        {
            await api.WithDb(db => db.Database.ExecuteSqlRawAsync("UPDATE club_settings SET reschedule_notice_hours = 48"));
        }
        Assert.Equal(HttpStatusCode.OK, (await Move(user, booked.Id(), to)).StatusCode);
    }

    [Fact]
    public async Task The_new_time_must_be_bookable()
    {
        var (user, other) = (await api.RegisterAsync(), await api.RegisterAsync());
        var origin = Weekend();
        var booked = await api.BookAndPayAsync(user, origin);
        var today = api.ClubToday();

        await AssertError(await Move(user, booked.Id(), origin), HttpStatusCode.BadRequest, "same_time");
        await AssertError(await Move(user, booked.Id(), origin with { Date = today.AddDays(15) }), HttpStatusCode.BadRequest, "outside_booking_window");
        await AssertError(await Move(user, booked.Id(), origin with { Date = today.AddDays(-1) }), HttpStatusCode.BadRequest, "outside_booking_window");
        await AssertError(await Move(user, booked.Id(), origin with { StartHour = 22 }), HttpStatusCode.BadRequest, "outside_hours");
        await AssertError(await Move(user, booked.Id(), origin with { CourtId = Guid.NewGuid() }), HttpStatusCode.BadRequest, "court_not_found");

        var taken = Weekend();
        await api.BookAndPayAsync(other, taken);
        await AssertError(await Move(user, booked.Id(), taken), HttpStatusCode.Conflict, "slot_taken");
        await AssertError(await Move(user, booked.Id(), taken with { StartHour = taken.StartHour + 1 }), HttpStatusCode.Conflict, "slot_taken");

        var blocked = Weekend();
        await api.WithDb(db => db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO court_blocks (court_id, start_at, end_at, reason) VALUES ({blocked.CourtId}, {blocked.StartAt}, {blocked.StartAt.AddHours(1)}, 'event')"));
        await AssertError(await Move(user, booked.Id(), blocked), HttpStatusCode.Conflict, "slot_blocked");

        // None of those refusals used up the Reschedule or moved anything.
        Assert.Equal("booked", await api.SlotStatusAsync(origin));
        Assert.Equal(HttpStatusCode.OK, (await Move(user, booked.Id(), Weekend())).StatusCode);
    }

    [Fact]
    public async Task Only_the_owner_can_move_and_only_a_confirmed_booking()
    {
        var (owner, stranger) = (await api.RegisterAsync(), await api.RegisterAsync());
        var held = await owner.BookAsync(Weekend());
        var to = Weekend();

        await AssertError(await Move(owner, held.Id(), to), HttpStatusCode.Conflict, "not_reschedulable");      // not paid yet
        Assert.Equal(JsonValueKind.Null, held.GetProperty("rescheduleUntil").ValueKind);

        await api.PayAsync(held.Id());
        await AssertError(await Move(stranger, held.Id(), to), HttpStatusCode.NotFound, "booking_not_found");
        await AssertError(await api.Anonymous().PostAsJsonAsync($"/api/v1/me/bookings/{held.Id()}/reschedule", new { courtId = to.CourtId, date = to.Date.ToString("yyyy-MM-dd"), startHour = to.StartHour }),
            HttpStatusCode.Unauthorized, "unauthorized");
        Assert.Equal(HttpStatusCode.OK, (await Move(owner, held.Id(), to)).StatusCode);
    }

    [Fact]
    public async Task Two_moves_fired_together_use_the_one_reschedule_once()
    {
        var user = await api.RegisterAsync();
        var booked = await api.BookAndPayAsync(user, Weekend());
        var targets = Enumerable.Range(0, 6).Select(_ => Weekend()).ToList();

        var results = await Task.WhenAll(targets.Select(t => Move(user, booked.Id(), t)));
        Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.OK));
        foreach (var r in results.Where(r => r.StatusCode != HttpStatusCode.OK))
            await AssertError(r, HttpStatusCode.Conflict, "already_rescheduled");

        // Exactly one target is taken; the others are still free.
        var statuses = new List<string>();
        foreach (var t in targets) statuses.Add(await api.SlotStatusAsync(t));
        Assert.Equal(1, statuses.Count(s => s == "booked"));
        Assert.Equal(5, statuses.Count(s => s == "available"));
        Assert.Empty(api.ServerErrors);
    }
}
