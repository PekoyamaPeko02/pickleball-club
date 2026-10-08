using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using PickleballClub.IntegrationTests.Infrastructure;

namespace PickleballClub.IntegrationTests;

[Collection(SettingsCollection.Name)]
public class ReportTests(ApiFactory api)
{
    private static async Task AssertError(HttpResponseMessage res, HttpStatusCode status, string code)
    {
        var body = await res.Content.ReadAsStringAsync();
        Assert.True(res.StatusCode == status, $"expected {(int)status} {code}, got {(int)res.StatusCode}: {body}");
        Assert.Equal(code, JsonDocument.Parse(body).RootElement.GetProperty("code").GetString());
    }

    private static string D(DateOnly d) => d.ToString("yyyy-MM-dd");

    private static async Task<JsonElement> Report(TestUser admin, DateOnly from, DateOnly to) =>
        await admin.Http.GetFromJsonAsync<JsonElement>($"/api/v1/admin/reports/revenue?from={D(from)}&to={D(to)}");

    private static JsonElement DayOf(JsonElement report, DateOnly date) =>
        report.GetProperty("days").EnumerateArray().Single(d => d.GetProperty("date").GetString() == D(date));

    [Fact]
    public async Task Revenue_is_counted_by_the_day_of_play_split_online_and_walk_in()
    {
        var admin = await api.AdminAsync();
        var customer = await api.RegisterAsync();
        // Its own Court keeps these Bookings away from the other tests; the days are far enough ahead to be empty.
        var court = (await (await admin.Http.PostAsJsonAsync("/api/v1/admin/courts", new { name = "Report " + Guid.NewGuid().ToString("N")[..6], indoor = false, sortOrder = 80 }))
            .Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var today = api.ClubToday();
        var (onlineDay, walkInDay) = (today.AddDays(12), today.AddDays(70));
        var before = await Report(admin, onlineDay, onlineDay);

        var online = await api.BookAndPayAsync(customer, new Pick(court, onlineDay, 10), hours: 2);
        var held = await (await api.RegisterAsync()).BookAsync(new Pick(court, onlineDay, 14));                     // not paid: does not count
        var refunded = await api.BookAndPayAsync(await api.RegisterAsync(), new Pick(court, onlineDay, 18));
        await admin.Http.PostAsJsonAsync($"/api/v1/admin/bookings/{refunded.Id()}/cancel", new { refundNote = "test" }); // cancelled: does not count
        await admin.WalkInAsync(court, walkInDay, 9, hours: 3, guestName: "Smith, John", guestPhone: "0890001111");
        await admin.WalkInAsync(court, walkInDay, 15, hours: 1, guestName: "=cmd|' /C calc'!A0", guestPhone: "0890002222");

        var day = DayOf(await Report(admin, onlineDay, onlineDay), onlineDay);
        var was = DayOf(before, onlineDay);
        decimal Delta(string field) => day.GetProperty(field).GetDecimal() - was.GetProperty(field).GetDecimal();
        Assert.Equal(1, Delta("bookings"));
        Assert.Equal(2, Delta("hours"));
        Assert.Equal(online.GetProperty("total").GetDecimal(), Delta("online"));
        Assert.Equal(0, Delta("walkIn"));
        Assert.Equal(online.GetProperty("total").GetDecimal(), Delta("total"));
        Assert.True(day.GetProperty("availableHours").GetInt32() > 0);

        var far = await Report(admin, walkInDay.AddDays(-1), walkInDay.AddDays(1));
        Assert.Equal(3, far.GetProperty("days").GetArrayLength()); // every day of the range, even empty ones
        var w = DayOf(far, walkInDay);
        Assert.Equal(2, w.GetProperty("bookings").GetInt32());
        Assert.Equal(4, w.GetProperty("hours").GetInt32());
        Assert.Equal(0m, w.GetProperty("online").GetDecimal());
        Assert.True(w.GetProperty("walkIn").GetDecimal() > 0);
        Assert.Equal(0, DayOf(far, walkInDay.AddDays(1)).GetProperty("bookings").GetInt32());

        // ---- CSV files
        var summary = await admin.Http.GetAsync($"/api/v1/admin/reports/revenue.csv?from={D(walkInDay)}&to={D(walkInDay)}");
        Assert.Equal("text/csv", summary.Content.Headers.ContentType!.MediaType);
        var bytes = await summary.Content.ReadAsByteArrayAsync();
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]); // BOM, so Excel reads Thai names
        var lines = Encoding.UTF8.GetString(bytes[3..]).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.StartsWith("Date,Bookings,Hours sold,", lines[0]);
        Assert.StartsWith($"{D(walkInDay)},2,4,0,", lines[1]);

        var list = await (await admin.Http.GetAsync($"/api/v1/admin/reports/bookings.csv?from={D(walkInDay)}&to={D(walkInDay)}")).Content.ReadAsStringAsync();
        Assert.Contains("\"Smith, John\"", list);              // a comma in a name is quoted
        Assert.Contains(",'=cmd|' /C calc'!A0,", list);        // a name that looks like a formula is defused
        Assert.DoesNotContain(",=cmd", list);
        Assert.Contains(",walk_in,cash,confirmed,", list);
        Assert.DoesNotContain(held.GetProperty("code").GetString()!, list);
    }

    [Fact]
    public async Task A_report_needs_an_admin_and_a_sensible_range()
    {
        var admin = await api.AdminAsync();
        var today = api.ClubToday();

        var byDefault = await admin.Http.GetFromJsonAsync<JsonElement>("/api/v1/admin/reports/revenue");
        Assert.Equal(30, byDefault.GetProperty("days").GetArrayLength());
        Assert.Equal(D(today), byDefault.GetProperty("to").GetString());

        await AssertError(await admin.Http.GetAsync($"/api/v1/admin/reports/revenue?from={D(today)}&to={D(today.AddDays(-1))}"), HttpStatusCode.BadRequest, "bad_range");
        await AssertError(await admin.Http.GetAsync($"/api/v1/admin/reports/revenue?from={D(today.AddDays(-400))}&to={D(today)}"), HttpStatusCode.BadRequest, "range_too_long");
        await AssertError(await (await api.RegisterAsync()).Http.GetAsync("/api/v1/admin/reports/revenue"), HttpStatusCode.Forbidden, "forbidden");
        await AssertError(await api.Anonymous().GetAsync("/api/v1/admin/reports/bookings.csv"), HttpStatusCode.Unauthorized, "unauthorized");
    }
}
