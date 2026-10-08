import type { Booking, BookingStatus } from '@pbc/api';
import { formatDayMonth, formatHours, formatWeekday } from '@pbc/ui';

export const STATUS_LABEL: Record<BookingStatus, string> = {
  held: 'Waiting for payment',
  confirmed: 'Confirmed',
  checked_in: 'Checked in',
  completed: 'Completed',
  expired: 'Not paid in time',
  cancelled: 'Cancelled by the club',
  no_show: 'No-show',
};

/** `Sat 24 Oct · 18:00–20:00 (2 hours)` */
export const whenOf = (b: Pick<Booking, 'date' | 'start' | 'end' | 'hours'>) =>
  `${formatWeekday(b.date)} ${formatDayMonth(b.date)} · ${b.start}–${b.end} (${formatHours(b.hours)})`;
