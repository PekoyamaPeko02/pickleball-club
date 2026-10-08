<template>
  <q-page class="pbc-page move">
    <h1 class="pbc-h2 page-title">Move your booking</h1>

    <q-banner v-if="error" class="text-negative" role="alert">{{ error }}</q-banner>
    <q-skeleton v-else-if="!booking || !club" height="420px" />

    <template v-else-if="!canMove">
      <p class="pbc-muted">This booking cannot be moved any more.</p>
      <q-btn unelevated no-caps class="pbc-btn-ink" :to="{ name: 'booking', params: { id: booking.id } }" label="Back to the booking" />
    </template>

    <template v-else>
      <p class="pbc-muted">
        Now: <strong>{{ booking.courtName }} · {{ whenOf(booking) }}</strong>, paid {{ formatBaht(booking.total) }}.
        Tap a new start time. It must be {{ formatHours(booking.hours) }} long and cost {{ formatBaht(booking.total) }} or less.
        You can move a booking only once.
      </p>

      <div class="days" role="tablist" aria-label="Choose a day">
        <button
          v-for="day in days" :key="day" type="button" role="tab" class="day" :class="{ 'day--active': day === date }"
          :aria-selected="day === date" @click="date = day"
        >
          <span class="day__dow">{{ day === club.today ? 'Today' : formatWeekday(day) }}</span>
          <span class="day__date pbc-num">{{ formatDayMonth(day) }}</span>
        </button>
      </div>

      <q-skeleton v-if="!availability" height="420px" class="q-mt-md" />
      <template v-else>
        <p class="q-mt-md q-mb-sm"><strong>{{ formatLongDate(availability.date) }}</strong></p>
        <AvailabilityGrid :availability="availability" selectable :selected="selectedKeys" @select="choose" />
      </template>

      <p v-if="hint" class="text-negative q-mt-md" role="alert">{{ hint }}</p>

      <div v-if="target" class="bar" role="region" aria-label="New time">
        <div class="bar__inner">
          <div>
            <div class="pbc-label pbc-muted">Move to</div>
            <div class="bar__what">{{ target.courtName }} · {{ whenOf(target) }}</div>
          </div>
          <q-btn unelevated no-caps size="lg" class="pbc-btn-brass" label="Confirm the move" :loading="busy" @click="confirm" />
        </div>
      </div>
    </template>
  </q-page>
</template>

<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import { useQuasar } from 'quasar';
import { ApiError, type Availability, type Booking, type ClubDate, type Slot } from '@pbc/api';
import { AvailabilityGrid, dateRange, formatBaht, formatDayMonth, formatHour, formatHours, formatLongDate, formatWeekday, hourOf } from '@pbc/ui';
import { useApi } from 'boot/api';
import { whenOf } from 'src/composables/bookings';
import { messageOf } from 'src/composables/errors';
import { useClubStore } from 'stores/club';

defineOptions({ name: 'ReschedulePage' });

const $q = useQuasar();
const route = useRoute();
const router = useRouter();
const clubStore = useClubStore();
const club = computed(() => clubStore.club);
const id = String(route.params.id);

const booking = ref<Booking | null>(null);
const error = ref('');
const canMove = computed(() => !!booking.value?.rescheduleUntil && Date.parse(booking.value.rescheduleUntil) > Date.now());
const days = computed(() => (club.value ? dateRange(club.value.today, club.value.lastBookableDate) : []));

const date = ref<ClubDate>('');
const availability = ref<Availability | null>(null);
const picked = ref<{ courtId: string; startHour: number } | null>(null);
const hint = ref('');
const busy = ref(false);

/** The Slots a move starting at `startHour` on that Court would take, or why it cannot start there. */
function runFrom(courtId: string, startHour: number): { slots: Slot[]; problem: string } {
  const b = booking.value!;
  const court = availability.value?.courts.find((c) => c.courtId === courtId);
  const slots = (court?.slots ?? []).filter((s) => hourOf(s.start) >= startHour && hourOf(s.start) < startHour + b.hours);
  if (slots.length < b.hours || slots.some((s) => s.status !== 'available'))
    return { slots: [], problem: `There are not ${formatHours(b.hours)} free in a row from ${formatHour(startHour)} on that court.` };
  const total = slots.reduce((sum, s) => sum + (s.price ?? 0), 0);
  if (total > b.total)
    return { slots: [], problem: `That time costs ${formatBaht(total)}, more than the ${formatBaht(b.total)} you paid. Choose a cheaper time.` };
  return { slots, problem: '' };
}

