using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using PickleballClub.Api.Services;
using PickleballClub.IntegrationTests.Infrastructure;

namespace PickleballClub.IntegrationTests;

[Collection(ApiCollection.Name)]
public class SmokeTests(ApiFactory api)
{
    // Seeded Courts (db/init/002_seed.sql). Each test uses its own Court + day so they never clash in the shared database.
    private static readonly Guid Court1 = Guid.Parse("c0000000-0000-0000-0000-000000000001");
    private static readonly Guid Court2 = Guid.Parse("c0000000-0000-0000-0000-000000000002");
    private static readonly Guid Court3 = Guid.Parse("c0000000-0000-0000-0000-000000000003");
    private const string Bangkok = "Asia/Bangkok";

    private async Task<JsonElement> Availability(DateOnly date)
    {
        var res = await api.Anonymous().GetAsync($"/api/v1/availability?date={date:yyyy-MM-dd}");
        Assert.True(res.IsSuccessStatusCode, $"{(int)res.StatusCode}: {string.Join("\n", api.ServerErrors)}");
        return await res.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static List<JsonElement> SlotsOf(JsonElement availability, Guid court) =>
        availability.GetProperty("courts").EnumerateArray()
            .Single(c => c.GetProperty("courtId").GetGuid() == court)
            .GetProperty("slots").EnumerateArray().ToList();

    private Task InsertWalkIn(Guid court, DateOnly date, int fromHour, int toHour, string status = "confirmed") =>
        api.WithDb(db => db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO bookings (source, guest_name, guest_phone, court_id, start_at, end_at, status, total, counter_payment)
            VALUES ('walk_in', 'Test Guest', '0800000000', {court},
                    {ClubClock.ToUtc(date, fromHour, Bangkok)}, {ClubClock.ToUtc(date, toHour, Bangkok)}, {status}, 600, 'cash')
            """));

    [Fact]
    public async Task Health_is_ok()
    {
        var res = await api.Anonymous().GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("up", (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("db").GetString());
    }

    [Fact]
    public void Api_uses_the_fake_clock() =>
        Assert.Same(api.Clock, api.Services.GetRequiredService<TimeProvider>());

    [Fact]
    public async Task Club_info_is_served_from_the_seeded_database()
    {
        var club = await api.Anonymous().GetFromJsonAsync<JsonElement>("/api/v1/club");
        Assert.Equal("Asia/Bangkok", club.GetProperty("timezone").GetString());
        Assert.Equal(4, club.GetProperty("courts").GetArrayLength());
        Assert.Equal(7, club.GetProperty("operatingHours").GetArrayLength());

        var today = api.ClubToday();
        var window = club.GetProperty("bookingWindowDays").GetInt32();
        Assert.Equal(today.ToString("yyyy-MM-dd"), club.GetProperty("today").GetString());
        Assert.Equal(today.AddDays(window).ToString("yyyy-MM-dd"), club.GetProperty("lastBookableDate").GetString());
    }

    [Fact]
    public async Task A_free_day_lists_every_slot_of_the_opening_hours_with_a_price()
    {
        var date = api.ClubToday().AddDays(2);
        var day = await Availability(date);

        Assert.True(day.GetProperty("withinBookingWindow").GetBoolean());
        Assert.Equal(4, day.GetProperty("courts").GetArrayLength());

        var slots = SlotsOf(day, Court3);
        Assert.Equal(17, slots.Count); // 06:00–23:00
        Assert.Equal("06:00", slots[0].GetProperty("start").GetString());
        Assert.Equal("23:00", slots[^1].GetProperty("end").GetString());
        Assert.All(slots, s =>
        {
            Assert.Equal("available", s.GetProperty("status").GetString());
            Assert.True(s.GetProperty("price").GetDecimal() > 0);
        });
        // Slot instants are UTC: 06:00 in Bangkok is 23:00Z the day before.
        Assert.Equal(ClubClock.ToUtc(date, 6, Bangkok), slots[0].GetProperty("startAt").GetDateTime().ToUniversalTime());
    }

    [Fact]
    public async Task A_confirmed_booking_takes_exactly_its_slots()
    {
        var date = api.ClubToday().AddDays(3);
        await InsertWalkIn(Court1, date, 10, 12);

        var slots = SlotsOf(await Availability(date), Court1);
        string StatusAt(string start) => slots.Single(s => s.GetProperty("start").GetString() == start).GetProperty("status").GetString()!;
        Assert.Equal("available", StatusAt("09:00"));
        Assert.Equal("booked", StatusAt("10:00"));
        Assert.Equal("booked", StatusAt("11:00"));
        Assert.Equal("available", StatusAt("12:00"));
        // the other Courts are untouched
        Assert.All(SlotsOf(await Availability(date), Court3), s => Assert.Equal("available", s.GetProperty("status").GetString()));
    }

    [Fact]
    public async Task The_database_refuses_two_live_bookings_on_the_same_court_and_time()
    {
        var date = api.ClubToday().AddDays(4);
        await InsertWalkIn(Court2, date, 18, 20);

        var ex = await Assert.ThrowsAsync<PostgresException>(() => InsertWalkIn(Court2, date, 19, 21, status: "held"));
        Assert.Equal(PostgresErrorCodes.ExclusionViolation, ex.SqlState); // 23P01

        await InsertWalkIn(Court2, date, 20, 22);                       // back-to-back is fine
        await InsertWalkIn(Court2, date, 18, 20, status: "cancelled");  // a released booking does not block
        await InsertWalkIn(Court1, date, 19, 21);                       // another Court is independent
    }

    [Fact]
    public async Task A_day_past_the_booking_window_is_shown_but_flagged()
    {
        var club = await api.Anonymous().GetFromJsonAsync<JsonElement>("/api/v1/club");
        var window = club.GetProperty("bookingWindowDays").GetInt32();

        Assert.True((await Availability(api.ClubToday().AddDays(window))).GetProperty("withinBookingWindow").GetBoolean());
        Assert.False((await Availability(api.ClubToday().AddDays(window + 1))).GetProperty("withinBookingWindow").GetBoolean());
        Assert.False((await Availability(api.ClubToday().AddDays(-1))).GetProperty("withinBookingWindow").GetBoolean());
    }

    [Fact]
    public async Task A_malformed_date_is_a_400_with_the_standard_error_body()
    {
        var res = await api.Anonymous().GetAsync("/api/v1/availability?date=tomorrow");
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("bad_request", (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task An_unknown_route_is_a_404_with_the_standard_error_body()
    {
        var res = await api.Anonymous().GetAsync("/api/v1/nope");
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        Assert.Equal("not_found", (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }
}
