using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using PickleballClub.Api.Data;
using PickleballClub.Api.Domain;
using PickleballClub.Api.Services;

namespace PickleballClub.Api.Features.Admin;

/// <param name="Hours">Court-hours sold.</param>
/// <param name="Online">Money from online Bookings (THB).</param>
/// <param name="WalkIn">Money taken at the counter for Walk-in Bookings (THB).</param>
/// <param name="AvailableHours">Court-hours on offer that day: Operating Hours × Courts open for booking (as set up today).</param>
public record RevenueDay(DateOnly Date, int Bookings, int Hours, decimal Online, decimal WalkIn, decimal Total, int AvailableHours);

public record RevenueReport(DateOnly From, DateOnly To, List<RevenueDay> Days);

/// <summary>
/// Revenue by the day of play. A Booking counts once it is paid for and as long as the Club keeps the money:
/// confirmed, checked in, completed or no-show. Holds and cancelled (refunded) Bookings do not count.
/// </summary>
public static class ReportEndpoints
{
    private static readonly string[] Earning = [BookingStatus.Confirmed, BookingStatus.CheckedIn, BookingStatus.Completed, BookingStatus.NoShow];
    private const int MaxDays = 366;

    public static void MapReports(this RouteGroupBuilder admin)
    {
        // ?from=yyyy-MM-dd&to=yyyy-MM-dd, both inclusive, the Club's local dates. Default: the last 30 days.
        admin.MapGet("/reports/revenue", async (DateOnly? from, DateOnly? to, AppDbContext db, TimeProvider clock, CancellationToken ct) =>
            await BuildAsync(db, clock, from, to, ct));

        admin.MapGet("/reports/revenue.csv", async (DateOnly? from, DateOnly? to, AppDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            var report = await BuildAsync(db, clock, from, to, ct);
            var csv = new StringBuilder("Date,Bookings,Hours sold,Online (THB),Walk-in (THB),Total (THB),Hours available,Occupancy %\r\n");
            foreach (var d in report.Days)
                csv.Append(CultureInfo.InvariantCulture,
                    $"{d.Date:yyyy-MM-dd},{d.Bookings},{d.Hours},{d.Online:0.##},{d.WalkIn:0.##},{d.Total:0.##},{d.AvailableHours},{(d.AvailableHours == 0 ? 0 : 100m * d.Hours / d.AvailableHours):0.#}\r\n");
            return Csv(csv, $"revenue_{report.From:yyyyMMdd}_{report.To:yyyyMMdd}.csv");
        });

        // One row per Booking that earned money in the range, for the accounts.
        admin.MapGet("/reports/bookings.csv", async (DateOnly? from, DateOnly? to, AppDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            var club = await db.ClubSettings.AsNoTracking().SingleAsync(ct);
            var (first, last) = Range(from, to, club, clock);
            var ids = await db.Bookings.AsNoTracking()
                .Where(b => Earning.Contains(b.Status) && b.StartAt >= ClubClock.ToUtc(first, 0, club.Timezone) && b.StartAt < ClubClock.ToUtc(last, 24, club.Timezone))
                .OrderBy(b => b.StartAt).Select(b => b.Id).ToListAsync(ct);
            var rows = await AdminEndpoints.LoadManyAsync(db, ids, ct);

            var csv = new StringBuilder("Code,Date,Start,End,Hours,Court,Customer,Phone,Email,Source,Payment,Status,Total (THB)\r\n");
            foreach (var b in rows)
                csv.Append(CultureInfo.InvariantCulture,
                    $"{b.Code},{b.Date:yyyy-MM-dd},{b.Start},{b.End},{b.Hours},{Cell(b.CourtName)},{Cell(b.CustomerName)},{Cell(b.CustomerPhone)},{Cell(b.CustomerEmail)},{b.Source},{b.PaymentMethod},{b.Status},{b.Total:0.##}\r\n");
            return Csv(csv, $"bookings_{first:yyyyMMdd}_{last:yyyyMMdd}.csv");
        });
    }

    private static async Task<RevenueReport> BuildAsync(AppDbContext db, TimeProvider clock, DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        var club = await db.ClubSettings.AsNoTracking().SingleAsync(ct);
        var (first, last) = Range(from, to, club, clock);

        var bookings = await db.Bookings.AsNoTracking()
            .Where(b => Earning.Contains(b.Status) && b.StartAt >= ClubClock.ToUtc(first, 0, club.Timezone) && b.StartAt < ClubClock.ToUtc(last, 24, club.Timezone))
            .Select(b => new { b.StartAt, b.EndAt, b.Total, b.Source })
            .ToListAsync(ct);
        var byDay = bookings.ToLookup(b => DateOnly.FromDateTime(ClubClock.ToLocal(b.StartAt, club.Timezone)));

        var courts = await db.Courts.AsNoTracking().CountAsync(c => c.IsActive, ct);
        var hours = await db.OperatingHours.AsNoTracking().ToDictionaryAsync(h => (int)h.DayOfWeek, h => h.CloseHour - h.OpenHour, ct);

        var days = new List<RevenueDay>();
        for (var d = first; d <= last; d = d.AddDays(1))
        {
            var rows = byDay[d].ToList();
            var online = rows.Where(b => b.Source == BookingSource.Online).Sum(b => b.Total);
            var walkIn = rows.Where(b => b.Source == BookingSource.WalkIn).Sum(b => b.Total);
            days.Add(new RevenueDay(d, rows.Count, rows.Sum(b => (int)Math.Round((b.EndAt - b.StartAt).TotalHours)), online, walkIn, online + walkIn,
                hours.GetValueOrDefault((int)d.DayOfWeek) * courts));
        }
        return new RevenueReport(first, last, days);
    }

    private static (DateOnly From, DateOnly To) Range(DateOnly? from, DateOnly? to, ClubSettings club, TimeProvider clock)
    {
        var today = ClubClock.Today(club.Timezone, clock.GetUtcNow().UtcDateTime);
        var last = to ?? today;
        var first = from ?? last.AddDays(-29);
        if (first > last) throw new ApiException(400, "bad_range", "The first day must not be after the last day.");
        if (last.DayNumber - first.DayNumber >= MaxDays) throw new ApiException(400, "range_too_long", $"A report can cover up to {MaxDays} days.");
        return (first, last);
    }

    /// <summary>
    /// One CSV field: quoted when it holds a comma, quote or line break, and never allowed to start with a character a
    /// spreadsheet would run as a formula (a customer can type their own name).
    /// </summary>
    internal static string Cell(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        if (value[0] is '=' or '+' or '-' or '@' or '\t' or '\r') value = "'" + value;
        return value.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
    }

    /// <summary>UTF-8 with a byte-order mark, so Excel reads Thai names correctly.</summary>
    private static IResult Csv(StringBuilder csv, string fileName) =>
        Results.File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray(), "text/csv; charset=utf-8", fileName);
}
