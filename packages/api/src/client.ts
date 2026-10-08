import type {
  AdminBooking, AuthConfig, AuthResponse, Availability, Booking, ClubDate, ClubInfo, ClubSettings, ClubSettingsRequest, CourtBlock,
  CourtBlockRequest, CourtRequest, CourtSettings, CreateBookingRequest, Holiday, OperatingHours, PaymentMethod, PriceRule, PriceRuleRequest,
  RefundDue, RegisterRequest, RescheduleRequest, RevenueReport, Schedule, Settings, UpdateProfileRequest, User, WalkInRequest,
} from './types';

export class ApiError extends Error {
  /**
   * @param errors Only on `validation_failed`: messages per request field, e.g. `{ password: ['…'] }`.
   */
  constructor(public status: number, public code: string, message: string, public errors: Record<string, string[]> = {}) {
    super(message);
  }
}

export interface PbcApi {
  /** Public: the Club's Courts, Operating Hours and booking rules. */
  getClub(): Promise<ClubInfo>;
  /** Public: every Court's Slots on one day, with price and status. Date defaults to today at the Club. */
  getAvailability(date?: ClubDate): Promise<Availability>;

  // ---- accounts. Wrong credentials are ApiError 400 `invalid_credentials` (never 401, which means "session ended").
  /** Whether "Sign in with Google" is on. */
  getAuthConfig(): Promise<AuthConfig>;
  /** A new Customer, signed in straight away. 409 `email_taken`, 400 `bad_phone` / `validation_failed`. */
  register(req: RegisterRequest): Promise<AuthResponse>;
  /** Customer sign-in. Five wrong passwords lock the account for 15 minutes. */
  login(email: string, password: string): Promise<AuthResponse>;
  /** Back-office sign-in: Admin accounts only. */
  adminLogin(email: string, password: string): Promise<AuthResponse>;
  /** `credential` is the ID token from Google's sign-in button. 400 `google_not_configured` / `google_token_invalid` / `google_email_unverified`. */
  googleLogin(credential: string): Promise<AuthResponse>;
  me(): Promise<User>;
  updateProfile(req: UpdateProfileRequest): Promise<User>;
  /** Ends every other session; the response is a fresh session for this one. 400 `wrong_password` / `same_password` / `no_password`. */
  changePassword(currentPassword: string, newPassword: string): Promise<AuthResponse>;
  /** Always succeeds, whether or not the address has an account. */
  forgotPassword(email: string): Promise<void>;
  /** 400 `reset_token_invalid` when the emailed link is unknown, expired or already used. */
  resetPassword(token: string, newPassword: string): Promise<void>;
  /** 400 `verify_token_invalid`. */
  verifyEmail(token: string): Promise<void>;
  resendVerification(): Promise<void>;

  // ---- bookings (signed-in Customer)
  /**
   * Holds the Slots for 10 minutes and opens a payment. Pass the same `idempotencyKey` when retrying so it cannot book twice.
   * 409 `slot_taken` / `slot_blocked` / `open_hold_exists` / `profile_incomplete`,
   * 400 `outside_booking_window` / `outside_hours` / `closed` / `slot_in_past` / `no_price`, 502 `payment_unavailable`.
   */
  createBooking(req: CreateBookingRequest, idempotencyKey: string): Promise<Booking>;
  /** Latest play time first; Holds that ran out are left out. */
  myBookings(): Promise<Booking[]>;
  getBooking(id: string): Promise<Booking>;
  /** Gives an unpaid Hold back. 409 `not_held`. */
  releaseHold(id: string): Promise<Booking>;
  /** Another way to pay for the same Hold; its deadline does not move. 409 `not_held`. */
  newPayment(id: string, method: PaymentMethod): Promise<Booking>;
  /**
   * Moves a confirmed Booking: once, at least the Club's notice hours before play, same number of Slots, any Court, within the
   * Booking Window, never to a dearer time. 409 `already_rescheduled` / `too_late_to_reschedule` / `not_reschedulable` /
   * `slot_taken` / `slot_blocked`, 400 `costs_more` / `same_time` / `outside_booking_window` / `outside_hours`.
   */
  reschedule(id: string, req: RescheduleRequest): Promise<Booking>;
  /** Development servers only: pretend the Customer paid. */
  devSimulatePayment(id: string): Promise<Booking>;

