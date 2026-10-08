<template>
  <q-page class="pbc-page booking">
    <q-banner v-if="error" class="text-negative" role="alert">
      {{ error }}
      <template #action><q-btn flat no-caps label="Try again" @click="load" /></template>
    </q-banner>
    <q-skeleton v-else-if="!booking" height="360px" />

    <template v-else>
      <!-- waiting for payment -->
      <template v-if="booking.status === 'held'">
        <h1 class="pbc-h2 page-title">Pay to confirm</h1>
        <p class="pbc-muted">
          We are holding {{ booking.courtName }} for you.
          <span aria-live="polite">Time left: <strong class="pbc-num">{{ timeLeft }}</strong></span>
        </p>

        <section class="pbc-card panel pay">
          <template v-if="booking.payment?.method === 'promptpay'">
            <img v-if="qr" :src="qr" width="240" height="240" class="pay__qr" alt="PromptPay QR code for this payment">
            <p class="pay__amount pbc-num">{{ formatBaht(booking.total) }}</p>
            <p class="pbc-muted">Scan with your banking app. This page updates by itself once the payment arrives.</p>
          </template>
          <template v-else>
            <p class="pay__amount pbc-num">{{ formatBaht(booking.total) }}</p>
            <q-btn v-if="booking.payment?.checkoutUrl" unelevated no-caps size="lg" class="pbc-btn-brass" :href="booking.payment.checkoutUrl" label="Pay by card" />
            <p v-else class="pbc-muted">The card payment page is not connected yet.</p>
          </template>

          <div class="pay__actions">
            <q-btn flat no-caps :label="booking.payment?.method === 'promptpay' ? 'Pay by card instead' : 'Pay by PromptPay QR instead'" :loading="switching" @click="switchMethod" />
            <q-btn flat no-caps color="negative" label="Cancel this booking" :loading="releasing" @click="release" />
          </div>
          <q-btn v-if="isDev" outline no-caps color="grey-8" class="q-mt-md" label="Simulate payment (development only)" :loading="simulating" @click="simulate" />
        </section>
      </template>

      <!-- paid -->
      <template v-else-if="PAID.includes(booking.status)">
        <h1 class="pbc-h2 page-title">{{ booking.status === 'confirmed' ? 'You are booked' : STATUS_LABEL[booking.status] }}</h1>
        <p v-if="booking.status === 'confirmed'" class="pbc-muted">Show this code at the club when you arrive.</p>
        <section class="pbc-card panel">
          <p class="code pbc-num" aria-label="Booking code">{{ booking.code }}</p>
        </section>
        <section v-if="booking.status === 'confirmed' && booking.source === 'online'" class="pbc-card panel">
          <h2 class="pbc-h3 move__title">Plans changed?</h2>
          <template v-if="canMove">
            <p class="pbc-muted">
              You can move this booking once, to a time of the same length that costs the same or less, until
              <strong>{{ formatClubDateTime(booking.rescheduleUntil!, timezone) }}</strong>.
            </p>
            <q-btn outline no-caps :to="{ name: 'reschedule', params: { id: booking.id } }" label="Move this booking" />
          </template>
          <p v-else class="pbc-muted q-mb-none">
            {{ booking.rescheduled ? 'This booking has already been moved once, so it cannot be moved again.' : 'It is too close to the start time to move this booking.' }}
            Bookings are non-refundable.
          </p>
        </section>
      </template>

      <!-- no longer a booking -->
      <template v-else>
        <h1 class="pbc-h2 page-title">{{ STATUS_LABEL[booking.status] }}</h1>
        <p class="pbc-muted">
          {{ booking.status === 'expired' ? 'The court was not paid for within 10 minutes, so it has gone back on sale.' : 'The club has cancelled this booking and will refund you directly.' }}
        </p>
        <q-btn unelevated no-caps class="pbc-btn-brass q-mb-md" :to="{ name: 'book' }" label="Book a court" />
      </template>

      <section class="pbc-card panel">
        <dl class="summary">
          <dt>Court</dt><dd>{{ booking.courtName }}</dd>
          <dt>Date</dt><dd>{{ formatLongDate(booking.date) }}</dd>
          <dt>Time</dt><dd class="pbc-num">{{ booking.start }}–{{ booking.end }} ({{ formatHours(booking.hours) }})</dd>
          <dt>Total</dt><dd class="pbc-num">{{ formatBaht(booking.total) }}</dd>
          <dt>Status</dt><dd>{{ STATUS_LABEL[booking.status] }}</dd>
        </dl>
      </section>

      <p><router-link :to="{ name: 'account' }">All my bookings</router-link></p>
    </template>
  </q-page>
