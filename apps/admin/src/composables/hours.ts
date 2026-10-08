import type { ClubDate, ClubInfo } from '@pbc/api';
import { formatHour } from '@pbc/ui';

/** The Operating Hours of that date's day of the week, or null when the Club is closed. */
export function hoursOn(club: ClubInfo | null, date: ClubDate) {
  if (!club || !date) return null;
  const dayOfWeek = new Date(`${date}T00:00:00Z`).getUTCDay();
  return club.operatingHours.find((h) => h.dayOfWeek === dayOfWeek) ?? null;
}

/** Select options for a start hour inside the Operating Hours of that date. */
export function startHourOptions(club: ClubInfo | null, date: ClubDate) {
  const h = hoursOn(club, date);
  if (!h) return [];
  return Array.from({ length: h.closeHour - h.openHour }, (_, i) => ({ label: formatHour(h.openHour + i), value: h.openHour + i }));
}

/** How many hours a booking starting at `startHour` can last before closing time. */
export function maxHoursFrom(club: ClubInfo | null, date: ClubDate, startHour: number) {
  const h = hoursOn(club, date);
  return h ? Math.max(1, h.closeHour - startHour) : 1;
}
