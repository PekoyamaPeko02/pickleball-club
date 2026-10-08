using System.Net.Http.Json;
using System.Text.Json;
using PickleballClub.Api.Services;

namespace PickleballClub.IntegrationTests.Infrastructure;

/// <summary>A two-hour block on one Court that no other test uses.</summary>
public sealed record Pick(Guid CourtId, DateOnly Date, int StartHour)
{
    public const string Bangkok = "Asia/Bangkok";
    public bool Indoor => CourtId == SlotPicker.Courts[0] || CourtId == SlotPicker.Courts[1];
    public bool Weekday => Date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);
    public DateTime StartAt => ClubClock.ToUtc(Date, StartHour, Bangkok);
}

/// <summary>
/// Hands out blocks from the seeded Club so tests never collide in the shared database: days +7 … +13 from the day the
/// run started, every Court, even start hours 06:00–20:00. SmokeTests use today +2 … +4 and tests may push the shared
/// clock forward by a day or so, hence the gap.
/// </summary>
public static class SlotPicker
{
    public static readonly Guid[] Courts =
    [
        Guid.Parse("c0000000-0000-0000-0000-000000000001"), // indoor
        Guid.Parse("c0000000-0000-0000-0000-000000000002"), // indoor
        Guid.Parse("c0000000-0000-0000-0000-000000000003"),
        Guid.Parse("c0000000-0000-0000-0000-000000000004"),
    ];

    private static readonly object Gate = new();
    private static List<Pick>? _free;

    /// <param name="weekday">True = Monday–Friday only, false = Saturday/Sunday only.</param>
    public static Pick Next(ApiFactory api, bool? weekday = null, int? startHour = null, bool? indoor = null)
    {
        lock (Gate)
        {
            if (_free is null)
            {
                var today = api.ClubToday();
                _free = (from day in Enumerable.Range(7, 7)
                         from hour in Enumerable.Range(0, 8).Select(i => 6 + i * 2)
                         from court in Courts
                         select new Pick(court, today.AddDays(day), hour)).ToList();
            }
            var pick = _free.FirstOrDefault(p =>
                           (weekday is null || p.Weekday == weekday) && (startHour is null || p.StartHour == startHour) && (indoor is null || p.Indoor == indoor))
                       ?? throw new InvalidOperationException("SlotPicker ran out of free blocks for that filter");
            _free.Remove(pick);
            return pick;
        }
    }

    /// <summary>
    /// The first of <paramref name="blocks"/> consecutive free blocks on one Court and day (all of them are taken out of
    /// the pool), for tests that book around the edges of a block.
    /// </summary>
    public static Pick NextRun(ApiFactory api, int blocks, bool? weekday = null, int? startHour = null, bool? indoor = null)
    {
        lock (Gate)
        {
            while (true)
            {
                var first = Next(api, weekday, startHour, indoor);
                var rest = Enumerable.Range(1, blocks - 1).Select(i => first with { StartHour = first.StartHour + i * 2 }).ToList();
                if (!rest.All(_free!.Contains)) continue; // a neighbour is in use: try the next candidate (this one stays out of the pool)
                foreach (var r in rest) _free!.Remove(r);
                return first;
            }
        }
    }
}

public static class BookingHelpers
{
    public static Task<HttpResponseMessage> HoldAsync(this TestUser user, Pick pick, int hours = 2, string method = "promptpay", string? key = null)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/bookings")
        {
            Content = JsonContent.Create(new { courtId = pick.CourtId, date = pick.Date.ToString("yyyy-MM-dd"), startHour = pick.StartHour, hours, method }),
        };
        if (key is not null) req.Headers.Add("Idempotency-Key", key);
        return user.Http.SendAsync(req);
    }

    /// <summary>Holds the block and returns the Booking; fails the test when the Hold is refused.</summary>
    public static async Task<JsonElement> BookAsync(this TestUser user, Pick pick, int hours = 2, string method = "promptpay")
    {
        var res = await user.HoldAsync(pick, hours, method);
        if (!res.IsSuccessStatusCode) throw new InvalidOperationException($"hold failed: {(int)res.StatusCode} {await res.Content.ReadAsStringAsync()}");
        return await res.Content.ReadFromJsonAsync<JsonElement>();
    }

    /// <summary>Pretends the Customer paid (the Development-only simulate endpoint) and returns the Booking afterwards.</summary>
    public static async Task<JsonElement> PayAsync(this ApiFactory api, Guid bookingId)
    {
        var res = await api.Anonymous().PostAsync($"/api/v1/dev/bookings/{bookingId}/simulate-payment", null);
        if (!res.IsSuccessStatusCode) throw new InvalidOperationException($"simulate-payment failed: {(int)res.StatusCode} {await res.Content.ReadAsStringAsync()}");
        return await res.Content.ReadFromJsonAsync<JsonElement>();
    }

    /// <summary>Holds and pays: a confirmed Booking.</summary>
    public static async Task<JsonElement> BookAndPayAsync(this ApiFactory api, TestUser user, Pick pick, int hours = 2)
    {
        var held = await user.BookAsync(pick, hours);
        return await api.PayAsync(held.GetProperty("id").GetGuid());
    }

    /// <summary>
    /// Makes sure the Club is open right now (06:20–21:40 local) by moving the shared clock forward if needed, and returns
    /// today's date and the current hour there. For tests that need a Booking that is happening now.
    /// </summary>
    public static (DateOnly Today, int Hour) EnsureOpenNow(this ApiFactory api)
    {
        var local = ClubClock.ToLocal(api.Clock.GetUtcNow().UtcDateTime, Pick.Bangkok);
        var minutes = local.Hour * 60 + local.Minute;
        if (minutes < 6 * 60 + 20) api.Clock.Advance(TimeSpan.FromMinutes(6 * 60 + 20 - minutes));
        else if (minutes > 21 * 60 + 40) api.Clock.Advance(TimeSpan.FromMinutes(24 * 60 - minutes + 6 * 60 + 20));
        local = ClubClock.ToLocal(api.Clock.GetUtcNow().UtcDateTime, Pick.Bangkok);
        return (DateOnly.FromDateTime(local), local.Hour);
    }

    public static Task<HttpResponseMessage> WalkInAsync(this TestUser admin, Guid courtId, DateOnly date, int startHour, int hours = 1,
        string guestName = "Walk-in Guest", string guestPhone = "089-000-1111", string payment = "cash") =>
        admin.Http.PostAsJsonAsync("/api/v1/admin/bookings/walk-in",
            new { courtId, date = date.ToString("yyyy-MM-dd"), startHour, hours, guestName, guestPhone, payment });

    public static Guid Id(this JsonElement booking) => booking.GetProperty("id").GetGuid();
    public static string Status(this JsonElement booking) => booking.GetProperty("status").GetString()!;

    /// <summary>The public status of one Slot (available, held, booked, …).</summary>
    public static async Task<string> SlotStatusAsync(this ApiFactory api, Pick pick, int? hour = null)
    {
        var day = await api.Anonymous().GetFromJsonAsync<JsonElement>($"/api/v1/availability?date={pick.Date:yyyy-MM-dd}");
        var court = day.GetProperty("courts").EnumerateArray().Single(c => c.GetProperty("courtId").GetGuid() == pick.CourtId);
        var label = ClubClock.Label(hour ?? pick.StartHour);
        return court.GetProperty("slots").EnumerateArray().Single(s => s.GetProperty("start").GetString() == label).GetProperty("status").GetString()!;
    }
}
