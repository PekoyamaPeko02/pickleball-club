using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using PickleballClub.IntegrationTests.Infrastructure;

namespace PickleballClub.IntegrationTests;

[Collection(SettingsCollection.Name)]
public class SettingsTests(ApiFactory api)
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

    private Task<JsonElement> Club() => api.Anonymous().GetFromJsonAsync<JsonElement>("/api/v1/club");
    private Task<JsonElement> Day(DateOnly date) => api.Anonymous().GetFromJsonAsync<JsonElement>($"/api/v1/availability?date={date:yyyy-MM-dd}");

    private static List<JsonElement> Slots(JsonElement day, Guid court) =>
        day.GetProperty("courts").EnumerateArray().Single(c => c.GetProperty("courtId").GetGuid() == court).GetProperty("slots").EnumerateArray().ToList();

    private static JsonElement SlotAt(JsonElement day, Guid court, string start) =>
        Slots(day, court).Single(s => s.GetProperty("start").GetString() == start);

    /// <summary>The first date on that day of the week, from tomorrow on.</summary>
    private DateOnly Next(DayOfWeek dow)
    {
        var d = api.ClubToday().AddDays(1);
        while (d.DayOfWeek != dow) d = d.AddDays(1);
        return d;
    }

    private static readonly Guid Court1 = Guid.Parse("c0000000-0000-0000-0000-000000000001");
    private static readonly Guid Court4 = Guid.Parse("c0000000-0000-0000-0000-000000000004");

    private static object Rule(Guid? court, string dayType, int from, int to, decimal price, string? label = null) =>
        new { courtId = court, dayType, startHour = from, endHour = to, pricePerHour = price, label };

    private async Task<Guid> NewCourt(TestUser admin, string name) =>
        (await Ok(admin.Http.PostAsJsonAsync("/api/v1/admin/courts", new { name, indoor = false, sortOrder = 50 }), HttpStatusCode.Created)).GetProperty("id").GetGuid();

    [Fact]
    public async Task Settings_are_for_admins_only()
    {
        var customer = await api.RegisterAsync();
        await AssertError(await api.Anonymous().GetAsync("/api/v1/admin/settings"), HttpStatusCode.Unauthorized, "unauthorized");
        await AssertError(await customer.Http.GetAsync("/api/v1/admin/settings"), HttpStatusCode.Forbidden, "forbidden");
        await AssertError(await customer.Http.PutAsJsonAsync("/api/v1/admin/settings/club", new { }), HttpStatusCode.Forbidden, "forbidden");
        await AssertError(await customer.Http.PostAsJsonAsync("/api/v1/admin/price-rules", new { }), HttpStatusCode.Forbidden, "forbidden");
    }

    [Fact]
    public async Task The_settings_page_gets_everything_in_one_call()
    {
        var admin = await api.AdminAsync();
        var s = await admin.Http.GetFromJsonAsync<JsonElement>("/api/v1/admin/settings");
        Assert.Equal("Asia/Bangkok", s.GetProperty("club").GetProperty("timezone").GetString());
        Assert.True(s.GetProperty("courts").GetArrayLength() >= 4);
        Assert.True(s.GetProperty("priceRules").GetArrayLength() >= 5);
        Assert.Equal(JsonValueKind.Array, s.GetProperty("operatingHours").ValueKind);
        Assert.Equal(JsonValueKind.Array, s.GetProperty("holidays").ValueKind);
    }

    [Fact]
    public async Task The_booking_window_and_reschedule_notice_are_set_by_the_admin()
    {
        var admin = await api.AdminAsync();
        var customer = await api.RegisterAsync();
        var before = await Club();
        object Settings(int window, int notice, string name = "Pickleball Club") => new { name, bookingWindowDays = window, rescheduleNoticeHours = notice };
        try
        {
            var saved = await Ok(admin.Http.PutAsJsonAsync("/api/v1/admin/settings/club", Settings(3, 12, "  Baan Pickle ")));
            Assert.Equal("Baan Pickle", saved.GetProperty("name").GetString());

            var club = await Club();
            Assert.Equal("Baan Pickle", club.GetProperty("name").GetString());
            Assert.Equal(3, club.GetProperty("bookingWindowDays").GetInt32());
            Assert.Equal(12, club.GetProperty("rescheduleNoticeHours").GetInt32());
            Assert.Equal(api.ClubToday().AddDays(3).ToString("yyyy-MM-dd"), club.GetProperty("lastBookableDate").GetString());

            // The new window binds a Customer straight away.
            var day4 = new Pick(Court4, api.ClubToday().AddDays(4), 10);
            await AssertError(await customer.HoldAsync(day4), HttpStatusCode.BadRequest, "outside_booking_window");
            Assert.Equal(HttpStatusCode.Created, (await customer.HoldAsync(day4 with { Date = api.ClubToday().AddDays(3) })).StatusCode);

            await AssertError(await admin.Http.PutAsJsonAsync("/api/v1/admin/settings/club", Settings(0, 12)), HttpStatusCode.BadRequest, "validation_failed");
            await AssertError(await admin.Http.PutAsJsonAsync("/api/v1/admin/settings/club", Settings(3, -1)), HttpStatusCode.BadRequest, "validation_failed");
            await AssertError(await admin.Http.PutAsJsonAsync("/api/v1/admin/settings/club", Settings(3, 12, " ")), HttpStatusCode.BadRequest, "validation_failed");
        }
        finally
        {
            await admin.Http.PutAsJsonAsync("/api/v1/admin/settings/club",
                Settings(before.GetProperty("bookingWindowDays").GetInt32(), before.GetProperty("rescheduleNoticeHours").GetInt32()));
        }
    }

    [Fact]
    public async Task A_court_is_added_renamed_and_switched_off_but_never_while_it_has_bookings_ahead()
    {
        var admin = await api.AdminAsync();
        var customer = await api.RegisterAsync();
        var name = "Court " + Guid.NewGuid().ToString("N")[..6];
        var id = await NewCourt(admin, name);
        var date = api.ClubToday().AddDays(6);

        Assert.Contains((await Club()).GetProperty("courts").EnumerateArray(), c => c.GetProperty("id").GetGuid() == id);
        Assert.NotEmpty(Slots(await Day(date), id)); // on the grid, priced by the Club-wide rules
        await AssertError(await admin.Http.PostAsJsonAsync("/api/v1/admin/courts", new { name, indoor = true, sortOrder = 1 }), HttpStatusCode.Conflict, "court_name_taken");
        await AssertError(await admin.Http.PostAsJsonAsync("/api/v1/admin/courts", new { name = " ", indoor = true, sortOrder = 1 }), HttpStatusCode.BadRequest, "validation_failed");

        var renamed = await Ok(admin.Http.PutAsJsonAsync($"/api/v1/admin/courts/{id}", new { name = name + " (roof)", indoor = true, sortOrder = 2, isActive = true }));
        Assert.Equal(name + " (roof)", renamed.GetProperty("name").GetString());
        Assert.True(renamed.GetProperty("indoor").GetBoolean());

        // With a Booking ahead it cannot be switched off.
        var booked = await api.BookAndPayAsync(customer, new Pick(id, date, 10));
        await AssertError(await admin.Http.PutAsJsonAsync($"/api/v1/admin/courts/{id}", new { name, indoor = true, sortOrder = 2, isActive = false }), HttpStatusCode.Conflict, "court_has_bookings");

        await Ok(admin.Http.PostAsJsonAsync($"/api/v1/admin/bookings/{booked.Id()}/cancel", new { refundNote = "test" }));
        var off = await Ok(admin.Http.PutAsJsonAsync($"/api/v1/admin/courts/{id}", new { name, indoor = true, sortOrder = 2, isActive = false }));
        Assert.False(off.GetProperty("isActive").GetBoolean());

        // Switched off: gone from the public site, still listed for the Admin, and not bookable.
        Assert.DoesNotContain((await Club()).GetProperty("courts").EnumerateArray(), c => c.GetProperty("id").GetGuid() == id);
        Assert.Contains((await admin.Http.GetFromJsonAsync<JsonElement>("/api/v1/admin/settings")).GetProperty("courts").EnumerateArray(), c => c.GetProperty("id").GetGuid() == id);
        await AssertError(await customer.HoldAsync(new Pick(id, date, 14)), HttpStatusCode.BadRequest, "court_not_found");
        await AssertError(await admin.Http.PutAsJsonAsync($"/api/v1/admin/courts/{Guid.NewGuid()}", new { name = "x", indoor = true, sortOrder = 2, isActive = true }), HttpStatusCode.NotFound, "court_not_found");
    }

    [Fact]
    public async Task Opening_hours_are_set_per_day_of_the_week_and_may_run_to_midnight()
    {
        var admin = await api.AdminAsync();
        var customer = await api.RegisterAsync();
        var (monday, tuesday) = (Next(DayOfWeek.Monday), Next(DayOfWeek.Tuesday));
        object Week(params object[] days) => new { days };
        object D(int dow, int open, int close) => new { dayOfWeek = dow, openHour = open, closeHour = close };
        var seeded = Week(Enumerable.Range(0, 7).Select(d => D(d, 6, 23)).ToArray());
        try
        {
            // Monday closed (left out), Tuesday 08:00–24:00, the rest as before.
            var days = new List<object> { D(0, 6, 23), D(2, 8, 24), D(3, 6, 23), D(4, 6, 23), D(5, 6, 23), D(6, 6, 23) };
            var saved = await Ok(admin.Http.PutAsJsonAsync("/api/v1/admin/hours", Week(days.ToArray())));
            Assert.Equal(6, saved.GetArrayLength());
            Assert.DoesNotContain((await Club()).GetProperty("operatingHours").EnumerateArray(), h => h.GetProperty("dayOfWeek").GetInt32() == 1);

            Assert.Empty(Slots(await Day(monday), Court4));
            await AssertError(await customer.HoldAsync(new Pick(Court4, monday, 10)), HttpStatusCode.BadRequest, "closed");

            var tue = Slots(await Day(tuesday), Court4);
            Assert.Equal(16, tue.Count);
            Assert.Equal("08:00", tue[0].GetProperty("start").GetString());
            Assert.Equal("00:00", tue[^1].GetProperty("end").GetString());
            // 23:00 is open now, but no Price Rule covers it yet: shown as closed and not bookable.
            Assert.Equal("closed", SlotAt(await Day(tuesday), Court4, "23:00").GetProperty("status").GetString());
            await AssertError(await customer.HoldAsync(new Pick(Court4, tuesday, 23), hours: 1), HttpStatusCode.BadRequest, "no_price");
            await AssertError(await customer.HoldAsync(new Pick(Court4, tuesday, 7), hours: 1), HttpStatusCode.BadRequest, "outside_hours");

            var late = await Ok(admin.Http.PostAsJsonAsync("/api/v1/admin/price-rules", Rule(null, "weekday", 23, 24, 250, "Late night")), HttpStatusCode.Created);
            var held = await customer.BookAsync(new Pick(Court4, tuesday, 22));
            Assert.Equal("00:00", held.GetProperty("end").GetString());
            Assert.Equal(650m, held.GetProperty("total").GetDecimal()); // 22:00 at 400 + 23:00 at 250
            await customer.Http.PostAsync($"/api/v1/me/bookings/{held.Id()}/release", null);
            await admin.Http.DeleteAsync($"/api/v1/admin/price-rules/{late.GetProperty("id").GetGuid()}");

            await AssertError(await admin.Http.PutAsJsonAsync("/api/v1/admin/hours", Week(D(1, 6, 23), D(1, 8, 20))), HttpStatusCode.BadRequest, "duplicate_day");
            await AssertError(await admin.Http.PutAsJsonAsync("/api/v1/admin/hours", Week(D(1, 10, 10))), HttpStatusCode.BadRequest, "bad_hours");
            await AssertError(await admin.Http.PutAsJsonAsync("/api/v1/admin/hours", Week(D(7, 6, 23))), HttpStatusCode.BadRequest, "validation_failed");
            await AssertError(await admin.Http.PutAsJsonAsync("/api/v1/admin/hours", Week(D(1, 6, 25))), HttpStatusCode.BadRequest, "validation_failed");
        }
        finally
        {
            await admin.Http.PutAsJsonAsync("/api/v1/admin/hours", seeded);
        }
        Assert.Equal(17, Slots(await Day(monday), Court4).Count);
    }

    [Fact]
    public async Task Price_rules_cannot_overlap_and_a_court_rule_beats_the_club_wide_one()
    {
        var admin = await api.AdminAsync();
        var customer = await api.RegisterAsync();
        var court = await NewCourt(admin, "Court " + Guid.NewGuid().ToString("N")[..6]);
        var weekday = Next(DayOfWeek.Wednesday);
        Task<HttpResponseMessage> Post(object rule) => admin.Http.PostAsJsonAsync("/api/v1/admin/price-rules", rule);
        decimal PriceAt(JsonElement day, string start) => SlotAt(day, court, start).GetProperty("price").GetDecimal();

        Assert.Equal(300m, PriceAt(await Day(weekday), "10:00")); // the Club-wide weekday rule

        var rule = await Ok(Post(Rule(court, "weekday", 9, 12, 500, "Morning special")), HttpStatusCode.Created);
        var id = rule.GetProperty("id").GetGuid();
        var day = await Day(weekday);
        Assert.Equal(500m, PriceAt(day, "09:00"));
        Assert.Equal(500m, PriceAt(day, "11:00"));
        Assert.Equal(300m, PriceAt(day, "12:00")); // the rule ends before 12:00
        Assert.Equal("Morning special", SlotAt(day, court, "10:00").GetProperty("label").GetString());

        // Same Court and Day Type: no shared hour allowed. Touching, another Day Type or another scope is fine.
        await AssertError(await Post(Rule(court, "weekday", 11, 14, 600)), HttpStatusCode.Conflict, "price_rule_overlap");
        await AssertError(await Post(Rule(court, "weekday", 8, 10, 600)), HttpStatusCode.Conflict, "price_rule_overlap");
        await AssertError(await Post(Rule(null, "weekday", 10, 12, 600)), HttpStatusCode.Conflict, "price_rule_overlap"); // clashes with the seeded Club-wide rule
        var next = await Ok(Post(Rule(court, "weekday", 12, 14, 550)), HttpStatusCode.Created);
        await Ok(Post(Rule(court, "holiday", 9, 12, 700)), HttpStatusCode.Created);

        await AssertError(await Post(Rule(court, "weekday", 14, 14, 1)), HttpStatusCode.BadRequest, "bad_hours");
        await AssertError(await Post(Rule(Guid.NewGuid(), "weekday", 14, 15, 1)), HttpStatusCode.BadRequest, "court_not_found");
        await AssertError(await Post(Rule(court, "sunday", 14, 15, 1)), HttpStatusCode.BadRequest, "validation_failed");
        await AssertError(await Post(Rule(court, "weekday", 14, 15, -5)), HttpStatusCode.BadRequest, "validation_failed");

        // A Booking keeps the price it was sold at when the rule changes afterwards.
        var booked = await api.BookAndPayAsync(customer, new Pick(court, weekday, 9));
        Assert.Equal(1000m, booked.GetProperty("total").GetDecimal());

        await AssertError(await admin.Http.PutAsJsonAsync($"/api/v1/admin/price-rules/{id}", Rule(court, "weekday", 9, 13, 520)), HttpStatusCode.Conflict, "price_rule_overlap"); // would run into the 12–14 rule
        var edited = await Ok(admin.Http.PutAsJsonAsync($"/api/v1/admin/price-rules/{id}", Rule(court, "weekday", 9, 12, 520)));
        Assert.Equal(520m, edited.GetProperty("pricePerHour").GetDecimal());
        Assert.Equal(1000m, (await customer.Http.GetFromJsonAsync<JsonElement>($"/api/v1/me/bookings/{booked.Id()}")).GetProperty("total").GetDecimal());

        // Removing the Court's rule falls back to the Club-wide price.
        Assert.Equal(HttpStatusCode.NoContent, (await admin.Http.DeleteAsync($"/api/v1/admin/price-rules/{next.GetProperty("id").GetGuid()}")).StatusCode);
        Assert.Equal(300m, PriceAt(await Day(weekday), "12:00"));
        await AssertError(await admin.Http.DeleteAsync($"/api/v1/admin/price-rules/{Guid.NewGuid()}"), HttpStatusCode.NotFound, "price_rule_not_found");
        await AssertError(await admin.Http.PutAsJsonAsync($"/api/v1/admin/price-rules/{Guid.NewGuid()}", Rule(court, "weekday", 20, 21, 1)), HttpStatusCode.NotFound, "price_rule_not_found");
    }

    [Fact]
    public async Task A_day_the_admin_adds_is_priced_as_a_holiday()
    {
        var admin = await api.AdminAsync();
        var thursday = Next(DayOfWeek.Thursday);
        var path = $"/api/v1/admin/holidays/{thursday:yyyy-MM-dd}";

        Assert.Equal("weekday", (await Day(thursday)).GetProperty("dayType").GetString());
        Assert.Equal(300m, SlotAt(await Day(thursday), Court1, "10:00").GetProperty("price").GetDecimal());

        await Ok(admin.Http.PutAsJsonAsync("/api/v1/admin/holidays", new { date = thursday.ToString("yyyy-MM-dd"), note = "Club anniversary" }));
        var day = await Day(thursday);
        Assert.Equal("holiday", day.GetProperty("dayType").GetString());
        Assert.Equal(400m, SlotAt(day, Court1, "10:00").GetProperty("price").GetDecimal());
        Assert.Equal(400m, SlotAt(day, Court1, "18:00").GetProperty("price").GetDecimal()); // not the weekday "Indoor peak" 450

        // Saving the same day again only changes the note.
        await Ok(admin.Http.PutAsJsonAsync("/api/v1/admin/holidays", new { date = thursday.ToString("yyyy-MM-dd"), note = "Renamed" }));
        var listed = (await admin.Http.GetFromJsonAsync<JsonElement>("/api/v1/admin/settings")).GetProperty("holidays").EnumerateArray()
            .Where(h => h.GetProperty("date").GetString() == thursday.ToString("yyyy-MM-dd")).ToList();
        Assert.Equal("Renamed", Assert.Single(listed).GetProperty("note").GetString());

        Assert.Equal(HttpStatusCode.NoContent, (await admin.Http.DeleteAsync(path)).StatusCode);
        Assert.Equal("weekday", (await Day(thursday)).GetProperty("dayType").GetString());
        Assert.Equal(HttpStatusCode.NoContent, (await admin.Http.DeleteAsync(path)).StatusCode); // removing it twice is harmless
    }
}
