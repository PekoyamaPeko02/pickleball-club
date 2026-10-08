// The API contract. Names follow CONTEXT.md; the .NET DTOs in backend/src/PickleballClub.Api must match field for field.

/** A date at the Club, `yyyy-MM-dd`. */
export type ClubDate = string;

export type DayType = 'weekday' | 'holiday';

/** `closed` = inside the Operating Hours but no Price Rule covers the Slot. */
export type SlotStatus = 'available' | 'booked' | 'held' | 'blocked' | 'past' | 'closed';

export interface Court {
  id: string;
  name: string;
  indoor: boolean;
}

/** One open day of the week (0 = Sunday … 6 = Saturday); a missing day is closed. Whole hours, `closeHour` 24 = midnight. */
export interface OperatingHours {
  dayOfWeek: number;
  openHour: number;
  closeHour: number;
}

export interface ClubInfo {
  name: string;
  timezone: string;
  /** Booking Window: a Customer may book `today` through `lastBookableDate`. */
  bookingWindowDays: number;
  /** A Reschedule must be made at least this many hours before play. */
  rescheduleNoticeHours: number;
  today: ClubDate;
  lastBookableDate: ClubDate;
  courts: Court[];
  operatingHours: OperatingHours[];
  /** True on a demo site: payments are simulated and nothing booked there is real. */
  demo: boolean;
}

/** A one-hour Slot of one Court. */
export interface Slot {
  /** Wall-clock time at the Club, `HH:mm`. */
  start: string;
  end: string;
  /** UTC instants (ISO 8601). */
  startAt: string;
  endAt: string;
  /** THB per hour; null when the Slot is `closed`. */
  price: number | null;
  label: string | null;
  status: SlotStatus;
}

export interface CourtAvailability {
  courtId: string;
  name: string;
  indoor: boolean;
  slots: Slot[];
}

export interface Availability {
  date: ClubDate;
  dayType: DayType;
  /** False when the date is outside the Booking Window, so a Customer cannot book it. */
  withinBookingWindow: boolean;
  courts: CourtAvailability[];
}

// ---------------------------------------------------------------- accounts

export type Role = 'customer' | 'admin';

export interface User {
  id: string;
  email: string;
  displayName: string | null;
  phone: string | null;
  role: Role;
  emailVerified: boolean;
  /** False for an account that so far only signs in with Google. */
  hasPassword: boolean;
  /** A Customer needs a name and a phone number before the first Booking. */
  profileComplete: boolean;
}

export interface AuthResponse {
  accessToken: string;
  /** UTC instant (ISO 8601). */
  expiresAt: string;
  user: User;
}

export interface AuthConfig {
  /** Null when "Sign in with Google" is switched off. */
  googleClientId: string | null;
}

export interface RegisterRequest {
  email: string;
  /** At least 10 characters. */
  password: string;
  displayName: string;
  phone?: string;
}

export interface UpdateProfileRequest {
  displayName: string;
  phone: string;
}

// ---------------------------------------------------------------- bookings

export type BookingStatus = 'held' | 'confirmed' | 'checked_in' | 'completed' | 'expired' | 'cancelled' | 'no_show';
export type BookingSource = 'online' | 'walk_in';
export type PaymentMethod = 'promptpay' | 'card';
export type PaymentStatus = 'pending' | 'succeeded' | 'failed';

export interface Payment {
  method: PaymentMethod;
  amount: number;
  status: PaymentStatus;
  /** PromptPay: the text to draw as a QR. Only while the Booking is held. */
  qrPayload: string | null;
  /** Card: the hosted payment page. Only while the Booking is held. */
  checkoutUrl: string | null;
  expiresAt: string | null;
}

/** One Court Rental: one Court, `hours` consecutive Slots. */
export interface Booking {
  id: string;
  /** What the Customer shows at the Club, e.g. `PB10001`. */
  code: string;
  status: BookingStatus;
  source: BookingSource;
  courtId: string;
  courtName: string;
  date: ClubDate;
  /** Wall-clock time at the Club, `HH:mm`. */
  start: string;
  end: string;
  hours: number;
  startAt: string;
  endAt: string;
  total: number;
  /** While `held`: when the Hold runs out (UTC, ISO 8601). */
  holdExpiresAt: string | null;
  createdAt: string;
  /** True once the Customer has used their one Reschedule. */
  rescheduled: boolean;
  /** The last moment the Customer may still Reschedule (UTC); null when they cannot (not confirmed, or already moved once). */
  rescheduleUntil: string | null;
  /** The newest payment attempt; null for a Walk-in Booking. */
  payment: Payment | null;
}

/** Where a Reschedule moves the Booking to; the number of Slots stays the same. */
export interface RescheduleRequest {
  courtId: string;
  date: ClubDate;
  startHour: number;
}