</template>

<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue';
import { useRoute } from 'vue-router';
import { useQuasar } from 'quasar';
import QRCode from 'qrcode';
import type { Booking, BookingStatus } from '@pbc/api';
import { formatBaht, formatClubDateTime, formatHours, formatLongDate } from '@pbc/ui';
import { useApi } from 'boot/api';
import { useClubStore } from 'stores/club';
import { STATUS_LABEL } from 'src/composables/bookings';
import { messageOf } from 'src/composables/errors';

defineOptions({ name: 'BookingPage' });

const PAID: BookingStatus[] = ['confirmed', 'checked_in', 'completed', 'no_show'];
const isDev = process.env.DEV;

const $q = useQuasar();
const route = useRoute();
const id = computed(() => String(route.params.id));
const booking = ref<Booking | null>(null);
const error = ref('');
const qr = ref('');
const now = ref(Date.now());

async function load() {
  try {
    booking.value = await useApi().getBooking(id.value);
    error.value = '';
  } catch (e) {
    if (!booking.value) error.value = messageOf(e); // a failed refresh keeps showing what we have
  }
}

// While the Booking is held: tick the countdown every second and ask the server every 3 seconds whether the money arrived.
let ticker: ReturnType<typeof setInterval> | undefined;
let poller: ReturnType<typeof setInterval> | undefined;
function stopTimers() {
  clearInterval(ticker);
  clearInterval(poller);
  ticker = poller = undefined;
}
watch(() => booking.value?.status, (status) => {
  stopTimers();
  if (status !== 'held') return;
  ticker = setInterval(() => { now.value = Date.now(); }, 1000);
  poller = setInterval(() => void load(), 3000);
}, { immediate: true });
onBeforeUnmount(stopTimers);

const clubStore = useClubStore();
const timezone = computed(() => clubStore.club?.timezone ?? 'Asia/Bangkok');
const canMove = computed(() => !!booking.value?.rescheduleUntil && Date.parse(booking.value.rescheduleUntil) > now.value);

const timeLeft = computed(() => {
  const until = booking.value?.holdExpiresAt ? Date.parse(booking.value.holdExpiresAt) : 0;
  const seconds = Math.max(0, Math.floor((until - now.value) / 1000));
  return `${Math.floor(seconds / 60)}:${String(seconds % 60).padStart(2, '0')}`;
});

// The payment provider sends either the QR's text (drawn here) or a ready-made picture of it.
watch(() => booking.value?.payment?.qrPayload, async (payload) => {
  if (!payload) qr.value = '';
  else if (payload.startsWith('data:image/')) qr.value = payload;
  else qr.value = await QRCode.toDataURL(payload, { width: 480, margin: 1 });
}, { immediate: true });

function act(flag: { value: boolean }, call: () => Promise<Booking>) {
  return async () => {
    flag.value = true;
    try {
      booking.value = await call();
    } catch (e) {
      $q.notify({ type: 'negative', message: messageOf(e) });
      await load();
    } finally {
      flag.value = false;
    }
  };
}

const switching = ref(false);
const releasing = ref(false);
const simulating = ref(false);
const switchMethod = act(switching, () => useApi().newPayment(id.value, booking.value?.payment?.method === 'promptpay' ? 'card' : 'promptpay'));
const release = act(releasing, () => useApi().releaseHold(id.value));
const simulate = act(simulating, () => useApi().devSimulatePayment(id.value));

onMounted(() => {
  void load();
  void clubStore.load();
});
watch(id, () => {
  booking.value = null;
  void load();
});
</script>

<style scoped>
.booking { max-width: 560px; }
.page-title { margin: 0 0 8px; color: var(--pbc-ink); }
.panel { padding: 20px; margin-bottom: 16px; }
.pay { text-align: center; }
.pay__qr { display: block; margin: 0 auto 8px; border-radius: var(--pbc-r-sm); }
.pay__amount { font-family: var(--pbc-font-display); font-size: 2rem; color: var(--pbc-ink); margin: 8px 0; }
.pay__actions { display: flex; flex-wrap: wrap; justify-content: center; gap: 8px; margin-top: 8px; }
.move__title { margin: 0 0 8px; color: var(--pbc-ink); }
.code { font-family: var(--pbc-font-display); font-size: 2.5rem; letter-spacing: 0.04em; text-align: center; margin: 0; color: var(--pbc-ink); }
.summary { display: grid; grid-template-columns: auto 1fr; gap: 8px 24px; margin: 0; }
.summary dt { color: var(--pbc-text-2); }
.summary dd { margin: 0; text-align: right; font-weight: 500; }
</style>
