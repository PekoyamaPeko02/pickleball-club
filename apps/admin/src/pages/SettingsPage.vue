<template>
  <q-page class="pbc-page settings">
    <h1 class="pbc-h3 page-title">Settings</h1>

    <q-banner v-if="error" class="text-negative" role="alert">
      {{ error }}
      <template #action><q-btn flat no-caps label="Try again" @click="load()" /></template>
    </q-banner>
    <q-skeleton v-else-if="!settings" height="320px" />

    <template v-else>
      <q-tabs v-model="tab" no-caps align="left" active-color="primary" indicator-color="primary" class="tabs">
        <q-tab name="rules" label="Booking rules" />
        <q-tab name="courts" label="Courts" />
        <q-tab name="hours" label="Opening hours" />
        <q-tab name="prices" label="Prices" />
        <q-tab name="holidays" label="Holidays" />
      </q-tabs>

      <q-tab-panels v-model="tab" class="panels">
        <!-- booking rules -->
        <q-tab-panel name="rules">
          <q-form class="form narrow" @submit.prevent="saveClub">
            <q-input v-model.trim="club.name" outlined label="Club name" :rules="[required]" lazy-rules />
            <q-input v-model.number="club.bookingWindowDays" outlined type="number" min="1" max="365" label="Booking window (days)"
                     hint="How many days ahead a customer can book online." />
            <q-input v-model.number="club.rescheduleNoticeHours" outlined type="number" min="0" max="720" label="Reschedule notice (hours)"
                     hint="A customer can move a booking until this many hours before it starts." />
            <div><q-btn unelevated no-caps type="submit" class="pbc-btn-ink" label="Save" :loading="busy" /></div>
          </q-form>
        </q-tab-panel>

        <!-- courts -->
        <q-tab-panel name="courts">
          <ul class="rows">
            <li v-for="c in settings.courts" :key="c.id" class="row-item">
              <span>
                <strong>{{ c.name }}</strong>
                <span class="pbc-muted"> · {{ c.indoor ? 'Indoor' : 'Outdoor' }}</span>
                <span v-if="!c.isActive" class="off"> · Switched off</span>
              </span>
              <q-btn flat no-caps label="Edit" @click="editCourt(c)" />
            </li>
          </ul>
          <q-btn outline no-caps icon="add" label="Add a court" class="q-mt-md" @click="editCourt(null)" />
        </q-tab-panel>

        <!-- hours -->
        <q-tab-panel name="hours">
          <q-form class="narrow" @submit.prevent="saveHours">
            <div v-for="day in week" :key="day.dayOfWeek" class="day">
              <q-toggle v-model="day.open" :label="WEEKDAY_NAMES[day.dayOfWeek]" class="day__name" />
              <template v-if="day.open">
                <q-select v-model="day.openHour" dense outlined emit-value map-options :options="OPEN_OPTIONS" :aria-label="`${WEEKDAY_NAMES[day.dayOfWeek]} opens`" class="day__hour" />
                <span class="pbc-muted">to</span>
                <q-select v-model="day.closeHour" dense outlined emit-value map-options :options="CLOSE_OPTIONS" :aria-label="`${WEEKDAY_NAMES[day.dayOfWeek]} closes`" class="day__hour" />
              </template>
              <span v-else class="pbc-muted">Closed</span>
            </div>
            <p class="pbc-muted q-mt-md">Hours with no price rule are shown as closed on the booking page, so check the Prices tab after changing these.</p>
            <q-btn unelevated no-caps type="submit" class="pbc-btn-ink" label="Save the week" :loading="busy" />
          </q-form>
        </q-tab-panel>

        <!-- prices -->
        <q-tab-panel name="prices">
          <p class="pbc-muted lead">
            Price per court, per hour. A rule for one court beats a rule for all courts. Saturdays, Sundays and the days on the Holidays tab use the
            "Weekend &amp; holiday" rules. Bookings already made keep the price they were sold at.
          </p>
          <template v-for="group in PRICE_GROUPS" :key="group.dayType">
            <h2 class="pbc-label pbc-muted q-mt-lg">{{ group.label }}</h2>
            <p v-if="rulesOf(group.dayType).length === 0" class="pbc-muted">No rules — nothing can be booked on these days.</p>
            <ul v-else class="rows">
              <li v-for="r in rulesOf(group.dayType)" :key="r.id" class="row-item">
                <span>
                  <strong class="pbc-num">{{ formatHour(r.startHour) }}–{{ formatHour(r.endHour) }}</strong>
                  <span class="pbc-muted"> · {{ courtName(r.courtId) }}</span>
                  <span v-if="r.label" class="pbc-muted"> · {{ r.label }}</span>
                </span>
                <span class="row-item__end">
                  <strong class="pbc-num">{{ formatBaht(r.pricePerHour) }}</strong>
                  <q-btn flat no-caps label="Edit" @click="editRule(r, group.dayType)" />
                  <q-btn flat round icon="delete" color="negative" :aria-label="`Delete the ${formatHour(r.startHour)}–${formatHour(r.endHour)} rule`" @click="removeRule(r)" />
                </span>
              </li>
            </ul>
            <q-btn outline no-caps icon="add" label="Add a rule" class="q-mt-sm" @click="editRule(null, group.dayType)" />
          </template>
        </q-tab-panel>

        <!-- holidays -->
        <q-tab-panel name="holidays">
          <p class="pbc-muted lead">Extra days priced as a holiday. Saturdays and Sundays are holidays already.</p>
          <p v-if="settings.holidays.length === 0" class="pbc-muted">No extra holidays.</p>
          <ul v-else class="rows narrow">
            <li v-for="h in settings.holidays" :key="h.date" class="row-item">
              <span><strong>{{ formatLongDate(h.date) }}</strong><span v-if="h.note" class="pbc-muted"> · {{ h.note }}</span></span>
              <q-btn flat round icon="delete" color="negative" :aria-label="`Remove ${formatLongDate(h.date)}`" @click="removeHoliday(h.date)" />
            </li>
          </ul>
          <q-form class="holiday-form" @submit.prevent="addHoliday">
            <q-input v-model="holiday.date" dense outlined type="date" label="Date" />
            <q-input v-model.trim="holiday.note" dense outlined label="Note (optional)" class="holiday-form__note" />
            <q-btn unelevated no-caps type="submit" class="pbc-btn-ink" label="Add" :disable="!holiday.date" :loading="busy" />
          </q-form>
        </q-tab-panel>
      </q-tab-panels>
    </template>

    <!-- court dialog -->
    <q-dialog v-model="courtDialog">
      <q-card class="dialog">
        <q-form @submit.prevent="saveCourt">
          <q-card-section class="form">
            <h2 class="pbc-h3 q-ma-none">{{ court.id ? 'Edit court' : 'Add a court' }}</h2>
            <q-input v-model.trim="court.name" outlined label="Name" :rules="[required]" lazy-rules />
            <q-toggle v-model="court.indoor" label="Indoor" />
            <q-input v-model.number="court.sortOrder" outlined type="number" min="0" label="Order on the page" hint="Lower numbers come first." />
            <q-toggle v-model="court.isActive" label="Open for booking" />
            <p v-if="dialogError" class="text-negative q-mb-none" role="alert">{{ dialogError }}</p>
          </q-card-section>
          <q-card-actions align="right">
            <q-btn v-close-popup flat no-caps label="Close" />
            <q-btn unelevated no-caps type="submit" class="pbc-btn-ink" label="Save" :loading="busy" />
          </q-card-actions>
        </q-form>
      </q-card>
    </q-dialog>

    <!-- price rule dialog -->
    <q-dialog v-model="ruleDialog">
      <q-card class="dialog">
        <q-form @submit.prevent="saveRule">
          <q-card-section class="form">
            <h2 class="pbc-h3 q-ma-none">{{ rule.id ? 'Edit price rule' : 'Add a price rule' }}</h2>
            <q-select v-model="rule.dayType" outlined emit-value map-options :options="PRICE_GROUPS.map((g) => ({ label: g.label, value: g.dayType }))" label="Days" />
            <q-select v-model="rule.courtId" outlined emit-value map-options :options="courtOptions" label="Court" />
            <div class="row q-col-gutter-sm">
              <q-select v-model="rule.startHour" class="col" outlined emit-value map-options :options="OPEN_OPTIONS" label="From" />
              <q-select v-model="rule.endHour" class="col" outlined emit-value map-options :options="CLOSE_OPTIONS" label="To" />
            </div>
            <q-input v-model.number="rule.pricePerHour" outlined type="number" min="0" step="1" label="Price per hour (THB)" />
            <q-input v-model.trim="rule.label" outlined label="Label (optional)" hint="Shown to the admin only, e.g. Peak." />
            <p v-if="dialogError" class="text-negative q-mb-none" role="alert">{{ dialogError }}</p>
          </q-card-section>
          <q-card-actions align="right">
            <q-btn v-close-popup flat no-caps label="Close" />
            <q-btn unelevated no-caps type="submit" class="pbc-btn-ink" label="Save" :loading="busy" />
          </q-card-actions>
        </q-form>
      </q-card>
    </q-dialog>
  </q-page>
