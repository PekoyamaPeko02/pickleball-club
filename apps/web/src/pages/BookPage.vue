<template>
  <q-page class="pbc-page book">
    <h1 class="pbc-h2 page-title">Book a court</h1>
    <p class="pbc-muted">Prices are per court, per hour. Tap the hours you want on one court.</p>

    <div v-if="club" class="days" role="tablist" aria-label="Choose a day">
      <button
        v-for="day in days" :key="day" type="button" role="tab" class="day" :class="{ 'day--active': day === date }"
        :aria-selected="day === date" @click="date = day"
      >
        <span class="day__dow">{{ day === club.today ? 'Today' : formatWeekday(day) }}</span>
        <span class="day__date pbc-num">{{ formatDayMonth(day) }}</span>
      </button>
    </div>

    <q-banner v-if="error" class="text-negative q-mt-md" role="alert">
      {{ error }}
      <template #action><q-btn flat no-caps label="Try again" @click="load" /></template>
    </q-banner>
    <q-skeleton v-else-if="!availability" height="420px" class="q-mt-md" />
    <template v-else>
      <p class="q-mt-md q-mb-sm">
        <strong>{{ formatLongDate(availability.date) }}</strong>
        <span class="pbc-muted"> · {{ availability.dayType === 'holiday' ? 'Weekend / holiday prices' : 'Weekday prices' }}</span>
      </p>
      <AvailabilityGrid :availability="availability" :selectable="availability.withinBookingWindow" :selected="selectedKeys" @select="toggle" />
    </template>

    <div v-if="summary" class="bar" role="region" aria-label="Your selection">
      <div class="bar__inner">
        <div>
          <div class="bar__what">{{ summary.courtName }} · {{ whenOf(summary) }}</div>
          <div class="bar__total pbc-num">{{ formatBaht(summary.total) }}</div>
        </div>
        <q-btn unelevated no-caps size="lg" class="pbc-btn-brass" label="Continue" @click="proceed" />
      </div>
    </div>
  </q-page>
</template>

<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue';
import { useRouter } from 'vue-router';
import type { Availability, ClubDate, Slot } from '@pbc/api';
import { AvailabilityGrid, dateRange, formatBaht, formatDayMonth, formatHour, formatLongDate, formatWeekday, hourOf } from '@pbc/ui';
import { useApi } from 'boot/api';
import { whenOf } from 'src/composables/bookings';
import { useCartStore } from 'stores/cart';
import { useClubStore } from 'stores/club';

defineOptions({ name: 'BookPage' });

const router = useRouter();
const store = useClubStore();
const cart = useCartStore();
const club = computed(() => store.club);
const days = computed(() => (club.value ? dateRange(club.value.today, club.value.lastBookableDate) : []));

const date = ref<ClubDate>('');
const availability = ref<Availability | null>(null);
const error = ref('');

// ---- selection: consecutive hours on one Court
const picked = ref<{ courtId: string; hours: number[] } | null>(null);

function toggle(courtId: string, slot: Slot) {
  const hour = hourOf(slot.start);
  const current = picked.value;
  if (!current || current.courtId !== courtId) {
    picked.value = { courtId, hours: [hour] };
    return;
  }
  const hours = [...current.hours];
  const first = hours[0]!;
  const last = hours[hours.length - 1]!;
  if (hours.includes(hour)) {
    if (hours.length === 1) picked.value = null;                                   // tap the only hour again: clear
    else if (hour === first) picked.value = { courtId, hours: hours.slice(1) };     // trim from either end
    else if (hour === last) picked.value = { courtId, hours: hours.slice(0, -1) };
    else picked.value = { courtId, hours: [hour] };                                 // a middle hour: start over from it
  } else if (hour === first - 1) picked.value = { courtId, hours: [hour, ...hours] };
  else if (hour === last + 1) picked.value = { courtId, hours: [...hours, hour] };
  else picked.value = { courtId, hours: [hour] };                                   // not next to the run: start over
}

const pickedSlots = computed(() => {
  const current = picked.value;
  const court = current && availability.value?.courts.find((c) => c.courtId === current.courtId);
  return court ? court.slots.filter((s) => current!.hours.includes(hourOf(s.start))) : [];
});
const selectedKeys = computed(() => (picked.value ? pickedSlots.value.map((s) => `${picked.value!.courtId}|${s.startAt}`) : []));

const summary = computed(() => {
  const current = picked.value;
  const court = current && availability.value?.courts.find((c) => c.courtId === current.courtId);
  if (!current || !court || !availability.value || pickedSlots.value.length === 0) return null;
  const startHour = current.hours[0]!;
  return {
    courtId: court.courtId,
    courtName: court.name,
    date: availability.value.date,
    startHour,
    hours: current.hours.length,
    start: formatHour(startHour),
    end: formatHour(startHour + current.hours.length),
    total: pickedSlots.value.reduce((sum, s) => sum + (s.price ?? 0), 0),
  };
});

function proceed() {
  const s = summary.value;
  if (!s) return;
  cart.set({ courtId: s.courtId, courtName: s.courtName, date: s.date, startHour: s.startHour, hours: s.hours, total: s.total });
  void router.push({ name: 'checkout' }); // the router sends a guest through sign-in first and back here
}

async function load() {
  if (!date.value) return;
  const requested = date.value;
  availability.value = null;
  error.value = '';
  try {
    const result = await useApi().getAvailability(requested);
    if (requested === date.value) availability.value = result; // ignore a reply for a day the user has left
  } catch (e) {
    if (requested === date.value) error.value = e instanceof Error ? e.message : 'Could not load the courts.';
  }
}

watch(date, () => {
  picked.value = null;
  void load();
});
onMounted(async () => {
  const info = await store.load();
  if (info) date.value = info.today;
  else error.value = store.error;
});
</script>

<style scoped>
.book { padding-bottom: 140px; }
.page-title { margin: 0 0 4px; color: var(--pbc-ink); }
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
.bar__total { font-family: var(--pbc-font-display); font-size: 1.5rem; color: var(--pbc-ink); }
</style>
