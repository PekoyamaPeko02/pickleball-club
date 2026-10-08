using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PickleballClub.Api.Data;
using PickleballClub.Api.Domain;
using PickleballClub.Api.Features.Auth;
using PickleballClub.Api.Features.Bookings;
using PickleballClub.Api.Services;

namespace PickleballClub.Api.Features.Admin;

public record WalkInRequest(
    [property: NotEmpty] Guid CourtId,
    DateOnly Date,
    [property: Range(0, 23)] int StartHour,
    [property: Range(1, 24)] int Hours,
    [property: NotEmpty, MaxLength(80)] string GuestName,
    [property: NotEmpty, MaxLength(30)] string GuestPhone,
    [property: Required, RegularExpression("^(cash|transfer)$")] string Payment);

public record CourtBlockRequest(
    [property: NotEmpty] Guid CourtId,
    DateOnly Date,
    [property: Range(0, 23)] int StartHour,
    [property: Range(1, 24)] int Hours,
    [property: NotEmpty, MaxLength(200)] string Reason);

/// <summary>Where an Admin Move puts the Booking; the number of Slots stays the same.</summary>
public record AdminMoveRequest([property: NotEmpty] Guid CourtId, DateOnly Date, [property: Range(0, 23)] int StartHour);

/// <param name="RefundNote">How the Club refunded the customer outside the system (e.g. "Transferred 900 THB to KBank ••1234").</param>
public record AdminCancelRequest([property: NotEmpty, MaxLength(500)] string RefundNote);

public record RefundedRequest([property: NotEmpty, MaxLength(500)] string Note);

public class AdminOptions
{
    /// <summary>Check-in opens this many minutes before the Booking starts and stays open until it ends.</summary>
    public int CheckInEarlyMinutes { get; set; } = 60;
    /// <summary>A Booking can be marked No-show once this many minutes have passed since it started.</summary>
    public int NoShowGraceMinutes { get; set; } = 15;
}

