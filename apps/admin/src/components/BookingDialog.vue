<template>
  <q-dialog :model-value="modelValue" position="right" full-height @update:model-value="emit('update:modelValue', $event)">
    <q-card v-if="booking" class="panel">
      <q-card-section class="row items-start no-wrap">
        <div>
          <div class="pbc-label pbc-muted">{{ booking.source === 'walk_in' ? 'Walk-in booking' : 'Online booking' }}</div>
          <h2 class="pbc-h3 panel__title pbc-num">{{ booking.code }}</h2>
          <span class="chip" :class="`chip--${booking.status}`">{{ STATUS_LABEL[booking.status] }}</span>
        </div>
        <q-space />
        <q-btn v-close-popup flat round icon="close" aria-label="Close" />
      </q-card-section>

      <q-card-section>
        <dl class="facts">
          <dt>Court</dt><dd>{{ booking.courtName }}</dd>
          <dt>When</dt><dd>{{ whenOf(booking) }}</dd>
          <dt>Name</dt><dd>{{ booking.customerName || '—' }}</dd>
          <dt>Phone</dt><dd><a v-if="booking.customerPhone" :href="`tel:${booking.customerPhone}`">{{ booking.customerPhone }}</a><span v-else>—</span></dd>
          <template v-if="booking.customerEmail"><dt>Email</dt><dd><a :href="`mailto:${booking.customerEmail}`">{{ booking.customerEmail }}</a></dd></template>
          <dt>Total</dt><dd class="pbc-num">{{ formatBaht(booking.total) }}</dd>
          <dt>Payment</dt>
          <dd>{{ booking.paymentMethod ? PAYMENT_LABEL[booking.paymentMethod] : booking.paymentState === 'pending' ? 'Not paid yet' : '—' }}</dd>
          <template v-if="booking.rescheduled"><dt>Moved</dt><dd>Once, by the customer</dd></template>
          <template v-if="booking.refundNote"><dt>Refund</dt><dd>{{ booking.refundNote }}</dd></template>
        </dl>
      </q-card-section>

      <q-card-section v-if="error" class="q-pt-none"><p class="text-negative q-mb-none" role="alert">{{ error }}</p></q-card-section>

      <!-- move -->
      <q-card-section v-if="mode === 'move'" class="form">
        <h3 class="pbc-label pbc-muted">Move to</h3>
        <q-select v-model="move.courtId" outlined emit-value map-options :options="courtOptions" label="Court" />
        <q-input v-model="move.date" outlined type="date" label="Date" />
        <q-select v-model="move.startHour" outlined emit-value map-options :options="hourOptions" label="From" :hint="`Stays ${formatHours(booking.hours)} long.`" />
        <div class="row q-gutter-sm">
          <q-btn unelevated no-caps class="pbc-btn-ink" label="Move the booking" :loading="busy" @click="run(() => api.adminMove(booking!.id, { ...move }))" />
          <q-btn flat no-caps label="Back" @click="mode = 'view'" />
        </div>
      </q-card-section>

      <!-- cancel -->
      <q-card-section v-else-if="mode === 'cancel'" class="form">
        <h3 class="pbc-label pbc-muted">Cancel this booking</h3>
        <p class="pbc-muted q-mb-none">The system does not send money back. Refund the customer yourself, then write down how.</p>
        <q-input v-model.trim="refundNote" outlined type="textarea" rows="3" label="How was it refunded?" hint="For example: transferred ฿900 to KBank ••1234." />
        <div class="row q-gutter-sm">
          <q-btn unelevated no-caps color="negative" label="Cancel the booking" :disable="!refundNote" :loading="busy" @click="run(() => api.adminCancel(booking!.id, refundNote))" />
          <q-btn flat no-caps label="Back" @click="mode = 'view'" />
        </div>
      </q-card-section>

      <!-- actions -->
      <q-card-section v-else class="actions">
        <template v-if="booking.status === 'confirmed'">
          <q-btn unelevated no-caps class="pbc-btn-ink" icon="how_to_reg" label="Check in" :loading="busy" @click="run(() => api.checkIn(booking!.id))" />
          <q-btn outline no-caps icon="swap_horiz" label="Move" @click="startMove" />
          <q-btn outline no-caps icon="person_off" label="Mark as no-show" :loading="busy" @click="run(() => api.markNoShow(booking!.id))" />
          <q-btn outline no-caps color="negative" icon="cancel" label="Cancel" @click="mode = 'cancel'" />
        </template>
        <template v-else-if="booking.status === 'checked_in'">
          <q-btn outline no-caps color="negative" icon="cancel" label="Cancel" @click="mode = 'cancel'" />
        </template>
        <p v-else-if="booking.status === 'held'" class="pbc-muted q-mb-none">The customer is paying. If no payment arrives, the court goes back on sale by itself.</p>
      </q-card-section>
    </q-card>
  </q-dialog>
