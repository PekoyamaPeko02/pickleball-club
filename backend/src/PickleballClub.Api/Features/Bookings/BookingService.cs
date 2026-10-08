using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using PickleballClub.Api.Data;
using PickleballClub.Api.Domain;
using PickleballClub.Api.Features.Auth;
using PickleballClub.Api.Features.Payments;
using PickleballClub.Api.Services;

namespace PickleballClub.Api.Features.Bookings;

/// <param name="StartHour">Hour of the Club's local day the first Slot starts (0–23).</param>
/// <param name="Hours">Number of consecutive Slots.</param>
public record CreateBookingRequest(
    [property: NotEmpty] Guid CourtId,
    DateOnly Date,
    [property: Range(0, 23)] int StartHour,
    [property: Range(1, 24)] int Hours,
    [property: Required, RegularExpression("^(promptpay|card)$")] string Method);

/// <summary>Where a Reschedule moves the Booking to; the number of Slots stays the same.</summary>
public record RescheduleRequest(
    [property: NotEmpty] Guid CourtId,
    DateOnly Date,
    [property: Range(0, 23)] int StartHour);

public record NewPaymentRequest([property: Required, RegularExpression("^(promptpay|card)$")] string Method);

public class BookingOptions
{
    /// <summary>How long a Hold keeps the Slots while the Customer pays.</summary>
    public int HoldMinutes { get; set; } = 10;
}

/// <summary>A priced run of consecutive Slots on one Court, validated against the Operating Hours and Price Rules.</summary>
public record Quote(Court Court, DateOnly Date, int StartHour, int Hours, DateTime StartAt, DateTime EndAt, decimal Total, string Timezone);

