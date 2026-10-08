import type { AdminBooking, BookingStatus } from '@pbc/api';
import { formatDayMonth, formatHours, formatWeekday } from '@pbc/ui';

export const STATUS_LABEL: Record<BookingStatus, string> = {
  held: 'Waiting for payment',
  confirmed: 'Confirmed',
  checked_in: 'Checked in',
  completed: 'Completed',
  expired: 'Not paid in time',
  cancelled: 'Cancelled',
  no_show: 'No-show',
};

/** Statuses that take up the Court on the schedule grid. */
export const ON_COURT: BookingStatus[] = ['held', 'confirmed', 'checked_in', 'completed'];

/** `Sat 24 Oct · 18:00–20:00 (2 hours)` */
export const whenOf = (b: Pick<AdminBooking, 'date' | 'start' | 'end' | 'hours'>) =>
  `${formatWeekday(b.date)} ${formatDayMonth(b.date)} · ${b.start}–${b.end} (${formatHours(b.hours)})`;

export const PAYMENT_LABEL: Record<string, string> = {
  promptpay: 'PromptPay (online)',
  card: 'Card (online)',
  cash: 'Cash at the counter',
  transfer: 'Transfer at the counter',
};