</template>

<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue';
import { useQuasar } from 'quasar';
import type { ClubDate, CourtSettings, DayType, PriceRule, Settings } from '@pbc/api';
import { WEEKDAY_NAMES, formatBaht, formatHour, formatLongDate } from '@pbc/ui';
import { useApi } from 'boot/api';
import { messageOf } from 'src/composables/errors';
import { useClubStore } from 'stores/club';

defineOptions({ name: 'SettingsPage' });

const OPEN_OPTIONS = Array.from({ length: 24 }, (_, h) => ({ label: formatHour(h), value: h }));
const CLOSE_OPTIONS = Array.from({ length: 24 }, (_, i) => ({ label: i + 1 === 24 ? '24:00 (midnight)' : formatHour(i + 1), value: i + 1 }));
const PRICE_GROUPS: { dayType: DayType; label: string }[] = [
  { dayType: 'weekday', label: 'Weekdays (Monday–Friday)' },
  { dayType: 'holiday', label: 'Weekend & holiday' },
];

const $q = useQuasar();
const api = useApi();
const clubStore = useClubStore();
const tab = ref('rules');
const settings = ref<Settings | null>(null);
const error = ref('');
const busy = ref(false);
const required = (v: string) => !!v || 'Required';

const club = reactive({ name: '', bookingWindowDays: 14, rescheduleNoticeHours: 48 });
const week = ref<{ dayOfWeek: number; open: boolean; openHour: number; closeHour: number }[]>([]);