/// <summary>What only an Admin may do to Bookings and Courts. None of it is bound by the Booking Window or the Reschedule rules.</summary>
public class AdminBookingService(
    AppDbContext db, BookingService bookings, BookingMailer mailer, TimeProvider clock, IOptions<AdminOptions> options, ILogger<AdminBookingService> log)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    /// <summary>A Walk-in Booking for a Walk-in Guest: paid at the counter, so it is confirmed at once (no Hold, no online payment).</summary>
    public async Task<Booking> CreateWalkInAsync(Guid adminId, WalkInRequest req, CancellationToken ct)
    {
        var phone = AuthEndpoints.NormalizePhone(req.GuestPhone) ?? throw new ApiException(400, "bad_phone", "That phone number does not look right.");
        var quote = await bookings.QuoteAsync(req.CourtId, req.Date, req.StartHour, req.Hours, allowInProgress: true, ct);
        await bookings.EnsureNotBlockedAsync(quote.Court.Id, quote.StartAt, quote.EndAt, ct);

        var booking = new Booking
        {
            Id = Guid.NewGuid(), Source = BookingSource.WalkIn, GuestName = req.GuestName.Trim(), GuestPhone = phone,
            CourtId = quote.Court.Id, StartAt = quote.StartAt, EndAt = quote.EndAt, Status = BookingStatus.Confirmed,
            Total = quote.Total, CounterPayment = req.Payment, CreatedBy = adminId, CreatedAt = Now, UpdatedAt = Now,
        };
        db.Bookings.Add(booking);
        try { await db.SaveChangesAsync(ct); }
        catch (Exception ex) when (BookingService.IsSlotContention(ex))
        {
            throw new ApiException(409, "slot_taken", "That time has just been taken.");
        }
        await db.Entry(booking).ReloadAsync(ct);
        log.LogInformation("Walk-in {Code} entered by admin {Admin}, total {Total}", booking.Code, adminId, booking.Total);
        return booking;
    }

    /// <summary>Takes a Court out of sale. Refused while a live Booking overlaps: move or cancel it first.</summary>
    public async Task<CourtBlock> CreateBlockAsync(Guid adminId, CourtBlockRequest req, CancellationToken ct)
    {
        if (req.StartHour + req.Hours > 24) throw new ApiException(400, "outside_hours", "A block cannot run past midnight.");
        var club = await db.ClubSettings.AsNoTracking().SingleAsync(ct);
        if (!await db.Courts.AnyAsync(c => c.Id == req.CourtId, ct)) throw new ApiException(400, "court_not_found", "That court does not exist.");
        var startAt = ClubClock.ToUtc(req.Date, req.StartHour, club.Timezone);
        var endAt = ClubClock.ToUtc(req.Date, req.StartHour + req.Hours, club.Timezone);
        if (endAt <= Now) throw new ApiException(400, "slot_in_past", "That time has already passed.");

        if (await db.Bookings.AnyAsync(b => b.CourtId == req.CourtId && BookingStatus.Live.Contains(b.Status) && b.StartAt < endAt && b.EndAt > startAt, ct))
            throw new ApiException(409, "block_conflict", "There is a booking in that time. Move or cancel it first.");
        if (await db.CourtBlocks.AnyAsync(b => b.CourtId == req.CourtId && b.StartAt < endAt && b.EndAt > startAt, ct))
            throw new ApiException(409, "block_overlap", "That court is already blocked for part of that time.");

        var block = new CourtBlock { Id = Guid.NewGuid(), CourtId = req.CourtId, StartAt = startAt, EndAt = endAt, Reason = req.Reason.Trim(), CreatedBy = adminId };
        db.CourtBlocks.Add(block);
        await db.SaveChangesAsync(ct);
        return block;
    }

    public async Task DeleteBlockAsync(Guid blockId, CancellationToken ct)
    {
        if (await db.CourtBlocks.Where(b => b.Id == blockId).ExecuteDeleteAsync(ct) == 0)
            throw new ApiException(404, "block_not_found", "That block does not exist.");
    }

    /// <summary>
    /// Admin Move: puts a confirmed Booking on another Court or time, e.g. when the Club must close a Court. It does not use
    /// up the Customer's Reschedule and is not bound by price, notice or the Booking Window. Same number of Slots; nothing is
    /// charged or refunded.
    /// </summary>
    public async Task<Booking> MoveAsync(Guid adminId, Guid bookingId, AdminMoveRequest req, CancellationToken ct)
    {
        var booking = await db.Bookings.AsNoTracking().FirstOrDefaultAsync(b => b.Id == bookingId, ct) ?? throw NotFound();
        if (booking.Status != BookingStatus.Confirmed)
            throw new ApiException(409, "not_movable", "Only a confirmed booking can be moved.");

        var hours = (int)Math.Round((booking.EndAt - booking.StartAt).TotalHours);
        var quote = await bookings.QuoteAsync(req.CourtId, req.Date, req.StartHour, hours, allowInProgress: true, ct);
        if (quote.Court.Id == booking.CourtId && quote.StartAt == booking.StartAt)
            throw new ApiException(400, "same_time", "That is the time the booking already has.");
        await bookings.EnsureNotBlockedAsync(quote.Court.Id, quote.StartAt, quote.EndAt, ct);

        int moved;
        var now = Now;
        try
        {
            moved = await db.Bookings.Where(b => b.Id == bookingId && b.Status == BookingStatus.Confirmed)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(b => b.CourtId, quote.Court.Id)
                    .SetProperty(b => b.StartAt, quote.StartAt)
                    .SetProperty(b => b.EndAt, quote.EndAt)
                    .SetProperty(b => b.UpdatedAt, now), ct);
        }
        catch (Exception ex) when (BookingService.IsSlotContention(ex))
        {
            throw new ApiException(409, "slot_taken", "That time is already booked.");
        }
        if (moved == 0) throw new ApiException(409, "not_movable", "Only a confirmed booking can be moved.");
        log.LogInformation("Booking {Code} moved by admin {Admin} to {Court} {Start}", booking.Code, adminId, quote.Court.Name, quote.StartAt);
        var wasCourt = await db.Courts.AsNoTracking().Where(c => c.Id == booking.CourtId).Select(c => c.Name).FirstAsync(ct);
        await mailer.MovedByClubAsync(bookingId, new Placement(wasCourt, booking.StartAt, booking.EndAt), ct);
        return await db.Bookings.AsNoTracking().FirstAsync(b => b.Id == bookingId, ct);
    }

    /// <summary>
    /// Admin Cancellation: the only way a paid Booking is cancelled. The Club refunds outside the system and writes down how.
    /// </summary>
    public async Task<Booking> CancelAsync(Guid adminId, Guid bookingId, AdminCancelRequest req, CancellationToken ct)
    {
        var booking = await db.Bookings.FirstOrDefaultAsync(b => b.Id == bookingId, ct) ?? throw NotFound();
        if (booking.Status == BookingStatus.Cancelled) return booking;
        if (booking.Status is not (BookingStatus.Confirmed or BookingStatus.CheckedIn))
            throw new ApiException(409, "not_cancellable", "Only a confirmed booking can be cancelled.");

        var now = Now;
        booking.Status = BookingStatus.Cancelled;
        booking.CancelledAt = now;
        booking.CancelledBy = adminId;
        booking.RefundNote = req.RefundNote.Trim();
        booking.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        log.LogInformation("Booking {Code} cancelled by admin {Admin}", booking.Code, adminId);
        await mailer.CancelledByClubAsync(booking.Id, ct);
        return booking;
    }

    /// <summary>Check-in: the customer has arrived. Open from <see cref="AdminOptions.CheckInEarlyMinutes"/> before the start until the end. Idempotent.</summary>
    public async Task<Booking> CheckInAsync(Guid bookingId, CancellationToken ct)
    {
        var booking = await db.Bookings.FirstOrDefaultAsync(b => b.Id == bookingId, ct) ?? throw NotFound();
        if (booking.Status == BookingStatus.CheckedIn) return booking;
        if (booking.Status != BookingStatus.Confirmed)
            throw new ApiException(409, "not_checkin_able", "Only a confirmed booking can be checked in.");

        var now = Now;
        if (now < booking.StartAt.AddMinutes(-options.Value.CheckInEarlyMinutes))
            throw new ApiException(409, "too_early", $"Check-in opens {options.Value.CheckInEarlyMinutes} minutes before the booking starts.");
        if (now >= booking.EndAt) throw new ApiException(409, "too_late", "This booking has already ended.");

        booking.Status = BookingStatus.CheckedIn;
        booking.CheckedInAt = now;
        booking.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        return booking;
    }

    /// <summary>
    /// No-show: a confirmed Booking whose customer did not come. Marked by the Admin once the grace time has passed.
    /// The customer gets nothing back and the Court goes back on sale for the rest of the time. Idempotent.
    /// </summary>
    public async Task<Booking> MarkNoShowAsync(Guid bookingId, CancellationToken ct)
    {
        var booking = await db.Bookings.FirstOrDefaultAsync(b => b.Id == bookingId, ct) ?? throw NotFound();
        if (booking.Status == BookingStatus.NoShow) return booking;
        if (booking.Status != BookingStatus.Confirmed)
            throw new ApiException(409, "not_no_show_able", "Only a confirmed booking that has not been checked in can be marked as a no-show.");

        var now = Now;
        if (now < booking.StartAt.AddMinutes(options.Value.NoShowGraceMinutes))
            throw new ApiException(409, "too_early", $"A no-show can be marked {options.Value.NoShowGraceMinutes} minutes after the booking starts.");

        booking.Status = BookingStatus.NoShow;
        booking.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        return booking;
    }

    /// <summary>A checked-in Booking whose time is over becomes completed. Returns the number completed.</summary>
    public Task<int> CompleteFinishedAsync(CancellationToken ct)
    {
        var now = Now;
        return db.Bookings.Where(b => b.Status == BookingStatus.CheckedIn && b.EndAt <= now)
            .ExecuteUpdateAsync(s => s.SetProperty(b => b.Status, BookingStatus.Completed).SetProperty(b => b.UpdatedAt, now), ct);
    }

    /// <summary>The Admin records that a payment flagged <c>refund_due</c> has been refunded by hand.</summary>
    public async Task MarkRefundedAsync(Guid adminId, Guid paymentId, string note, CancellationToken ct)
    {
        var payment = await db.Payments.FirstOrDefaultAsync(p => p.Id == paymentId, ct)
                      ?? throw new ApiException(404, "payment_not_found", "That payment does not exist.");
        if (!payment.RefundDue) throw new ApiException(409, "no_refund_due", "This payment does not need a refund.");
        if (payment.RefundedAt is not null) return;
        payment.RefundedAt = Now;
        payment.RefundedBy = adminId;
        payment.RefundNote = note.Trim();
        await db.SaveChangesAsync(ct);
    }

    private static ApiException NotFound() => new(404, "booking_not_found", "That booking does not exist.");
}
