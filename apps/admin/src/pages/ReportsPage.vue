<template>
  <q-page class="pbc-page reports">
    <h1 class="pbc-h3 page-title">Revenue</h1>

    <!-- one row of filters; everything below shows the same range -->
    <div class="filters">
      <q-btn-toggle v-model="preset" no-caps unelevated toggle-color="primary" color="white" text-color="primary" :options="PRESETS" class="filters__presets" />
      <q-input v-model="from" dense outlined type="date" label="From" class="filters__date" @update:model-value="preset = 'custom'" />
      <q-input v-model="to" dense outlined type="date" label="To" class="filters__date" @update:model-value="preset = 'custom'" />
      <q-btn-toggle v-model="grouping" no-caps unelevated toggle-color="primary" color="white" text-color="primary"
                    :options="[{ label: 'By day', value: 'day' }, { label: 'By month', value: 'month' }]" />
    </div>
    <p class="pbc-muted note">By the day of play. A booking counts once it is paid; cancelled bookings are left out.</p>

    <q-banner v-if="error" class="text-negative" role="alert">
      {{ error }}
      <template #action><q-btn flat no-caps label="Try again" @click="load()" /></template>
    </q-banner>
    <q-skeleton v-else-if="!report" height="420px" />

    <div v-else :class="{ stale: loading }">
      <!-- headline numbers -->
      <dl class="tiles">
        <div class="tile tile--lead"><dt>Total revenue</dt><dd>{{ formatBaht(totals.total) }}</dd></div>
        <div class="tile"><dt>Online</dt><dd>{{ formatBaht(totals.online) }}</dd></div>
        <div class="tile"><dt>Walk-in</dt><dd>{{ formatBaht(totals.walkIn) }}</dd></div>
        <div class="tile"><dt>Bookings</dt><dd>{{ totals.bookings.toLocaleString('en-US') }}</dd></div>
        <div class="tile"><dt>Hours sold</dt><dd>{{ totals.hours.toLocaleString('en-US') }}</dd></div>
        <div class="tile"><dt>Occupancy</dt><dd>{{ percent(totals.hours, totals.availableHours) }}</dd></div>
      </dl>

      <!-- chart -->
      <section class="pbc-card card" aria-labelledby="chart-title">
        <div class="card__head">
          <h2 id="chart-title" class="card__title">Revenue {{ grouping === 'day' ? 'by day' : 'by month' }}</h2>
          <ul class="legend" aria-label="Legend">
            <li><span class="swatch swatch--online" />Online</li>
            <li><span class="swatch swatch--walkin" />Walk-in</li>
          </ul>
        </div>

        <p v-if="totals.total === 0" class="pbc-muted empty">No revenue in this range.</p>
        <div v-else class="chart">
          <div class="chart__axis" aria-hidden="true">
            <span v-for="tick in ticks" :key="tick" :style="{ bottom: `${(tick / scaleMax) * 100}%` }">{{ compact(tick) }}</span>
          </div>
          <div class="chart__plot" @pointerleave="active = null">
            <span v-for="tick in ticks" :key="tick" class="chart__grid" :style="{ bottom: `${(tick / scaleMax) * 100}%` }" aria-hidden="true" />
            <div class="chart__cols">
              <button
                v-for="(b, i) in buckets" :key="b.key" type="button" class="slot" :class="{ 'slot--active': active === i }"
                :aria-label="`${b.label}: total ${formatBaht(b.total)}, online ${formatBaht(b.online)}, walk-in ${formatBaht(b.walkIn)}`"
                @pointerenter="active = i" @focus="active = i" @blur="active = null"
              >
                <span class="slot__bar">
                  <span v-if="b.walkIn > 0" class="seg seg--walkin seg--top" :style="{ height: `${(b.walkIn / scaleMax) * 100}%` }" />
                  <span v-if="b.online > 0" class="seg seg--online" :class="{ 'seg--top': b.walkIn === 0 }" :style="{ height: `${(b.online / scaleMax) * 100}%` }" />
                </span>
              </button>
            </div>
            <div v-if="active !== null && buckets[active]" class="tip" :class="{ 'tip--left': active > buckets.length / 2 }"
                 :style="{ left: `${((active + 0.5) / buckets.length) * 100}%` }" role="status">
              <div class="tip__title">{{ buckets[active]!.label }}</div>
              <div class="tip__row"><span class="key key--online" /><strong>{{ formatBaht(buckets[active]!.online) }}</strong><span>Online</span></div>
              <div class="tip__row"><span class="key key--walkin" /><strong>{{ formatBaht(buckets[active]!.walkIn) }}</strong><span>Walk-in</span></div>
              <div class="tip__row tip__row--total"><span class="key" /><strong>{{ formatBaht(buckets[active]!.total) }}</strong><span>Total</span></div>
            </div>
          </div>
          <div class="chart__x" aria-hidden="true">
            <span v-for="(b, i) in buckets" :key="b.key">{{ i % labelEvery === 0 ? b.short : '' }}</span>
          </div>
        </div>
      </section>

      <!-- the same numbers as a table -->
      <section class="pbc-card card">
        <div class="card__head">
          <h2 class="card__title">{{ grouping === 'day' ? 'Day by day' : 'Month by month' }}</h2>
          <div class="row q-gutter-sm">
            <q-btn outline no-caps dense icon="download" label="Summary (CSV)" :loading="downloading === 'summary'" @click="download('summary')" />
            <q-btn outline no-caps dense icon="download" label="Bookings (CSV)" :loading="downloading === 'bookings'" @click="download('bookings')" />
          </div>
        </div>
        <div class="table-scroll">
          <table class="table">
            <thead>
              <tr>
                <th scope="col">{{ grouping === 'day' ? 'Date' : 'Month' }}</th>
                <th scope="col">Bookings</th><th scope="col">Hours sold</th><th scope="col">Online</th><th scope="col">Walk-in</th><th scope="col">Total</th><th scope="col">Occupancy</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="b in buckets" :key="b.key">
                <th scope="row">{{ b.label }}</th>
                <td>{{ b.bookings }}</td><td>{{ b.hours }}</td><td>{{ formatBaht(b.online) }}</td><td>{{ formatBaht(b.walkIn) }}</td>
                <td><strong>{{ formatBaht(b.total) }}</strong></td><td>{{ percent(b.hours, b.availableHours) }}</td>
              </tr>
            </tbody>
            <tfoot>
              <tr>
                <th scope="row">Total</th>
                <td>{{ totals.bookings }}</td><td>{{ totals.hours }}</td><td>{{ formatBaht(totals.online) }}</td><td>{{ formatBaht(totals.walkIn) }}</td>
                <td><strong>{{ formatBaht(totals.total) }}</strong></td><td>{{ percent(totals.hours, totals.availableHours) }}</td>
              </tr>
            </tfoot>
          </table>
        </div>
      </section>
    </div>
  </q-page>