async function load() {
  error.value = '';
  try {
    const s = await api.getSettings();
    settings.value = s;
    Object.assign(club, { name: s.club.name, bookingWindowDays: s.club.bookingWindowDays, rescheduleNoticeHours: s.club.rescheduleNoticeHours });
    week.value = [1, 2, 3, 4, 5, 6, 0].map((dayOfWeek) => {
      const h = s.operatingHours.find((x) => x.dayOfWeek === dayOfWeek);
      return { dayOfWeek, open: !!h, openHour: h?.openHour ?? 6, closeHour: h?.closeHour ?? 23 };
    });
  } catch (e) {
    error.value = messageOf(e);
  }
}

/** Runs one change, says so, and shows the settings afresh. Returns whether it worked. */
async function change(call: () => Promise<unknown>, done: string, onError?: (message: string) => void): Promise<boolean> {
  busy.value = true;
  try {
    await call();
    $q.notify({ type: 'positive', message: done });
    await Promise.all([load(), clubStore.load(true)]);
    return true;
  } catch (e) {
    if (onError) onError(messageOf(e));
    else $q.notify({ type: 'negative', message: messageOf(e) });
    return false;
  } finally {
    busy.value = false;
  }
}

const saveClub = () => change(() => api.updateClubSettings({ ...club }), 'Booking rules saved.');

const saveHours = () => change(
  () => api.setOperatingHours(week.value.filter((d) => d.open).map(({ dayOfWeek, openHour, closeHour }) => ({ dayOfWeek, openHour, closeHour }))),
  'Opening hours saved.');

// ---- courts
const courtDialog = ref(false);
const dialogError = ref('');
const court = reactive({ id: '', name: '', indoor: false, sortOrder: 0, isActive: true });

