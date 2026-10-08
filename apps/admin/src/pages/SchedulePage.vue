<template>
  <q-page class="pbc-page schedule">
    <div class="toolbar">
      <h1 class="pbc-h3 page-title">Schedule</h1>
      <div class="toolbar__date">
        <q-btn flat round icon="chevron_left" aria-label="Previous day" :disable="!date" @click="shift(-1)" />
        <q-input v-model="date" dense outlined type="date" aria-label="Date" class="toolbar__picker" />
        <q-btn flat round icon="chevron_right" aria-label="Next day" :disable="!date" @click="shift(1)" />
        <q-btn flat no-caps label="Today" :disable="!club || date === club.today" @click="date = club!.today" />
      </div>
      <q-form class="toolbar__code" @submit.prevent="findByCode">
        <q-input v-model.trim="code" dense outlined label="Booking code" placeholder="PB10001">
          <template #append><q-btn flat dense round type="submit" icon="search" aria-label="Find the booking" :loading="finding" /></template>
        </q-input>
      </q-form>
    </div>

    <p v-if="date" class="q-mb-sm"><strong>{{ formatLongDate(date) }}</strong> <span class="pbc-muted">· Click a free hour to add a walk-in booking or block the court.</span></p>

    <q-banner v-if="error" class="text-negative" role="alert">
      {{ error }}
      <template #action><q-btn flat no-caps label="Try again" @click="load()" /></template>
    </q-banner>
    <q-skeleton v-else-if="!schedule" height="480px" />
    <template v-else>
      <ScheduleGrid :schedule="schedule" @free="onFree" @booking="openBooking" @block="onBlock" />

      <h2 class="pbc-label pbc-muted q-mt-lg">Bookings on this day ({{ schedule.bookings.length }})</h2>
      <p v-if="schedule.bookings.length === 0" class="pbc-muted">None yet.</p>
      <ul v-else class="list pbc-card">
        <li v-for="b in schedule.bookings" :key="b.id">
          <button type="button" class="list__row" @click="openBooking(b)">
            <span class="pbc-num list__time">{{ b.start }}–{{ b.end }}</span>
            <span class="list__court">{{ b.courtName }}</span>
            <span class="list__name">{{ b.customerName || '—' }} <span class="pbc-muted pbc-num">· {{ b.code }}</span></span>
            <span class="pbc-muted">{{ STATUS_LABEL[b.status] }}</span>
          </button>
        </li>
      </ul>
    </template>

    <!-- what to do with a free hour -->
    <q-dialog v-model="choosing">
      <q-card class="choose">
        <q-card-section>
          <h2 class="pbc-h3 q-ma-none">{{ target.courtName }} · {{ formatHour(target.startHour) }}</h2>
        </q-card-section>
        <q-card-actions vertical align="stretch">
          <q-btn unelevated no-caps class="pbc-btn-ink" icon="person_add" label="Walk-in booking" @click="choosing = false; walkIn = true" />
          <q-btn outline no-caps icon="block" label="Block the court" @click="choosing = false; blocking = true" />
          <q-btn v-close-popup flat no-caps label="Close" />
        </q-card-actions>
      </q-card>
    </q-dialog>

    <template v-if="club">
      <WalkInDialog v-model="walkIn" :club="club" :court-id="target.courtId" :date="date" :start-hour="target.startHour" @created="created('Walk-in booking created.')" />
      <BlockDialog v-model="blocking" :club="club" :court-id="target.courtId" :date="date" :start-hour="target.startHour" @created="created('Court blocked.')" />
      <BookingDialog v-model="showBooking" :club="club" :booking="current" @changed="onChanged" />
    </template>
  </q-page>
</template>

<script setup lang="ts">
import { computed, onMounted, reactive, ref, watch } from 'vue';
import { useQuasar } from 'quasar';
import type { AdminBooking, ClubDate, CourtBlock, Schedule, Slot } from '@pbc/api';
import { addDays, formatHour, formatLongDate, hourOf } from '@pbc/ui';
import { useApi } from 'boot/api';
import BlockDialog from 'components/BlockDialog.vue';
import BookingDialog from 'components/BookingDialog.vue';
import ScheduleGrid from 'components/ScheduleGrid.vue';
import WalkInDialog from 'components/WalkInDialog.vue';
import { STATUS_LABEL } from 'src/composables/bookings';
import { messageOf } from 'src/composables/errors';
import { useClubStore } from 'stores/club';

