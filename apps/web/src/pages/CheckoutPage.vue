<template>
  <q-page class="pbc-page checkout">
    <h1 class="pbc-h2 page-title">Review and pay</h1>

    <template v-if="selection">
      <section class="pbc-card panel">
        <dl class="summary">
          <dt>Court</dt><dd>{{ selection.courtName }}</dd>
          <dt>Date</dt><dd>{{ formatLongDate(selection.date) }}</dd>
          <dt>Time</dt><dd class="pbc-num">{{ formatHour(selection.startHour) }}–{{ formatHour(selection.startHour + selection.hours) }} ({{ formatHours(selection.hours) }})</dd>
          <dt class="summary__total">Total</dt><dd class="summary__total pbc-num">{{ formatBaht(selection.total) }}</dd>
        </dl>
        <router-link :to="{ name: 'book' }">Change</router-link>
      </section>

      <section v-if="user && !user.profileComplete" class="pbc-card panel">
        <h2 class="pbc-h3">Your details</h2>
        <p class="pbc-muted">The club needs a name and a phone number for the booking.</p>
        <div class="form">
          <q-input v-model.trim="profile.displayName" outlined label="Name" autocomplete="name" />
          <q-input v-model.trim="profile.phone" outlined type="tel" label="Phone number" autocomplete="tel" />
        </div>
      </section>

      <section class="pbc-card panel">
        <h2 class="pbc-h3">How would you like to pay?</h2>
        <q-option-group v-model="method" :options="METHODS" type="radio" />
      </section>

      <p class="pbc-muted terms">
        Bookings are non-refundable. You can move a booking once, at least {{ club?.rescheduleNoticeHours ?? 48 }} hours before you play.
        We hold the court for 10 minutes while you pay.
      </p>

      <q-banner v-if="error" class="notice notice--error" role="alert">
        {{ error }}
        <template v-if="errorAction" #action>
          <q-btn flat no-caps :label="errorAction.label" :to="errorAction.to" />
        </template>
      </q-banner>

      <q-btn unelevated no-caps size="lg" class="pbc-btn-brass full-width" :label="`Pay ${formatBaht(selection.total)}`" :loading="busy" @click="pay" />
    </template>

    <template v-else>
      <p class="pbc-muted">You have not chosen a time yet.</p>
      <q-btn unelevated no-caps class="pbc-btn-ink" :to="{ name: 'book' }" label="Choose a time" />
    </template>
  </q-page>
</template>

<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue';
import { useRouter, type RouteLocationRaw } from 'vue-router';
import { ApiError, type PaymentMethod } from '@pbc/api';
import { formatBaht, formatHour, formatHours, formatLongDate } from '@pbc/ui';
import { useApi } from 'boot/api';
import { messageOf } from 'src/composables/errors';
import { useAuthStore } from 'stores/auth';
import { useCartStore } from 'stores/cart';
import { useClubStore } from 'stores/club';

defineOptions({ name: 'CheckoutPage' });

const METHODS: { label: string; value: PaymentMethod }[] = [
  { label: 'PromptPay QR — scan with your banking app', value: 'promptpay' },
  { label: 'Credit or debit card', value: 'card' },
];

const router = useRouter();
const auth = useAuthStore();
const cart = useCartStore();
const clubStore = useClubStore();
const club = computed(() => clubStore.club);
const user = computed(() => auth.user);
const selection = computed(() => cart.selection);

const profile = reactive({ displayName: user.value?.displayName ?? '', phone: user.value?.phone ?? '' });
const method = ref<PaymentMethod>('promptpay');
const busy = ref(false);
const error = ref('');
const errorAction = ref<{ label: string; to: RouteLocationRaw } | null>(null);

// One key per visit to this page: pressing "Pay" twice, or a retry after a dropped connection, cannot book twice.
const idempotencyKey = crypto.randomUUID();

async function pay() {
  const s = selection.value;
  if (!s) return;
  busy.value = true;
  error.value = '';
  errorAction.value = null;
  try {
    if (user.value && !user.value.profileComplete) auth.setUser(await useApi().updateProfile({ ...profile }));
    const booking = await useApi().createBooking(
      { courtId: s.courtId, date: s.date, startHour: s.startHour, hours: s.hours, method: method.value }, idempotencyKey);
    cart.set(null);
    await router.replace({ name: 'booking', params: { id: booking.id } });
  } catch (e) {
    error.value = messageOf(e);
    if (e instanceof ApiError) {
      if (['slot_taken', 'slot_blocked', 'slot_in_past', 'outside_booking_window'].includes(e.code))
        errorAction.value = { label: 'Choose another time', to: { name: 'book' } };
      else if (e.code === 'open_hold_exists') errorAction.value = { label: 'See my bookings', to: { name: 'account' } };
    }
  } finally {
    busy.value = false;
  }
}

onMounted(() => clubStore.load());
</script>

<style scoped>
.checkout { max-width: 560px; }
.page-title { margin: 0 0 16px; color: var(--pbc-ink); }
.panel { padding: 20px; margin-bottom: 16px; }
.panel h2 { margin: 0 0 8px; color: var(--pbc-ink); }
.summary { display: grid; grid-template-columns: auto 1fr; gap: 8px 24px; margin: 0 0 12px; }
.summary dt { color: var(--pbc-text-2); }
.summary dd { margin: 0; text-align: right; font-weight: 500; }
.summary__total { padding-top: 12px; border-top: 1px solid var(--pbc-line); font-size: 1.15rem; }
dd.summary__total { font-family: var(--pbc-font-display); font-size: 1.5rem; color: var(--pbc-ink); }
.form { display: grid; gap: 12px; }
.terms { font-size: 0.9rem; line-height: 1.6; }
.notice { border-radius: var(--pbc-r-lg); margin-bottom: 16px; }
.notice--error { background: #fbeae8; color: var(--pbc-error); }
</style>
