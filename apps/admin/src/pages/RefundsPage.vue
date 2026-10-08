<template>
  <q-page class="pbc-page">
    <div class="row items-center q-mb-sm">
      <h1 class="pbc-h3 page-title">Refunds to make</h1>
      <q-space />
      <q-toggle v-model="showAll" label="Show finished ones too" />
    </div>
    <p class="pbc-muted lead">
      Payments that arrived but could not buy the booking — usually because the 10-minute hold had run out and someone else took the court.
      The system never sends money back: refund the customer yourself, then record it here.
    </p>

    <q-banner v-if="error" class="text-negative" role="alert">
      {{ error }}
      <template #action><q-btn flat no-caps label="Try again" @click="load" /></template>
    </q-banner>
    <q-skeleton v-else-if="!rows" height="160px" />
    <p v-else-if="rows.length === 0" class="pbc-card empty">Nothing to refund.</p>
    <ul v-else class="list">
      <li v-for="r in rows" :key="r.paymentId" class="pbc-card item">
        <div class="item__main">
          <div class="item__amount pbc-num">{{ formatBaht(r.amount) }}</div>
          <div>
            <strong>{{ r.customerName || '—' }}</strong>
            <span class="pbc-muted"> · booking <span class="pbc-num">{{ r.bookingCode }}</span></span>
          </div>
          <div class="pbc-muted">
            <a v-if="r.customerPhone" :href="`tel:${r.customerPhone}`">{{ r.customerPhone }}</a>
            <template v-if="r.customerPhone && r.customerEmail"> · </template>
            <a v-if="r.customerEmail" :href="`mailto:${r.customerEmail}`">{{ r.customerEmail }}</a>
          </div>
          <div class="pbc-muted">
            Paid by {{ r.method === 'promptpay' ? 'PromptPay' : 'card' }}<template v-if="r.paidAt"> on {{ formatClubDateTime(r.paidAt, timezone) }}</template>
            · {{ reasonOf(r.reason) }}
          </div>
          <div v-if="r.refundedAt" class="item__done">Refunded {{ formatClubDateTime(r.refundedAt, timezone) }} — {{ r.refundNote }}</div>
        </div>
        <q-btn v-if="!r.refundedAt" unelevated no-caps class="pbc-btn-ink" label="Mark as refunded" @click="open(r)" />
      </li>
    </ul>

    <q-dialog v-model="dialog">
      <q-card class="dialog">
        <q-card-section>
          <h2 class="pbc-h3 q-ma-none">Mark as refunded</h2>
          <p v-if="current" class="pbc-muted q-mb-none">{{ formatBaht(current.amount) }} to {{ current.customerName }} ({{ current.bookingCode }})</p>
        </q-card-section>
        <q-card-section class="q-pt-none">
          <q-input v-model.trim="note" outlined type="textarea" rows="3" label="How was it refunded?" hint="For example: PromptPay to 081-555-6666." />
          <p v-if="saveError" class="text-negative q-mt-sm q-mb-none" role="alert">{{ saveError }}</p>
        </q-card-section>
        <q-card-actions align="right">
          <q-btn v-close-popup flat no-caps label="Close" />
          <q-btn unelevated no-caps class="pbc-btn-ink" label="Save" :disable="!note" :loading="saving" @click="save" />
        </q-card-actions>
      </q-card>
    </q-dialog>
  </q-page>
</template>

<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue';
import { useQuasar } from 'quasar';
import type { RefundDue } from '@pbc/api';
import { formatBaht, formatClubDateTime } from '@pbc/ui';
import { useApi } from 'boot/api';
import { messageOf } from 'src/composables/errors';
import { useClubStore } from 'stores/club';

defineOptions({ name: 'RefundsPage' });

const REASONS: Record<string, string> = {
  court_taken: 'The hold had run out and someone else booked the court.',
  court_blocked: 'The hold had run out and the court was blocked.',
  time_passed: 'The payment arrived after the booked time.',
  already_confirmed: 'The booking had already been paid (paid twice).',
};
const reasonOf = (reason: string | null) => (reason && REASONS[reason]) || 'The booking was no longer waiting for payment.';

const $q = useQuasar();
const clubStore = useClubStore();
const timezone = computed(() => clubStore.club?.timezone ?? 'Asia/Bangkok');

const showAll = ref(false);
const rows = ref<RefundDue[] | null>(null);
const error = ref('');

async function load() {
  error.value = '';
  try {
    rows.value = await useApi().getRefunds(showAll.value);
  } catch (e) {
    error.value = messageOf(e);
  }
}

const dialog = ref(false);
const current = ref<RefundDue | null>(null);
const note = ref('');
const saving = ref(false);
const saveError = ref('');

function open(r: RefundDue) {
  current.value = r;
  note.value = '';
  saveError.value = '';
  dialog.value = true;
}

async function save() {
  if (!current.value) return;
  saving.value = true;
  saveError.value = '';
  try {
    await useApi().markRefunded(current.value.paymentId, note.value);
    dialog.value = false;
    $q.notify({ type: 'positive', message: 'Recorded.' });
    await load();
  } catch (e) {
    saveError.value = messageOf(e);
  } finally {
    saving.value = false;
  }
}

watch(showAll, load);
onMounted(() => {
  void load();
  void clubStore.load();
});
</script>

<style scoped>
.page-title { margin: 0; color: var(--pbc-ink); }
.lead { max-width: 70ch; }
.empty { padding: 24px; color: var(--pbc-text-2); }
.list { list-style: none; margin: 0; padding: 0; display: grid; gap: 12px; }
.item { display: flex; flex-wrap: wrap; align-items: center; justify-content: space-between; gap: 16px; padding: 16px 20px; }
.item__main { display: grid; gap: 2px; }
.item__amount { font-family: var(--pbc-font-display); font-size: 1.5rem; color: var(--pbc-ink); }
.item__done { color: var(--pbc-positive); font-weight: 500; }
.dialog { width: min(94vw, 440px); }
</style>