  // ---- back office (signed-in Admin). None of it is bound by the Booking Window or the Reschedule rules.
  /** One day: the grid, every Booking that touches it (with who it belongs to) and the Court Blocks. Date defaults to today. */
  getSchedule(date?: ClubDate): Promise<Schedule>;
  getAdminBooking(id: string): Promise<AdminBooking>;
  /** By the code the customer shows (case-insensitive). 404 `booking_not_found`. */
  getAdminBookingByCode(code: string): Promise<AdminBooking>;
  /** Walk-in Booking for a Walk-in Guest: confirmed at once. 409 `slot_taken` / `slot_blocked`, 400 `bad_phone` / `outside_hours`. */
  createWalkIn(req: WalkInRequest): Promise<AdminBooking>;
  /** Admin Move: same number of Slots, any free time; does not use up the Customer's Reschedule. 409 `slot_taken` / `slot_blocked` / `not_movable`. */
  adminMove(id: string, req: RescheduleRequest): Promise<AdminBooking>;
  /** Admin Cancellation; `refundNote` says how the Club refunded outside the system. 409 `not_cancellable`. */
  adminCancel(id: string, refundNote: string): Promise<AdminBooking>;
  /** 409 `too_early` (opens 60 min before) / `too_late` / `not_checkin_able`. Idempotent. */
  checkIn(id: string): Promise<AdminBooking>;
  /** 409 `too_early` (15 min after the start) / `not_no_show_able`. Frees the Court. */
  markNoShow(id: string): Promise<AdminBooking>;
  /** 409 `block_conflict` when a live Booking overlaps, `block_overlap` when another block does. */
  createBlock(req: CourtBlockRequest): Promise<CourtBlock>;
  deleteBlock(id: string): Promise<void>;
  /** Payments waiting for a manual refund; `all` also lists the ones already refunded. */
  getRefunds(all?: boolean): Promise<RefundDue[]>;
  markRefunded(paymentId: string, note: string): Promise<void>;

  // ---- settings (signed-in Admin)
  getSettings(): Promise<Settings>;
  updateClubSettings(req: ClubSettingsRequest): Promise<ClubSettings>;
  /** 409 `court_name_taken`. */
  createCourt(req: CourtRequest): Promise<CourtSettings>;
  /** Switching a Court off with Bookings ahead is 409 `court_has_bookings`. */
  updateCourt(id: string, req: CourtRequest): Promise<CourtSettings>;
  /** Replaces the whole week; a day that is left out is closed. 400 `bad_hours` / `duplicate_day`. */
  setOperatingHours(days: OperatingHours[]): Promise<OperatingHours[]>;
  /** 409 `price_rule_overlap` when it shares an hour with a rule of the same Court scope and Day Type. */
  createPriceRule(req: PriceRuleRequest): Promise<PriceRule>;
  updatePriceRule(id: string, req: PriceRuleRequest): Promise<PriceRule>;
  deletePriceRule(id: string): Promise<void>;
  /** Adds the day, or changes its note when it is already a holiday. */
  saveHoliday(holiday: Holiday): Promise<Holiday>;
  deleteHoliday(date: ClubDate): Promise<void>;

  // ---- reports (signed-in Admin). Dates are inclusive; at most 366 days. 400 `bad_range` / `range_too_long`.
  /** Revenue by the day of play. Defaults to the last 30 days. */
  getRevenueReport(from?: ClubDate, to?: ClubDate): Promise<RevenueReport>;
  /** The same report as CSV text (one row per day). */
  getRevenueCsv(from: ClubDate, to: ClubDate): Promise<string>;
  /** CSV text with one row per Booking that earned money in the range. */
  getBookingsCsv(from: ClubDate, to: ClubDate): Promise<string>;
}

export interface HttpApiOptions {
  /** e.g. `http://localhost:5090/api/v1` */
  baseUrl: string;
  getToken?: () => string | null | undefined;
  /** Called on a 401: the session is missing, expired or revoked. */
  onUnauthorized?: () => void;
}