</template>

<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue';
import { exportFile, useQuasar } from 'quasar';
import type { ClubDate, RevenueDay, RevenueReport } from '@pbc/api';
import { addDays, formatBaht, formatDayMonth, formatWeekday } from '@pbc/ui';
import { useApi } from 'boot/api';
import { messageOf } from 'src/composables/errors';
import { useClubStore } from 'stores/club';

defineOptions({ name: 'ReportsPage' });

type Preset = 'last7' | 'last30' | 'last90' | 'month' | 'next14' | 'custom';
const PRESETS: { label: string; value: Preset }[] = [
  { label: 'Last 7 days', value: 'last7' },
  { label: 'Last 30 days', value: 'last30' },
  { label: 'Last 90 days', value: 'last90' },
  { label: 'This month', value: 'month' },
  { label: 'Next 14 days', value: 'next14' },
];

const $q = useQuasar();
const clubStore = useClubStore();
const preset = ref<Preset>('last30');
const from = ref<ClubDate>('');
const to = ref<ClubDate>('');
const grouping = ref<'day' | 'month'>('day');
const report = ref<RevenueReport | null>(null);
const loading = ref(false);
const error = ref('');
const active = ref<number | null>(null);

function applyPreset() {
  const today = clubStore.club?.today;
  if (!today || preset.value === 'custom') return;
  const ranges: Record<Exclude<Preset, 'custom'>, [ClubDate, ClubDate]> = {
    last7: [addDays(today, -6), today],
    last30: [addDays(today, -29), today],
    last90: [addDays(today, -89), today],
    month: [`${today.slice(0, 8)}01`, today],
    next14: [today, addDays(today, 14)],
  };
  [from.value, to.value] = ranges[preset.value];
}