public class BookingService(
    AppDbContext db,
    IPaymentProvider payments,
    TimeProvider clock,
    IOptions<BookingOptions> options,
    IOptions<AppOptions> app,
    BookingMailer mailer,
    ILogger<BookingService> log)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    // ---------------------------------------------------------------- quoting

    /// <summary>
    /// Prices <paramref name="hours"/> consecutive Slots on one Court; never trusts a client price.
    /// Throws when the Court is unknown, the run is outside the Operating Hours, a Slot has no Price Rule, or it has started.
    /// </summary>
    /// <param name="allowInProgress">A Walk-in Booking may take a Slot that has already begun.</param>
    public async Task<Quote> QuoteAsync(Guid courtId, DateOnly date, int startHour, int hours, bool allowInProgress, CancellationToken ct)
    {
        var club = await db.ClubSettings.AsNoTracking().SingleAsync(ct);
        var court = await db.Courts.AsNoTracking().FirstOrDefaultAsync(c => c.Id == courtId && c.IsActive, ct)
                    ?? throw new ApiException(400, "court_not_found", "That court does not exist.");
        var open = await db.OperatingHours.AsNoTracking().FirstOrDefaultAsync(h => h.DayOfWeek == (short)date.DayOfWeek, ct)
                   ?? throw new ApiException(400, "closed", "The club is closed on that day.");
        if (hours < 1 || startHour < open.OpenHour || startHour + hours > open.CloseHour)
            throw new ApiException(400, "outside_hours", "That time is outside the opening hours.");

        var startAt = ClubClock.ToUtc(date, startHour, club.Timezone);
        var endAt = ClubClock.ToUtc(date, startHour + hours, club.Timezone);
        var now = Now;
        if (allowInProgress ? ClubClock.ToUtc(date, startHour + 1, club.Timezone) <= now : startAt <= now)
            throw new ApiException(400, "slot_in_past", "That time has already started.");

        var isAddedHoliday = await db.Holidays.AsNoTracking().AnyAsync(h => h.HolidayDate == date, ct);
        var dayType = Pricing.DayTypeOf(date, isAddedHoliday ? new HashSet<DateOnly> { date } : new HashSet<DateOnly>());
        var rules = await db.PriceRules.AsNoTracking().Where(r => r.DayType == dayType).ToListAsync(ct);
        decimal total = 0;
        for (var h = startHour; h < startHour + hours; h++)
            total += (Pricing.PriceFor(rules, court.Id, dayType, h)
                      ?? throw new ApiException(400, "no_price", $"{ClubClock.Label(h)} is not on sale.")).Price;

        return new Quote(court, date, startHour, hours, startAt, endAt, total, club.Timezone);
    }

    /// <summary>Court Blocks are not covered by the DB exclusion constraint, so check them here.</summary>
    public async Task EnsureNotBlockedAsync(Guid courtId, DateTime startAt, DateTime endAt, CancellationToken ct)
    {
        if (await db.CourtBlocks.AnyAsync(b => b.CourtId == courtId && b.StartAt < endAt && b.EndAt > startAt, ct))
            throw new ApiException(409, "slot_blocked", "That court is not available at that time.");
    }

    // ---------------------------------------------------------------- hold

    /// <summary>A Customer starts a Booking: the Slots are held for <see cref="BookingOptions.HoldMinutes"/> and a payment is opened.</summary>
    public async Task<Booking> CreateHoldAsync(Guid userId, CreateBookingRequest req, string? idempotencyKey, CancellationToken ct)
    {
        if (idempotencyKey is not null && await FindByKeyAsync(userId, idempotencyKey, ct) is { } replay) return replay;

        var user = await db.Users.AsNoTracking().FirstAsync(u => u.Id == userId, ct);
        if (!Sessions.ProfileComplete(user))
            throw new ApiException(409, "profile_incomplete", "Add your name and phone number before you book.");

        var club = await db.ClubSettings.AsNoTracking().SingleAsync(ct);
        var now = Now;
        var today = ClubClock.Today(club.Timezone, now);
        if (req.Date < today || req.Date > today.AddDays(club.BookingWindowDays))
            throw new ApiException(400, "outside_booking_window", $"You can book up to {club.BookingWindowDays} days ahead.");

        var quote = await QuoteAsync(req.CourtId, req.Date, req.StartHour, req.Hours, allowInProgress: false, ct);
        await EnsureNotBlockedAsync(quote.Court.Id, quote.StartAt, quote.EndAt, ct);

        // A Hold of this Customer that ran out but has not been swept yet must not count as "one open Hold".
        await db.Bookings.Where(b => b.UserId == userId && b.Status == BookingStatus.Held && b.HoldExpiresAt <= now)
            .ExecuteUpdateAsync(s => s.SetProperty(b => b.Status, BookingStatus.Expired).SetProperty(b => b.UpdatedAt, now), ct);

        var holdUntil = now.AddMinutes(options.Value.HoldMinutes);
        var booking = new Booking
        {
            Id = Guid.NewGuid(), Source = BookingSource.Online, UserId = userId, CourtId = quote.Court.Id,
            StartAt = quote.StartAt, EndAt = quote.EndAt, Status = BookingStatus.Held, Total = quote.Total,
            HoldExpiresAt = holdUntil, IdempotencyKey = idempotencyKey,
            CreatedAt = now, UpdatedAt = now, // from the app's clock, not the database's, so every rule compares like with like
        };
        db.Bookings.Add(booking);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (PgError(ex) is { SqlState: PostgresErrorCodes.UniqueViolation } || IsSlotContention(ex))
        {
            db.ChangeTracker.Clear();
            // A concurrent request with the same Idempotency-Key (double tap / client retry) won the race: replay its Booking.
            if (idempotencyKey is not null && await FindByKeyAsync(userId, idempotencyKey, ct) is { } winner) return winner;
            if (IsSlotContention(ex))
                throw new ApiException(409, "slot_taken", "Someone has just taken that time. Please choose another.");
            if (PgError(ex)?.ConstraintName == "ux_bookings_one_hold_per_user")
                throw new ApiException(409, "open_hold_exists", "You have a booking waiting for payment. Pay for it or release it first.");
            throw;
        }
        await db.Entry(booking).ReloadAsync(ct); // pick up the DB-generated code

        try
        {
            await OpenPaymentAsync(booking, req.Method, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not ApiException)
        {
            // No way to pay: give the Slots back straight away rather than holding them for nothing.
            booking.Status = BookingStatus.Expired;
            booking.UpdatedAt = Now;
            await db.SaveChangesAsync(ct);
            log.LogError(ex, "Could not open a payment for booking {Code}", booking.Code);
            throw new ApiException(502, "payment_unavailable", "We could not start the payment. Please try again in a moment.");
        }

        log.LogInformation("Booking {Code} held for customer {User}, total {Total}", booking.Code, userId, booking.Total);
        return booking;
    }

    /// <summary>Another way to pay for the same Hold (e.g. card instead of the QR). The Hold's deadline does not move.</summary>
    public async Task<Booking> NewPaymentAsync(Guid userId, Guid bookingId, string method, CancellationToken ct)
    {
        var booking = await db.Bookings.FirstOrDefaultAsync(b => b.Id == bookingId && b.UserId == userId, ct)
                      ?? throw new ApiException(404, "booking_not_found", "That booking does not exist.");
        if (booking.Status != BookingStatus.Held || booking.HoldExpiresAt <= Now)
            throw new ApiException(409, "not_held", "This booking is no longer waiting for payment.");
        await OpenPaymentAsync(booking, method, ct);
        return booking;
    }

    private async Task OpenPaymentAsync(Booking booking, string method, CancellationToken ct)
    {
        var returnUrl = $"{app.Value.WebUrl.TrimEnd('/')}/bookings/{booking.Id}";
        var intent = await payments.CreateAsync(new PaymentRequest(booking.Id, booking.Code, booking.Total, method, booking.HoldExpiresAt!.Value, returnUrl), ct);
        db.Payments.Add(new Payment
        {
            Id = Guid.NewGuid(), BookingId = booking.Id, Provider = intent.Provider, ProviderRef = intent.ProviderRef, Method = intent.Method,
            Amount = booking.Total, Status = PaymentStatus.Pending, QrPayload = intent.QrPayload, CheckoutUrl = intent.CheckoutUrl, ExpiresAt = intent.ExpiresAt,
        });
        await db.SaveChangesAsync(ct);
    }

    /// <summary>The Customer gives an unpaid Hold back before it runs out.</summary>
    public async Task<Booking> ReleaseHoldAsync(Guid userId, Guid bookingId, CancellationToken ct)
    {
        var booking = await db.Bookings.FirstOrDefaultAsync(b => b.Id == bookingId && b.UserId == userId, ct)
                      ?? throw new ApiException(404, "booking_not_found", "That booking does not exist.");
        if (booking.Status == BookingStatus.Expired) return booking;
        if (booking.Status != BookingStatus.Held)
            throw new ApiException(409, "not_held", "This booking is no longer waiting for payment.");
        booking.Status = BookingStatus.Expired;
        booking.UpdatedAt = Now;
        await db.SaveChangesAsync(ct);
        return booking;
    }

    /// <summary>Releases Holds whose time ran out. Returns the number released.</summary>
    public Task<int> ExpireHoldsAsync(CancellationToken ct)
    {
        var now = Now;
        return db.Bookings.Where(b => b.Status == BookingStatus.Held && b.HoldExpiresAt < now)
            .ExecuteUpdateAsync(s => s.SetProperty(b => b.Status, BookingStatus.Expired).SetProperty(b => b.UpdatedAt, now), ct);
    }

    // ---------------------------------------------------------------- reschedule

    /// <summary>
    /// The Customer moves their own confirmed Booking: once, at least the Club's notice hours before play, to the same number
    /// of Slots on any Court inside the Booking Window, and never to a time that costs more than was paid. Nothing is charged
    /// or refunded. The move is one UPDATE, so the exclusion constraint guards the new time like any other Booking.
    /// </summary>
    public async Task<Booking> RescheduleAsync(Guid userId, Guid bookingId, RescheduleRequest req, CancellationToken ct)
    {
        var booking = await db.Bookings.AsNoTracking().FirstOrDefaultAsync(b => b.Id == bookingId && b.UserId == userId, ct)
                      ?? throw new ApiException(404, "booking_not_found", "That booking does not exist.");
        var club = await db.ClubSettings.AsNoTracking().SingleAsync(ct);
        var now = Now;

        if (booking.Status != BookingStatus.Confirmed)
            throw new ApiException(409, "not_reschedulable", "Only a confirmed booking can be moved.");
        if (booking.RescheduledAt is not null) throw AlreadyRescheduled();
        if (now > booking.StartAt.AddHours(-club.RescheduleNoticeHours))
            throw new ApiException(409, "too_late_to_reschedule", $"A booking can be moved up to {club.RescheduleNoticeHours} hours before it starts.");

        var today = ClubClock.Today(club.Timezone, now);
        if (req.Date < today || req.Date > today.AddDays(club.BookingWindowDays))
            throw new ApiException(400, "outside_booking_window", $"You can move a booking to a day within the next {club.BookingWindowDays} days.");

        var hours = (int)Math.Round((booking.EndAt - booking.StartAt).TotalHours);
        var quote = await QuoteAsync(req.CourtId, req.Date, req.StartHour, hours, allowInProgress: false, ct);
        if (quote.Court.Id == booking.CourtId && quote.StartAt == booking.StartAt)
            throw new ApiException(400, "same_time", "That is the time you already have.");
        if (quote.Total > booking.Total)
            throw new ApiException(400, "costs_more", "That time costs more than you paid. Choose a time at the same price or less.");
        await EnsureNotBlockedAsync(quote.Court.Id, quote.StartAt, quote.EndAt, ct);

        int moved;
        try
        {
            // Conditional on "still confirmed and never rescheduled", so two requests at once cannot both use the one Reschedule.
            moved = await db.Bookings
                .Where(b => b.Id == bookingId && b.Status == BookingStatus.Confirmed && b.RescheduledAt == null)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(b => b.CourtId, quote.Court.Id)
                    .SetProperty(b => b.StartAt, quote.StartAt)
                    .SetProperty(b => b.EndAt, quote.EndAt)
                    .SetProperty(b => b.RescheduledAt, now)
                    .SetProperty(b => b.UpdatedAt, now), ct);
        }
        catch (Exception ex) when (IsSlotContention(ex))
        {
            throw new ApiException(409, "slot_taken", "Someone has just taken that time. Please choose another.");
        }
        if (moved == 0) throw AlreadyRescheduled();

        log.LogInformation("Booking {Code} rescheduled by its customer to {Court} {Start}", booking.Code, quote.Court.Name, quote.StartAt);
        var wasCourt = await db.Courts.AsNoTracking().Where(c => c.Id == booking.CourtId).Select(c => c.Name).FirstAsync(ct);
        await mailer.RescheduledAsync(bookingId, new Placement(wasCourt, booking.StartAt, booking.EndAt), ct);
        return await db.Bookings.AsNoTracking().FirstAsync(b => b.Id == bookingId, ct);
    }

    private static ApiException AlreadyRescheduled() =>
        new(409, "already_rescheduled", "This booking has already been moved once and cannot be moved again.");

    // ---------------------------------------------------------------- payment

    /// <summary>
    /// Called from the payment webhook; the only place an online Booking becomes confirmed. Idempotent.
    /// When the money cannot buy the Booking any more (the Hold ran out and the Court was taken or blocked, the time has
    /// passed, or it was already paid) the payment is flagged <c>refund_due</c> for the Admin to refund by hand.
    /// Returns the Booking when this call confirmed it, else null.
    /// </summary>
    public async Task<Booking?> ConfirmPaymentAsync(string provider, string providerRef, bool succeeded, string rawBody, CancellationToken ct)
    {
        // Row locks make this safe against a repeated webhook and against two payments of one Booking landing together.
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var payment = await db.Payments
            .FromSqlInterpolated($"SELECT * FROM payments WHERE provider = {provider} AND provider_ref = {providerRef} FOR UPDATE")
            .FirstOrDefaultAsync(ct) ?? throw new ApiException(404, "payment_not_found", "Payment not found.");
        if (payment.Status != PaymentStatus.Pending) return null;

        var now = Now;
        payment.RawWebhook = rawBody;
        if (!succeeded)
        {
            payment.Status = PaymentStatus.Failed;
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return null;
        }
        payment.Status = PaymentStatus.Succeeded;
        payment.PaidAt = now;

        var booking = await db.Bookings.FromSqlInterpolated($"SELECT * FROM bookings WHERE id = {payment.BookingId} FOR UPDATE").FirstAsync(ct);
        string? refundReason =
            booking.Status is not (BookingStatus.Held or BookingStatus.Expired) ? "already_" + booking.Status
            : booking.StartAt <= now ? "time_passed"
            // An expired Hold can still be rescued if nobody took the Court in the meantime.
            : booking.Status == BookingStatus.Expired && await db.CourtBlocks.AnyAsync(b => b.CourtId == booking.CourtId && b.StartAt < booking.EndAt && b.EndAt > booking.StartAt, ct) ? "court_blocked"
            : null;

        if (refundReason is null)
        {
            var (wasStatus, wasHold, wasUpdated) = (booking.Status, booking.HoldExpiresAt, booking.UpdatedAt);
            booking.Status = BookingStatus.Confirmed;
            booking.HoldExpiresAt = null;
            booking.UpdatedAt = now;
            try
            {
                await db.SaveChangesAsync(ct); // inside the transaction EF wraps this in a savepoint, so a failure leaves the transaction usable
                await tx.CommitAsync(ct);
                log.LogInformation("Booking {Code} confirmed by payment {Ref}", booking.Code, providerRef);
                await mailer.ConfirmedAsync(booking.Id, ct);
                return booking;
            }
            catch (Exception ex) when (IsSlotContention(ex))
            {
                // The exclusion constraint says the Court went to someone else after the Hold ran out.
                (booking.Status, booking.HoldExpiresAt, booking.UpdatedAt) = (wasStatus, wasHold, wasUpdated);
                refundReason = "court_taken";
            }
        }

        payment.RefundDue = true;
        payment.RefundReason = refundReason;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        log.LogWarning("Payment {Ref} for booking {Booking} needs a manual refund: {Reason}", providerRef, payment.BookingId, refundReason);
        return null;
    }

    // ---------------------------------------------------------------- helpers

    private Task<Booking?> FindByKeyAsync(Guid userId, string key, CancellationToken ct) =>
        db.Bookings.AsNoTracking().FirstOrDefaultAsync(b => b.UserId == userId && b.IdempotencyKey == key, ct);

    /// <summary>The Postgres error behind a failed SaveChanges. Npgsql's execution strategy wraps transient errors (deadlock) in an
    /// InvalidOperationException, so the whole exception chain is searched.</summary>
    public static PostgresException? PgError(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException!)
            if (e is PostgresException pg) return pg;
        return null;
    }

    /// <summary>
    /// Another Booking holds (or is concurrently taking) an overlapping time on the Court: the EXCLUDE constraint fired, or Postgres
    /// picked us as the victim when two conflicting writes deadlocked on it (which the constraint can cause under a race).
    /// </summary>
    public static bool IsSlotContention(Exception ex) =>
        PgError(ex) is { SqlState: PostgresErrorCodes.ExclusionViolation or PostgresErrorCodes.DeadlockDetected };
}
