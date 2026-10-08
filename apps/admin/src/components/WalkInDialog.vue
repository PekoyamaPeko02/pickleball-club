<template>
  <q-dialog :model-value="modelValue" @update:model-value="emit('update:modelValue', $event)">
    <q-card class="dialog">
      <q-card-section>
        <h2 class="pbc-h3 dialog__title">Walk-in booking</h2>
        <p class="pbc-muted q-mb-none">For someone who phoned or is at the counter. Confirmed straight away.</p>
      </q-card-section>
      <q-form @submit.prevent="submit">
        <q-card-section class="form">
          <q-select v-model="form.courtId" outlined emit-value map-options :options="courtOptions" label="Court" />
          <q-input v-model="form.date" outlined type="date" label="Date" />
          <div class="row q-col-gutter-sm">
            <q-select v-model="form.startHour" class="col" outlined emit-value map-options :options="hourOptions" label="From" />
            <q-select v-model="form.hours" class="col" outlined emit-value map-options :options="lengthOptions" label="Length" />
          </div>
          <q-input v-model.trim="form.guestName" outlined label="Guest name" :rules="[required]" lazy-rules />
          <q-input v-model.trim="form.guestPhone" outlined type="tel" label="Guest phone" :rules="[required]" lazy-rules />
          <q-option-group v-model="form.payment" inline type="radio" :options="[{ label: 'Cash', value: 'cash' }, { label: 'Transfer', value: 'transfer' }]" />
          <p v-if="error" class="text-negative q-mb-none" role="alert">{{ error }}</p>
        </q-card-section>
        <q-card-actions align="right">
          <q-btn v-close-popup flat no-caps label="Close" />
          <q-btn unelevated no-caps type="submit" class="pbc-btn-ink" label="Create booking" :loading="busy" />
        </q-card-actions>
      </q-form>
    </q-card>
  </q-dialog>
</template>

<script setup lang="ts">
import { computed, reactive, ref, watch } from 'vue';
import type { ClubDate, ClubInfo, CounterPayment } from '@pbc/api';
import { formatHours } from '@pbc/ui';
import { useApi } from 'boot/api';
import { messageOf } from 'src/composables/errors';
import { maxHoursFrom, startHourOptions } from 'src/composables/hours';

defineOptions({ name: 'WalkInDialog' });

const props = defineProps<{ modelValue: boolean; club: ClubInfo; courtId: string; date: ClubDate; startHour: number }>();
const emit = defineEmits<{ 'update:modelValue': [open: boolean]; created: [] }>();

const form = reactive({ courtId: '', date: '' as ClubDate, startHour: 0, hours: 1, guestName: '', guestPhone: '', payment: 'cash' as CounterPayment });
const busy = ref(false);
const error = ref('');
const required = (v: string) => !!v || 'Required';

// Start from the cell that was clicked each time the dialog opens.
watch(() => props.modelValue, (open) => {
  if (!open) return;
  Object.assign(form, { courtId: props.courtId, date: props.date, startHour: props.startHour, hours: 1, guestName: '', guestPhone: '', payment: 'cash' });
  error.value = '';
});

const courtOptions = computed(() => props.club.courts.map((c) => ({ label: c.name, value: c.id })));
const hourOptions = computed(() => startHourOptions(props.club, form.date));
const lengthOptions = computed(() =>
  Array.from({ length: maxHoursFrom(props.club, form.date, form.startHour) }, (_, i) => ({ label: formatHours(i + 1), value: i + 1 })));

async function submit() {
  busy.value = true;
  error.value = '';
  try {
    await useApi().createWalkIn({ ...form });
    emit('created');
    emit('update:modelValue', false);
  } catch (e) {
    error.value = messageOf(e);
  } finally {
    busy.value = false;
  }
}
</script>

<style scoped>
.dialog { width: min(94vw, 440px); }
.dialog__title { margin: 0 0 4px; color: var(--pbc-ink); }
.form { display: grid; gap: 12px; padding-top: 0; }
</style>