async function load() {
  if (!from.value || !to.value) return;
  const [f, t] = [from.value, to.value];
  loading.value = true; // the previous report stays on screen, dimmed, until the new one arrives
  error.value = '';
  try {
    const result = await useApi().getRevenueReport(f, t);
    if (f !== from.value || t !== to.value) return; // the range changed meanwhile
    report.value = result;
    grouping.value = result.days.length > 62 ? 'month' : 'day';
  } catch (e) {
    report.value = null;
    error.value = messageOf(e);
  } finally {
    loading.value = false;
  }
}

// ---- buckets: the days themselves, or the days added up by month
interface Bucket extends Omit<RevenueDay, 'date'> { key: string; label: string; short: string }

const monthName = new Intl.DateTimeFormat('en-GB', { month: 'long', year: 'numeric', timeZone: 'UTC' });
const monthShort = new Intl.DateTimeFormat('en-GB', { month: 'short', timeZone: 'UTC' });

const buckets = computed<Bucket[]>(() => {
  const days = report.value?.days ?? [];
  if (grouping.value === 'day')
    return days.map((d) => ({ ...d, key: d.date, label: `${formatWeekday(d.date)} ${formatDayMonth(d.date)}`, short: formatDayMonth(d.date) }));

  const byMonth = new Map<string, Bucket>();
  for (const d of days) {
    const key = d.date.slice(0, 7);
    const first = new Date(`${key}-01T00:00:00Z`);
    const b = byMonth.get(key) ?? { key, label: monthName.format(first), short: monthShort.format(first), bookings: 0, hours: 0, online: 0, walkIn: 0, total: 0, availableHours: 0 };
    b.bookings += d.bookings; b.hours += d.hours; b.online += d.online; b.walkIn += d.walkIn; b.total += d.total; b.availableHours += d.availableHours;
    byMonth.set(key, b);
  }
  return [...byMonth.values()];
});

const totals = computed(() => (report.value?.days ?? []).reduce(
  (sum, d) => ({
    bookings: sum.bookings + d.bookings, hours: sum.hours + d.hours, online: sum.online + d.online,
    walkIn: sum.walkIn + d.walkIn, total: sum.total + d.total, availableHours: sum.availableHours + d.availableHours,
  }),
  { bookings: 0, hours: 0, online: 0, walkIn: 0, total: 0, availableHours: 0 }));

// ---- chart scale: a round number at or above the tallest column, with three ticks
const scaleMax = computed(() => {
  const max = Math.max(1, ...buckets.value.map((b) => b.total));
  const unit = 10 ** Math.floor(Math.log10(max));
  return [1, 2, 2.5, 5, 10].map((m) => m * unit).find((v) => v >= max)!;
});
const ticks = computed(() => [0, scaleMax.value / 2, scaleMax.value]);
const labelEvery = computed(() => Math.max(1, Math.ceil(buckets.value.length / 8)));

const compact = (n: number) => (n >= 1_000_000 ? `${+(n / 1_000_000).toFixed(1)}M` : n >= 1000 ? `${+(n / 1000).toFixed(1)}K` : String(n));
const percent = (part: number, whole: number) => (whole > 0 ? `${Math.round((part / whole) * 100)}%` : '—');

// ---- CSV
const downloading = ref<'summary' | 'bookings' | null>(null);
async function download(kind: 'summary' | 'bookings') {
  downloading.value = kind;
  try {
    const csv = kind === 'summary' ? await useApi().getRevenueCsv(from.value, to.value) : await useApi().getBookingsCsv(from.value, to.value);
    const name = `${kind === 'summary' ? 'revenue' : 'bookings'}_${from.value}_${to.value}.csv`;
    // The byte-order mark makes Excel read Thai names correctly.
    if (exportFile(name, csv, { mimeType: 'text/csv', byteOrderMark: '﻿' }) !== true)
      $q.notify({ type: 'negative', message: 'Your browser blocked the download.' });
  } catch (e) {
    $q.notify({ type: 'negative', message: messageOf(e) });
  } finally {
    downloading.value = null;
  }
}

watch(preset, applyPreset);
watch([from, to], () => void load());
onMounted(async () => {
  await clubStore.load();
  applyPreset();
});
</script>