function editCourt(c: CourtSettings | null) {
  const nextOrder = Math.max(0, ...(settings.value?.courts.map((x) => x.sortOrder) ?? [])) + 1;
  Object.assign(court, c ?? { id: '', name: '', indoor: false, sortOrder: nextOrder, isActive: true });
  dialogError.value = '';
  courtDialog.value = true;
}

async function saveCourt() {
  const { id, ...req } = court;
  const ok = await change(() => (id ? api.updateCourt(id, req) : api.createCourt(req)), 'Court saved.', (m) => { dialogError.value = m; });
  if (ok) courtDialog.value = false;
}

const courtOptions = computed(() => [{ label: 'All courts', value: null as string | null }, ...(settings.value?.courts.map((c) => ({ label: c.name, value: c.id as string | null })) ?? [])]);
const courtName = (id: string | null) => (id ? settings.value?.courts.find((c) => c.id === id)?.name ?? 'Unknown court' : 'All courts');

// ---- price rules
const ruleDialog = ref(false);
const rule = reactive({ id: '', courtId: null as string | null, dayType: 'weekday' as DayType, startHour: 6, endHour: 23, pricePerHour: 0, label: '' as string | null });
const rulesOf = (dayType: DayType) => settings.value?.priceRules.filter((r) => r.dayType === dayType) ?? [];

function editRule(r: PriceRule | null, dayType: DayType) {
  Object.assign(rule, r ?? { id: '', courtId: null, dayType, startHour: 6, endHour: 23, pricePerHour: 0, label: '' });
  dialogError.value = '';
  ruleDialog.value = true;
}

async function saveRule() {
  const { id, ...rest } = rule;
  const req = { ...rest, label: rest.label || null };
  const ok = await change(() => (id ? api.updatePriceRule(id, req) : api.createPriceRule(req)), 'Price rule saved.', (m) => { dialogError.value = m; });
  if (ok) ruleDialog.value = false;
}

function removeRule(r: PriceRule) {
  $q.dialog({
    title: 'Delete this price rule?',
    message: `${formatHour(r.startHour)}–${formatHour(r.endHour)}, ${courtName(r.courtId)}, ${formatBaht(r.pricePerHour)} per hour. Hours left without any rule cannot be booked.`,
    ok: { label: 'Delete', color: 'negative', flat: true, noCaps: true },
    cancel: { label: 'Keep it', flat: true, noCaps: true },
  }).onOk(() => void change(() => api.deletePriceRule(r.id), 'Price rule deleted.'));
}

// ---- holidays
const holiday = reactive({ date: '' as ClubDate, note: '' });
async function addHoliday() {
  const ok = await change(() => api.saveHoliday({ date: holiday.date, note: holiday.note || null }), 'Holiday added.');
  if (ok) Object.assign(holiday, { date: '', note: '' });
}
const removeHoliday = (date: ClubDate) => change(() => api.deleteHoliday(date), 'Holiday removed.');

onMounted(load);
</script>

<style scoped>
.settings { max-width: 880px; }
.page-title { margin: 0 0 16px; color: var(--pbc-ink); }
.tabs { border-bottom: 1px solid var(--pbc-line); }
.panels { background: transparent; }
.panels :deep(.q-tab-panel) { padding: 24px 0; }
.form { display: grid; gap: 12px; }
.narrow { max-width: 520px; }
.lead { max-width: 70ch; }
.rows { list-style: none; margin: 0; padding: 0; background: var(--pbc-surface); border: 1px solid var(--pbc-line); border-radius: var(--pbc-r-lg); overflow: hidden; }
.rows li + li { border-top: 1px solid var(--pbc-line); }
.row-item { display: flex; align-items: center; justify-content: space-between; gap: 12px; padding: 8px 8px 8px 16px; min-height: 52px; }
.row-item__end { display: flex; align-items: center; gap: 4px; flex: 0 0 auto; }
.off { color: var(--pbc-error); font-weight: 600; }
.day { display: flex; align-items: center; gap: 12px; min-height: 48px; }
.day__name { width: 150px; }
.day__hour { width: 150px; }
.holiday-form { display: flex; flex-wrap: wrap; align-items: center; gap: 12px; margin-top: 16px; }
.holiday-form__note { flex: 1 1 220px; max-width: 320px; }
.dialog { width: min(94vw, 440px); }
</style>
