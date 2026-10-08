// In-browser mock of the API, so the public pages run without the backend (`pnpm dev:web`).
// Only the public, read-only calls are mocked; everything that needs an account answers 501 `demo_mode`.
// PLACEHOLDER DATA: mirrors db/init/002_seed.sql — keep the two in step.
import { ApiError, type PbcApi } from './client';
import type { Availability, ClubDate, ClubInfo, Court, DayType, Slot, SlotStatus } from './types';

const TIMEZONE = 'Asia/Bangkok';
const UTC_OFFSET_HOURS = 7; // Asia/Bangkok has no daylight saving
const BOOKING_WINDOW_DAYS = 14;
const OPEN_HOUR = 6;
const CLOSE_HOUR = 23;

const COURTS: Court[] = [
  { id: 'c0000000-0000-0000-0000-000000000001', name: 'Court 1', indoor: true },
  { id: 'c0000000-0000-0000-0000-000000000002', name: 'Court 2', indoor: true },
  { id: 'c0000000-0000-0000-0000-000000000003', name: 'Court 3', indoor: false },
  { id: 'c0000000-0000-0000-0000-000000000004', name: 'Court 4', indoor: false },
];

/** Today's date at the Club (`yyyy-MM-dd`), whatever the browser's own timezone is. */
export function clubToday(now: Date = new Date()): ClubDate {
  return new Intl.DateTimeFormat('en-CA', { timeZone: TIMEZONE }).format(now);
}

function addDays(date: ClubDate, days: number): ClubDate {
  const d = new Date(`${date}T00:00:00Z`);
  d.setUTCDate(d.getUTCDate() + days);
  return d.toISOString().slice(0, 10);
}

const label = (hour: number) => `${String(hour % 24).padStart(2, '0')}:00`;

/** UTC instant of `hour`:00 on the Club's local `date`. */
const toUtc = (date: ClubDate, hour: number) => new Date(Date.parse(`${date}T00:00:00Z`) + (hour - UTC_OFFSET_HOURS) * 3_600_000);

function dayTypeOf(date: ClubDate): DayType {
  const dow = new Date(`${date}T00:00:00Z`).getUTCDay();
  return dow === 0 || dow === 6 ? 'holiday' : 'weekday';
}

function priceFor(court: Court, dayType: DayType, hour: number): { price: number; label: string } {
  if (dayType === 'holiday') return { price: 400, label: 'Weekend & holiday' };
  if (hour < 16) return { price: 300, label: 'Off-peak' };
  return court.indoor ? { price: 450, label: 'Indoor peak' } : { price: 400, label: 'Peak' };
}

/** Stable pseudo-random "someone booked this" so the grid looks lived-in and does not change on reload. */
function isTaken(date: ClubDate, courtId: string, hour: number): boolean {
  let h = 2166136261;
  for (const ch of `${date}|${courtId}|${hour}`) h = Math.imul(h ^ ch.charCodeAt(0), 16777619);
  const busy = hour >= 17 && hour < 21 ? 0.55 : 0.2;
  return ((h >>> 0) % 1000) / 1000 < busy;
}

export function createMockApi(): PbcApi {
  const delay = <T>(value: T) => new Promise<T>((resolve) => setTimeout(() => resolve(value), 150));
  const demo = () => Promise.reject(new ApiError(501, 'demo_mode', 'This is not available in demo mode. Start the server to try it.'));

  return {
    getAuthConfig: () => delay({ googleClientId: null }),
    register: demo,
    login: demo,
    adminLogin: demo,
    googleLogin: demo,
    me: demo,
    updateProfile: demo,
    changePassword: demo,
    forgotPassword: demo,
    resetPassword: demo,
    verifyEmail: demo,
    resendVerification: demo,
    createBooking: demo,
    myBookings: demo,
    getBooking: demo,
    releaseHold: demo,
    newPayment: demo,
    reschedule: demo,
    devSimulatePayment: demo,
    getSchedule: demo,
    getAdminBooking: demo,
    getAdminBookingByCode: demo,
    createWalkIn: demo,
    adminMove: demo,
    adminCancel: demo,
    checkIn: demo,
    markNoShow: demo,
    createBlock: demo,
    deleteBlock: demo,
    getRefunds: demo,
    markRefunded: demo,
    getSettings: demo,
    updateClubSettings: demo,
    createCourt: demo,
    updateCourt: demo,
    setOperatingHours: demo,
    createPriceRule: demo,
    updatePriceRule: demo,
    deletePriceRule: demo,
    saveHoliday: demo,
    deleteHoliday: demo,
    getRevenueReport: demo,
    getRevenueCsv: demo,
    getBookingsCsv: demo,

    getClub() {
      const today = clubToday();
      const club: ClubInfo = {
        name: 'Pickleball Club',
        timezone: TIMEZONE,
        bookingWindowDays: BOOKING_WINDOW_DAYS,
        rescheduleNoticeHours: 48,
        today,
        lastBookableDate: addDays(today, BOOKING_WINDOW_DAYS),
        courts: COURTS,
        operatingHours: [0, 1, 2, 3, 4, 5, 6].map((dayOfWeek) => ({ dayOfWeek, openHour: OPEN_HOUR, closeHour: CLOSE_HOUR })),
        demo: false,
      };
      return delay(club);
    },

    getAvailability(date) {
      const today = clubToday();
      const day = date ?? today;
      const dayType = dayTypeOf(day);
      const now = Date.now();
      const result: Availability = {
        date: day,
        dayType,
        withinBookingWindow: day >= today && day <= addDays(today, BOOKING_WINDOW_DAYS),
        courts: COURTS.map((court) => {
          const slots: Slot[] = [];
          for (let hour = OPEN_HOUR; hour < CLOSE_HOUR; hour++) {
            const startAt = toUtc(day, hour);
            const status: SlotStatus = startAt.getTime() <= now ? 'past' : isTaken(day, court.id, hour) ? 'booked' : 'available';
            slots.push({
              start: label(hour),
              end: label(hour + 1),
              startAt: startAt.toISOString(),
              endAt: toUtc(day, hour + 1).toISOString(),
              ...priceFor(court, dayType, hour),
              status,
            });
          }
          return { courtId: court.id, name: court.name, indoor: court.indoor, slots };
        }),
      };
      return delay(result);
    },
  };
}