<style scoped>
/* Chart colours: validated as a categorical pair on a white surface (lightness band, chroma, colour-vision separation, 3:1 contrast). */
.reports { --series-online: #3b6ea5; --series-walkin: #b8872f; max-width: 1080px; }
.page-title { margin: 0 0 16px; color: var(--pbc-ink); }
.filters { display: flex; flex-wrap: wrap; align-items: center; gap: 12px; }
.filters__presets { border: 1px solid var(--pbc-line); }
.filters__date { width: 160px; }
.note { margin: 8px 0 16px; font-size: 0.85rem; }
.stale { opacity: 0.5; transition: opacity 0.15s; }

.tiles { display: grid; grid-template-columns: repeat(auto-fit, minmax(150px, 1fr)); gap: 12px; margin: 0 0 16px; }
.tile { background: var(--pbc-surface); border: 1px solid var(--pbc-line); border-radius: var(--pbc-r-lg); padding: 14px 16px; }
.tile dt { font-size: 0.8rem; color: var(--pbc-text-2); }
.tile dd { margin: 2px 0 0; font-size: 1.5rem; font-weight: 600; color: var(--pbc-text); line-height: 1.2; }
.tile--lead dd { font-size: 2rem; }

.card { padding: 16px 20px 20px; margin-bottom: 16px; }
.card__head { display: flex; flex-wrap: wrap; align-items: center; justify-content: space-between; gap: 8px 16px; margin-bottom: 12px; }
.card__title { margin: 0; font-size: 1rem; font-weight: 600; line-height: 1.4; color: var(--pbc-text); }
.empty { margin: 24px 0; }

.legend { display: flex; gap: 16px; list-style: none; margin: 0; padding: 0; font-size: 0.85rem; color: var(--pbc-text-2); }
.legend li { display: flex; align-items: center; gap: 6px; }
.swatch { width: 10px; height: 10px; border-radius: 2px; }
.swatch--online, .key--online, .seg--online { background: var(--series-online); }
.swatch--walkin, .key--walkin, .seg--walkin { background: var(--series-walkin); }

.chart { display: grid; grid-template-columns: 44px 1fr; grid-template-rows: 220px auto; column-gap: 8px; }
.chart__axis { position: relative; font-size: 0.72rem; color: var(--pbc-text-2); font-variant-numeric: tabular-nums; }
.chart__axis span { position: absolute; right: 0; transform: translateY(50%); }
.chart__plot { position: relative; }
.chart__grid { position: absolute; left: 0; right: 0; height: 1px; background: var(--pbc-line); }
.chart__cols { position: absolute; inset: 0; display: flex; align-items: stretch; }
.slot { flex: 1 1 0; min-width: 0; display: flex; align-items: flex-end; justify-content: center; padding: 0; border: 0; background: none; cursor: default; }
.slot--active { background: rgba(27, 42, 65, 0.05); }
.slot:focus-visible { outline: 2px solid var(--pbc-ink); outline-offset: -2px; }
.slot__bar { display: flex; flex-direction: column; justify-content: flex-end; gap: 2px; width: min(24px, 70%); height: 100%; }
.seg { display: block; min-height: 2px; }
.seg--top { border-radius: 4px 4px 0 0; }
.chart__x { grid-column: 2; display: flex; padding-top: 6px; font-size: 0.72rem; color: var(--pbc-text-2); }
.chart__x span { flex: 1 1 0; min-width: 0; text-align: center; white-space: nowrap; overflow: visible; }

.tip {
  position: absolute; top: 0; z-index: 2; transform: translateX(12px); pointer-events: none; min-width: 150px;
  background: var(--pbc-surface); border: 1px solid var(--pbc-line-strong); border-radius: var(--pbc-r-sm); box-shadow: var(--pbc-shadow-1);
  padding: 8px 10px; font-size: 0.8rem; color: var(--pbc-text-2);
}
.tip--left { transform: translateX(calc(-100% - 12px)); }
.tip__title { font-weight: 600; color: var(--pbc-text); margin-bottom: 4px; }
.tip__row { display: grid; grid-template-columns: 12px auto 1fr; align-items: center; gap: 6px; }
.tip__row strong { color: var(--pbc-text); font-variant-numeric: tabular-nums; }
.tip__row--total { margin-top: 4px; padding-top: 4px; border-top: 1px solid var(--pbc-line); }
.key { width: 12px; height: 3px; border-radius: 2px; }

.table-scroll { overflow-x: auto; }
.table { width: 100%; border-collapse: collapse; font-variant-numeric: tabular-nums; font-size: 0.9rem; }
.table th, .table td { padding: 8px 12px; text-align: right; white-space: nowrap; border-bottom: 1px solid var(--pbc-line); }
.table th:first-child { text-align: left; }
.table thead th { font-size: 0.8rem; font-weight: 600; color: var(--pbc-text-2); }
.table tbody th { font-weight: 500; }
.table tfoot th, .table tfoot td { border-bottom: 0; font-weight: 600; }
</style>