defineOptions({ name: 'SchedulePage' });

const $q = useQuasar();
const store = useClubStore();
const club = computed(() => store.club);

const date = ref<ClubDate>('');
const schedule = ref<Schedule | null>(null);
const error = ref('');

const shift = (days: number) => { date.value = addDays(date.value, days); };

/** @param quiet Refresh in place (after an action) instead of showing the skeleton. */
async function load(quiet = false) {
  if (!date.value) return;
  const requested = date.value;
  if (!quiet) schedule.value = null;
  error.value = '';
  try {
    const result = await useApi().getSchedule(requested);
    if (requested === date.value) schedule.value = result;
  } catch (e) {
    if (requested === date.value) error.value = messageOf(e);
  }
}

// ---- a free hour
const target = reactive({ courtId: '', courtName: '', startHour: 0 });
const choosing = ref(false);
const walkIn = ref(false);
const blocking = ref(false);

function onFree(courtId: string, slot: Slot) {
  Object.assign(target, { courtId, courtName: club.value?.courts.find((c) => c.id === courtId)?.name ?? '', startHour: hourOf(slot.start) });
  choosing.value = true;
}

function created(message: string) {
  $q.notify({ type: 'positive', message });
  void load(true);
}

// ---- a booking
const current = ref<AdminBooking | null>(null);
const showBooking = ref(false);
function openBooking(b: AdminBooking) {
  current.value = b;
  showBooking.value = true;
}
function onChanged(b: AdminBooking) {
  current.value = b;
  $q.notify({ type: 'positive', message: `${b.code}: ${STATUS_LABEL[b.status].toLowerCase()}.` });
  if (b.date !== date.value) date.value = b.date; // moved to another day: follow it
  else void load(true);
}

const code = ref('');
const finding = ref(false);
async function findByCode() {
  if (!code.value) return;
  finding.value = true;
  try {
    const b = await useApi().getAdminBookingByCode(code.value);
    code.value = '';
    if (b.date !== date.value) date.value = b.date;
    openBooking(b);
  } catch (e) {
    $q.notify({ type: 'negative', message: messageOf(e) });
  } finally {
    finding.value = false;
  }
}

// ---- a block
function onBlock(block: CourtBlock) {
  $q.dialog({
    title: `${block.courtName} is blocked ${block.start}–${block.end}`,
    message: block.reason,
    ok: { label: 'Remove the block', color: 'negative', flat: true, noCaps: true },
    cancel: { label: 'Keep it', flat: true, noCaps: true },
  }).onOk(async () => {
    try {
      await useApi().deleteBlock(block.id);
      $q.notify({ type: 'positive', message: 'Block removed.' });
    } catch (e) {
      $q.notify({ type: 'negative', message: messageOf(e) });
    }
    void load(true);
  });
}

watch(date, () => void load());
onMounted(async () => {
  const info = await store.load();
  if (info) date.value = info.today;
  else error.value = store.error;
});
</script>

<style scoped>
.schedule { max-width: 1280px; }
.page-title { margin: 0; color: var(--pbc-ink); }
.toolbar { display: flex; flex-wrap: wrap; align-items: center; gap: 12px 24px; margin-bottom: 12px; }
.toolbar__date { display: flex; align-items: center; gap: 4px; }
.toolbar__picker { width: 170px; }
.toolbar__code { margin-left: auto; width: 220px; }
.list { list-style: none; margin: 8px 0 0; padding: 0; overflow: hidden; }
.list li + li { border-top: 1px solid var(--pbc-line); }
.list__row {
  display: grid; grid-template-columns: 110px 90px 1fr auto; gap: 12px; align-items: center; width: 100%;
  padding: 10px 16px; background: none; border: 0; font: inherit; text-align: left; cursor: pointer; color: var(--pbc-text);
}
.list__row:hover { background: var(--pbc-surface-2); }
.list__row:focus-visible { outline: 2px solid var(--pbc-ink); outline-offset: -2px; }
.list__time { font-weight: 600; }
.list__name { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.choose { width: min(92vw, 320px); }
@media (max-width: 599px) { .list__row { grid-template-columns: 1fr 1fr; } .toolbar__code { margin-left: 0; width: 100%; } }
</style>
