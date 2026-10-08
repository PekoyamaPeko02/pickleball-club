import type { ClubDate } from '@pbc/api';

/** `฿1,250` */
export const formatBaht = (amount: number) => `฿${amount.toLocaleString('en-US', { maximumFractionDigits: 2 })}`;

// A ClubDate is a calendar day, not an instant: do the arithmetic and formatting in UTC so the browser's timezone never shifts it.
const asUtc = (date: ClubDate) => new Date(`${date}T00:00:00Z`);

export function addDays(date: ClubDate, days: number): ClubDate {
  const d = asUtc(date);
  d.setUTCDate(d.getUTCDate() + days);
  return d.toISOString().slice(0, 10);
}

/** Every day from `from` to `to`, inclusive. */
export function dateRange(from: ClubDate, to: ClubDate): ClubDate[] {
  const days: ClubDate[] = [];
  for (let d = from; d <= to; d = addDays(d, 1)) days.push(d);
  return days;
}

const fmt = (options: Intl.DateTimeFormatOptions) => new Intl.DateTimeFormat('en-GB', { ...options, timeZone: 'UTC' });
const weekday = fmt({ weekday: 'short' });
const dayMonth = fmt({ day: 'numeric', month: 'short' });
const long = fmt({ weekday: 'long', day: 'numeric', month: 'long', year: 'numeric' });

/** `Sat` */
export const formatWeekday = (date: ClubDate) => weekday.format(asUtc(date));
/** `24 Oct` */
export const formatDayMonth = (date: ClubDate) => dayMonth.format(asUtc(date));
/** `Saturday 24 October 2026` */
export const formatLongDate = (date: ClubDate) => long.format(asUtc(date));

export const WEEKDAY_NAMES = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'] as const;

/** `06:00`; hour 24 reads `00:00`. */
export const formatHour = (hour: number) => `${String(hour % 24).padStart(2, '0')}:00`;

/** `18:00` → 18 */
export const hourOf = (time: string) => Number(time.slice(0, 2));

/** `2 hours` / `1 hour` */
export const formatHours = (hours: number) => `${hours} ${hours === 1 ? 'hour' : 'hours'}`;

/** A UTC instant as wall-clock time at the Club: `Sat 24 Oct, 18:00`. */
export const formatClubDateTime = (iso: string, timezone: string) =>
  new Intl.DateTimeFormat('en-GB', { timeZone: timezone, weekday: 'short', day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit', hour12: false })
    .format(new Date(iso));
