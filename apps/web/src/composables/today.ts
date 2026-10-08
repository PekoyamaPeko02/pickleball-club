import { computed, onMounted, ref } from 'vue';
import type { Availability, ClubInfo, CourtAvailability } from '@pbc/api';
import { WEEKDAY_NAMES, formatBaht, formatHour } from '@pbc/ui';
import { useApi } from 'boot/api';
import { useClubStore } from 'stores/club';

/** What the public pages say about one Court today. */
export interface CourtToday {
  courtId: string;
  name: string;
  indoor: boolean;
  /** False when nothing is left to book today. */
  free: boolean;
  /** `Free from 18:00`, `Full today` or `Closed today`. */
  status: string;
  /** The start of the next free hour today (`18:00`), when there is one. */
  from: string | null;
}

function describe(court: CourtAvailability): CourtToday {
  const next = court.slots.find((s) => s.status === 'available');
  const status = next ? `Free from ${next.start}` : court.slots.length === 0 ? 'Closed today' : 'Full today';
  return { courtId: court.courtId, name: court.name, indoor: court.indoor, free: !!next, status, from: next?.start ?? null };
}

/** `Every day 06:00–23:00`, or one line per run of days that share the same hours. */
export function hoursLines(club: ClubInfo): { days: string; hours: string }[] {
  const order = [1, 2, 3, 4, 5, 6, 0]; // Monday first
  const text = (day: number) => {
    const h = club.operatingHours.find((x) => x.dayOfWeek === day);
    return h ? `${formatHour(h.openHour)}–${formatHour(h.closeHour)}` : 'Closed';
  };
  const lines: { from: number; to: number; hours: string }[] = [];
  for (const day of order) {
    const last = lines[lines.length - 1];
    if (last && last.hours === text(day)) last.to = day;
    else lines.push({ from: day, to: day, hours: text(day) });
  }
  if (lines.length === 1) return [{ days: 'Every day', hours: lines[0]!.hours }];
  return lines.map((l) => ({ days: l.from === l.to ? WEEKDAY_NAMES[l.from]! : `${WEEKDAY_NAMES[l.from]}–${WEEKDAY_NAMES[l.to]}`, hours: l.hours }));
}

/** The Club and today's Courts, for the home and courts pages. Works without today's data (the Courts just show no status). */
export function useToday() {
  const store = useClubStore();
  const club = computed(() => store.club);
  const availability = ref<Availability | null>(null);

  const courts = computed<CourtToday[]>(() => {
    if (availability.value) return availability.value.courts.map(describe);
    return (club.value?.courts ?? []).map((c) => ({ courtId: c.id, name: c.name, indoor: c.indoor, free: true, status: '', from: null }));
  });

  /** `฿300–฿450 an hour today`, from the hours still on sale. */
  const priceToday = computed(() => {
    const prices = (availability.value?.courts ?? []).flatMap((c) => c.slots).map((s) => s.price).filter((p): p is number => p !== null);
    if (prices.length === 0) return '';
    const [min, max] = [Math.min(...prices), Math.max(...prices)];
    return min === max ? `${formatBaht(min)} an hour` : `${formatBaht(min)}–${formatBaht(max)} an hour`;
  });

  onMounted(async () => {
    const info = await store.load();
    if (!info) return;
    try {
      availability.value = await useApi().getAvailability(info.today);
    } catch {
      /* the pages still work without today's status */
    }
  });

  return { club, courts, priceToday, availability };
}