export function createHttpApi(opts: HttpApiOptions): PbcApi {
  async function request<T>(method: string, path: string, body?: unknown, extraHeaders: Record<string, string> = {}): Promise<T> {
    const headers: Record<string, string> = { Accept: 'application/json', ...extraHeaders };
    const token = opts.getToken?.();
    if (token) headers.Authorization = `Bearer ${token}`;
    if (body !== undefined) headers['Content-Type'] = 'application/json';

    // The apps compile with exactOptionalPropertyTypes: leave `body` out rather than passing undefined.
    const init: RequestInit = { method, headers };
    if (body !== undefined) init.body = JSON.stringify(body);

    let res: Response;
    try {
      res = await fetch(opts.baseUrl + path, init);
    } catch {
      throw new ApiError(0, 'network_error', 'Cannot reach the server. Check your connection and try again.');
    }

    if (res.status === 401) opts.onUnauthorized?.();
    if (!res.ok) {
      // Every API error is JSON { code, message }.
      const err = (await res.json().catch(() => null)) as { code?: string; message?: string; errors?: Record<string, string[]> } | null;
      throw new ApiError(res.status, err?.code ?? 'error', err?.message ?? `Request failed (${res.status}).`, err?.errors ?? {});
    }
    return res.status === 204 ? (undefined as T) : ((await res.json()) as T);
  }

  /** For the CSV downloads: the body as text instead of JSON. */
  async function requestText(path: string): Promise<string> {
    const headers: Record<string, string> = {};
    const token = opts.getToken?.();
    if (token) headers.Authorization = `Bearer ${token}`;
    let res: Response;
    try {
      res = await fetch(opts.baseUrl + path, { headers });
    } catch {
      throw new ApiError(0, 'network_error', 'Cannot reach the server. Check your connection and try again.');
    }
    if (res.status === 401) opts.onUnauthorized?.();
    if (!res.ok) {
      const err = (await res.json().catch(() => null)) as { code?: string; message?: string } | null;
      throw new ApiError(res.status, err?.code ?? 'error', err?.message ?? `Request failed (${res.status}).`);
    }
    return res.text();
  }

  const range = (from?: ClubDate, to?: ClubDate) => {
    const q = new URLSearchParams();
    if (from) q.set('from', from);
    if (to) q.set('to', to);
    const s = q.toString();
    return s ? `?${s}` : '';
  };

  return {
    getClub: () => request('GET', '/club'),
    getAvailability: (date) => request('GET', date ? `/availability?date=${encodeURIComponent(date)}` : '/availability'),

    getAuthConfig: () => request('GET', '/auth/config'),
    register: (req) => request('POST', '/auth/register', req),
    login: (email, password) => request('POST', '/auth/login', { email, password }),
    adminLogin: (email, password) => request('POST', '/auth/admin/login', { email, password }),
    googleLogin: (credential) => request('POST', '/auth/google', { credential }),
    me: () => request('GET', '/auth/me'),
    updateProfile: (req) => request('PUT', '/auth/me', req),
    changePassword: (currentPassword, newPassword) => request('POST', '/auth/password/change', { currentPassword, newPassword }),
    forgotPassword: (email) => request('POST', '/auth/password/forgot', { email }),
    resetPassword: (token, newPassword) => request('POST', '/auth/password/reset', { token, newPassword }),
    verifyEmail: (token) => request('POST', '/auth/email/verify', { token }),
    resendVerification: () => request('POST', '/auth/email/resend'),

    createBooking: (req, idempotencyKey) => request('POST', '/bookings', req, { 'Idempotency-Key': idempotencyKey }),
    myBookings: () => request('GET', '/me/bookings'),
    getBooking: (id) => request('GET', `/me/bookings/${id}`),
    releaseHold: (id) => request('POST', `/me/bookings/${id}/release`),
    newPayment: (id, method) => request('POST', `/me/bookings/${id}/payment`, { method }),
    reschedule: (id, req) => request('POST', `/me/bookings/${id}/reschedule`, req),
    devSimulatePayment: (id) => request('POST', `/dev/bookings/${id}/simulate-payment`),

    getSchedule: (date) => request('GET', date ? `/admin/schedule?date=${encodeURIComponent(date)}` : '/admin/schedule'),
    getAdminBooking: (id) => request('GET', `/admin/bookings/${id}`),
    getAdminBookingByCode: (code) => request('GET', `/admin/bookings/by-code/${encodeURIComponent(code)}`),
    createWalkIn: (req) => request('POST', '/admin/bookings/walk-in', req),
    adminMove: (id, req) => request('POST', `/admin/bookings/${id}/move`, req),
    adminCancel: (id, refundNote) => request('POST', `/admin/bookings/${id}/cancel`, { refundNote }),
    checkIn: (id) => request('POST', `/admin/bookings/${id}/check-in`),
    markNoShow: (id) => request('POST', `/admin/bookings/${id}/no-show`),
    createBlock: (req) => request('POST', '/admin/blocks', req),
    deleteBlock: (id) => request('DELETE', `/admin/blocks/${id}`),
    getRefunds: (all) => request('GET', all ? '/admin/refunds?all=true' : '/admin/refunds'),
    markRefunded: (paymentId, note) => request('POST', `/admin/refunds/${paymentId}/refunded`, { note }),

    getSettings: () => request('GET', '/admin/settings'),
    updateClubSettings: (req) => request('PUT', '/admin/settings/club', req),
    createCourt: (req) => request('POST', '/admin/courts', req),
    updateCourt: (id, req) => request('PUT', `/admin/courts/${id}`, req),
    setOperatingHours: (days) => request('PUT', '/admin/hours', { days }),
    createPriceRule: (req) => request('POST', '/admin/price-rules', req),
    updatePriceRule: (id, req) => request('PUT', `/admin/price-rules/${id}`, req),
    deletePriceRule: (id) => request('DELETE', `/admin/price-rules/${id}`),
    saveHoliday: (holiday) => request('PUT', '/admin/holidays', holiday),
    deleteHoliday: (date) => request('DELETE', `/admin/holidays/${date}`),

    getRevenueReport: (from, to) => request('GET', `/admin/reports/revenue${range(from, to)}`),
    getRevenueCsv: (from, to) => requestText(`/admin/reports/revenue.csv${range(from, to)}`),
    getBookingsCsv: (from, to) => requestText(`/admin/reports/bookings.csv${range(from, to)}`),
  };
}