function choose(courtId: string, slot: Slot) {
  const startHour = hourOf(slot.start);
  const { problem } = runFrom(courtId, startHour);
  hint.value = problem;
  picked.value = problem ? null : { courtId, startHour };
}

const selectedKeys = computed(() => {
  const p = picked.value;
  return p ? runFrom(p.courtId, p.startHour).slots.map((s) => `${p.courtId}|${s.startAt}`) : [];
});

const target = computed(() => {
  const p = picked.value;
  const court = p && availability.value?.courts.find((c) => c.courtId === p.courtId);
  if (!p || !court || !booking.value || !availability.value) return null;
  return {
    courtId: p.courtId,
    courtName: court.name,
    date: availability.value.date,
    startHour: p.startHour,
    start: formatHour(p.startHour),
    end: formatHour(p.startHour + booking.value.hours),
    hours: booking.value.hours,
  };
});

async function confirm() {
  const t = target.value;
  if (!t) return;
  busy.value = true;
  hint.value = '';
  try {
    await useApi().reschedule(id, { courtId: t.courtId, date: t.date, startHour: t.startHour });
    $q.notify({ type: 'positive', message: 'Your booking has been moved.' });
    await router.replace({ name: 'booking', params: { id } });
  } catch (e) {
    hint.value = messageOf(e);
    picked.value = null;
    // The grid may be out of date (someone took the time): show it afresh.
    if (e instanceof ApiError && ['slot_taken', 'slot_blocked'].includes(e.code)) void loadDay();
    else booking.value = await useApi().getBooking(id).catch(() => booking.value);
  } finally {
    busy.value = false;
  }
}

async function loadDay() {
  if (!date.value) return;
  const requested = date.value;
  availability.value = null;
  try {
    const result = await useApi().getAvailability(requested);
    if (requested === date.value) availability.value = result;
  } catch (e) {
    if (requested === date.value) error.value = messageOf(e);
  }
}

watch(date, () => {
  picked.value = null;
  hint.value = '';
  void loadDay();
});

onMounted(async () => {
  try {
    const [b, info] = await Promise.all([useApi().getBooking(id), clubStore.load()]);
    booking.value = b;
    if (!info) error.value = clubStore.error;
    else date.value = b.date >= info.today && b.date <= info.lastBookableDate ? b.date : info.today;
  } catch (e) {
    error.value = messageOf(e);
  }
});
</script>

<style scoped>
.move { padding-bottom: 140px; }
.page-title { margin: 0 0 8px; color: var(--pbc-ink); }
.days { display: flex; gap: 8px; overflow-x: auto; padding: 16px 0 8px; }
.day {
  flex: 0 0 auto; min-width: 76px; padding: 10px 12px; cursor: pointer; font: inherit; text-align: center;
  background: var(--pbc-surface); color: var(--pbc-text); border: 1px solid var(--pbc-line); border-radius: var(--pbc-r-lg);
}
.day:hover { border-color: var(--pbc-ink); }
.day:focus-visible { outline: 2px solid var(--pbc-ink); outline-offset: 2px; }
.day--active { background: var(--pbc-ink); border-color: var(--pbc-ink); color: #fff; }
.day__dow { display: block; font-size: 0.75rem; font-weight: 600; letter-spacing: 0.04em; text-transform: uppercase; opacity: 0.8; }
.day__date { display: block; font-weight: 600; margin-top: 2px; }
.bar { position: fixed; left: 0; right: 0; bottom: 0; z-index: 10; background: var(--pbc-surface); border-top: 1px solid var(--pbc-line); box-shadow: 0 -4px 16px rgba(27, 42, 65, 0.08); }
.bar__inner { max-width: 1120px; margin: 0 auto; padding: 12px 16px; display: flex; align-items: center; justify-content: space-between; gap: 16px; }
.bar__what { font-weight: 500; }
</style>