</template>

<script setup lang="ts">
import { computed, reactive, ref, watch } from 'vue';
import type { AdminBooking, ClubDate, ClubInfo } from '@pbc/api';
import { formatBaht, formatHours } from '@pbc/ui';
import { useApi } from 'boot/api';
import { PAYMENT_LABEL, STATUS_LABEL, whenOf } from 'src/composables/bookings';
import { messageOf } from 'src/composables/errors';
import { startHourOptions } from 'src/composables/hours';

defineOptions({ name: 'BookingDialog' });

const props = defineProps<{ modelValue: boolean; club: ClubInfo; booking: AdminBooking | null }>();
const emit = defineEmits<{ 'update:modelValue': [open: boolean]; changed: [booking: AdminBooking] }>();

const api = useApi();
const mode = ref<'view' | 'move' | 'cancel'>('view');
const busy = ref(false);
const error = ref('');
const refundNote = ref('');
const move = reactive({ courtId: '', date: '' as ClubDate, startHour: 0 });

watch(() => [props.modelValue, props.booking?.id], () => {
  mode.value = 'view';
  error.value = '';
  refundNote.value = '';
});

const courtOptions = computed(() => props.club.courts.map((c) => ({ label: c.name, value: c.id })));
const hourOptions = computed(() => startHourOptions(props.club, move.date));

function startMove() {
  const b = props.booking!;
  Object.assign(move, { courtId: b.courtId, date: b.date, startHour: Number(b.start.slice(0, 2)) });
  mode.value = 'move';
}

async function run(call: () => Promise<AdminBooking>) {
  busy.value = true;
  error.value = '';
  try {
    emit('changed', await call());
    mode.value = 'view';
  } catch (e) {
    error.value = messageOf(e);
  } finally {
    busy.value = false;
  }
}
</script>

<style scoped>
.panel { width: min(100vw, 400px); }
.panel__title { margin: 2px 0 8px; color: var(--pbc-ink); font-size: 1.6rem; }
.facts { display: grid; grid-template-columns: auto 1fr; gap: 8px 16px; margin: 0; }
.facts dt { color: var(--pbc-text-2); }
.facts dd { margin: 0; text-align: right; font-weight: 500; overflow-wrap: anywhere; }
.form { display: grid; gap: 12px; }
.form h3 { margin: 0; }
.actions { display: grid; gap: 8px; }
.chip { display: inline-block; padding: 2px 10px; border-radius: 999px; font-size: 0.75rem; font-weight: 600; background: var(--pbc-surface-2); color: var(--pbc-text-2); }
.chip--held { background: var(--pbc-brass-surface); color: var(--pbc-brass-ink); }
.chip--confirmed, .chip--checked_in { background: var(--pbc-positive-surface); color: var(--pbc-positive); }
.chip--cancelled, .chip--no_show { background: #fbeae8; color: var(--pbc-error); }
</style>
