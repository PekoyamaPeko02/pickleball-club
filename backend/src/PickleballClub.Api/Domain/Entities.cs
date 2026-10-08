namespace PickleballClub.Api.Domain;

// Names follow CONTEXT.md. Table and column names are the snake_case form (see db/init/001_schema.sql).

public static class Roles
{
    public const string Customer = "customer";
    public const string Admin = "admin";
}

public static class DayTypes
{
    public const string Weekday = "weekday";
    public const string Holiday = "holiday";
}

public static class BookingSource
{
    public const string Online = "online";
    public const string WalkIn = "walk_in";
}

public static class BookingStatus
{
    public const string Held = "held";
    public const string Confirmed = "confirmed";
    public const string CheckedIn = "checked_in";
    public const string Completed = "completed";
    public const string Expired = "expired";
    public const string Cancelled = "cancelled";
    public const string NoShow = "no_show";

    /// <summary>Statuses that occupy the Court. Must match the WHERE of the no_overlapping_bookings constraint.</summary>
    public static readonly string[] Live = [Held, Confirmed, CheckedIn];
}

/// <summary>The single settings row of the Club.</summary>
public class ClubSettings
{
    public bool Id { get; set; } = true;
    public string Name { get; set; } = "";
    public string Timezone { get; set; } = "Asia/Bangkok";
    public int BookingWindowDays { get; set; }
    public int RescheduleNoticeHours { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>A Customer or an Admin. A Walk-in Guest has no User.</summary>
public class User
{
    public Guid Id { get; set; }
    public string Email { get; set; } = "";
    public string? PasswordHash { get; set; }
    public string? GoogleSubject { get; set; }
    public string? DisplayName { get; set; }
    public string? Phone { get; set; }
    public string Role { get; set; } = Roles.Customer;
    public DateTime? EmailVerifiedAt { get; set; }
    public DateTime? PasswordChangedAt { get; set; }
    public int FailedLogins { get; set; }
    public DateTime? LockedUntil { get; set; }
    public DateTime CreatedAt { get; set; }
}

public static class TokenPurpose
{
    public const string ResetPassword = "reset_password";
    public const string VerifyEmail = "verify_email";
}

/// <summary>A one-time link sent by email. Only the hash of the token is kept.</summary>
public class UserToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Purpose { get; set; } = "";
    public string TokenHash { get; set; } = "";
    public DateTime ExpiresAt { get; set; }
    public DateTime? ConsumedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class Court
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public bool Indoor { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>Operating Hours of one day of the week, in whole hours of the Club's local day (CloseHour 24 = midnight).</summary>
public class OperatingHours
{
    public short DayOfWeek { get; set; }
    public short OpenHour { get; set; }
    public short CloseHour { get; set; }
}

public class Holiday
{
    public DateOnly HolidayDate { get; set; }
    public string? Note { get; set; }
}

/// <summary>Hourly price for Slots starting in [StartHour, EndHour) on one Day Type. CourtId null = every Court.</summary>
public class PriceRule
{
    public Guid Id { get; set; }
    public Guid? CourtId { get; set; }
    public string DayType { get; set; } = DayTypes.Weekday;
    public short StartHour { get; set; }
    public short EndHour { get; set; }
    public decimal PricePerHour { get; set; }
    public string? Label { get; set; }
}

/// <summary>One Court Rental: one Court, consecutive Slots from StartAt to EndAt (UTC).</summary>
public class Booking
{
    public Guid Id { get; set; }
    public string Code { get; set; } = "";
    public string Source { get; set; } = BookingSource.Online;
    public Guid? UserId { get; set; }
    public string? GuestName { get; set; }
    public string? GuestPhone { get; set; }
    public Guid CourtId { get; set; }
    public DateTime StartAt { get; set; }
    public DateTime EndAt { get; set; }
    public string Status { get; set; } = BookingStatus.Held;
    public decimal Total { get; set; }
    public DateTime? HoldExpiresAt { get; set; }
    public string? CounterPayment { get; set; }
    public DateTime? RescheduledAt { get; set; }
    public DateTime? CheckedInAt { get; set; }
    public DateTime? ReminderSentAt { get; set; }
    public DateTime? CancelledAt { get; set; }
    public Guid? CancelledBy { get; set; }
    public string? RefundNote { get; set; }
    public Guid? CreatedBy { get; set; }
    public string? IdempotencyKey { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class CourtBlock
{
    public Guid Id { get; set; }
    public Guid CourtId { get; set; }
    public DateTime StartAt { get; set; }
    public DateTime EndAt { get; set; }
    public string Reason { get; set; } = "";
    public Guid? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
}

public static class PaymentMethod
{
    public const string PromptPay = "promptpay";
    public const string Card = "card";
}

public static class PaymentStatus
{
    public const string Pending = "pending";
    public const string Succeeded = "succeeded";
    public const string Failed = "failed";
}

/// <summary>One attempt to pay for a Booking online.</summary>
public class Payment
{
    public Guid Id { get; set; }
    public Guid BookingId { get; set; }
    public string Provider { get; set; } = "";
    public string ProviderRef { get; set; } = "";
    public string Method { get; set; } = PaymentMethod.PromptPay;
    public decimal Amount { get; set; }
    public string Status { get; set; } = PaymentStatus.Pending;
    public string? QrPayload { get; set; }
    public string? CheckoutUrl { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime? PaidAt { get; set; }
    /// <summary>The money arrived but the Booking could not be confirmed: the Club must refund outside the system.</summary>
    public bool RefundDue { get; set; }
    public string? RefundReason { get; set; }
    public DateTime? RefundedAt { get; set; }
    public Guid? RefundedBy { get; set; }
    public string? RefundNote { get; set; }
    public string? RawWebhook { get; set; }
    public DateTime CreatedAt { get; set; }
}