export interface CreateBookingRequest {
  courtId: string;
  date: ClubDate;
  /** Hour of the Club's day the first Slot starts (0–23). */
  startHour: number;
  /** Number of consecutive Slots. */
  hours: number;
  method: PaymentMethod;
}

// ---------------------------------------------------------------- back office (Admin)

export type AdminPaymentState = 'paid' | 'counter' | 'pending' | 'none';
export type CounterPayment = 'cash' | 'transfer';

/** A Booking as the Admin sees it: with who it belongs to. */
export interface AdminBooking {
  id: string;
  code: string;
  status: BookingStatus;
  source: BookingSource;
  courtId: string;
  courtName: string;
  date: ClubDate;
  start: string;
  end: string;
  hours: number;
  startAt: string;
  endAt: string;
  total: number;
  /** The Customer's name, or the Walk-in Guest's. */
  customerName: string;
  customerPhone: string | null;
  /** Null for a Walk-in Guest. */
  customerEmail: string | null;
  /** `paid` online, `counter` for a Walk-in Booking, `pending` while held. */
  paymentState: AdminPaymentState;
  paymentMethod: PaymentMethod | CounterPayment | null;
  rescheduled: boolean;
  holdExpiresAt: string | null;
  checkedInAt: string | null;
  cancelledAt: string | null;
  /** Admin Cancellation: how the Club refunded outside the system. */
  refundNote: string | null;
  createdAt: string;
}

/** Court Block: a period a Court is out of sale. */
export interface CourtBlock {
  id: string;
  courtId: string;
  courtName: string;
  date: ClubDate;
  start: string;
  end: string;
  startAt: string;
  endAt: string;
  reason: string;
}

/** One day at the Club: the public grid plus who has which Court and which Courts are blocked. */
export interface Schedule {
  availability: Availability;
  /** Every Booking touching the day except Holds that ran out, by start time. */
  bookings: AdminBooking[];
  blocks: CourtBlock[];
}

export interface WalkInRequest {
  courtId: string;
  date: ClubDate;
  startHour: number;
  hours: number;
  guestName: string;
  guestPhone: string;
  payment: CounterPayment;
}

export interface CourtBlockRequest {
  courtId: string;
  date: ClubDate;
  startHour: number;
  hours: number;
  reason: string;
}

/** A payment whose money arrived but bought nothing: the Club must refund it by hand. */
export interface RefundDue {
  paymentId: string;
  bookingId: string;
  bookingCode: string;
  customerName: string;
  customerPhone: string | null;
  customerEmail: string | null;
  method: PaymentMethod;
  amount: number;
  /** `court_taken`, `court_blocked`, `time_passed` or `already_<status>`. */
  reason: string | null;
  paidAt: string | null;
  refundedAt: string | null;
  refundNote: string | null;
}

// ---------------------------------------------------------------- settings (Admin)

export interface ClubSettings {
  name: string;
  timezone: string;
  bookingWindowDays: number;
  rescheduleNoticeHours: number;
}

export type ClubSettingsRequest = Omit<ClubSettings, 'timezone'>;

export interface CourtSettings {
  id: string;
  name: string;
  indoor: boolean;
  sortOrder: number;
  /** A switched-off Court is hidden from the public site and cannot be booked. */
  isActive: boolean;
}

export type CourtRequest = Omit<CourtSettings, 'id'>;

/** Price Rule: THB per hour for Slots starting in [startHour, endHour) on one Day Type. */
export interface PriceRule {
  id: string;
  /** Null = every Court. A rule for one Court beats a Club-wide rule. */
  courtId: string | null;
  dayType: DayType;
  startHour: number;
  /** 24 = midnight. */
  endHour: number;
  pricePerHour: number;
  label: string | null;
}

export type PriceRuleRequest = Omit<PriceRule, 'id'>;

/** A day the Admin counts as a holiday on top of Saturdays and Sundays. */
export interface Holiday {
  date: ClubDate;
  note: string | null;
}

export interface Settings {
  club: ClubSettings;
  /** Every Court, including switched-off ones. */
  courts: CourtSettings[];
  operatingHours: OperatingHours[];
  priceRules: PriceRule[];
  holidays: Holiday[];
}

// ---------------------------------------------------------------- reports (Admin)

/** One day of play. Money counts once paid and while the Club keeps it (cancelled Bookings and Holds do not count). */
export interface RevenueDay {
  date: ClubDate;
  bookings: number;
  /** Court-hours sold. */
  hours: number;
  /** THB from online Bookings. */
  online: number;
  /** THB taken at the counter for Walk-in Bookings. */
  walkIn: number;
  total: number;
  /** Court-hours on offer: Operating Hours × Courts open for booking (as set up today). */
  availableHours: number;
}

export interface RevenueReport {
  from: ClubDate;
  to: ClubDate;
  /** Every day of the range, including days with nothing sold. */
  days: RevenueDay[];
}
