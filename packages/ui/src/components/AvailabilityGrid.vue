<template>
  <div class="grid-scroll">
    <table class="grid" :aria-label="`Court availability`">
      <thead>
        <tr>
          <th scope="col" class="grid__time">Time</th>
          <th v-for="court in availability.courts" :key="court.courtId" scope="col">
            <div class="grid__court">{{ court.name }}</div>
            <div class="grid__meta">{{ court.indoor ? 'Indoor' : 'Outdoor' }}</div>
          </th>
        </tr>
      </thead>
      <tbody>
        <tr v-for="(row, i) in rows" :key="row.start">
          <th scope="row" class="grid__time pbc-num">{{ row.start }}</th>
          <td v-for="court in availability.courts" :key="court.courtId">
            <component
              :is="canSelect(court.slots[i]) ? 'button' : 'div'"
              v-if="court.slots[i]"
              class="slot"
              :class="[`slot--${court.slots[i]!.status}`, { 'slot--selected': isSelected(court.courtId, court.slots[i]!) }]"
              :type="canSelect(court.slots[i]) ? 'button' : undefined"
              :aria-pressed="canSelect(court.slots[i]) ? isSelected(court.courtId, court.slots[i]!) : undefined"
              :aria-label="`${court.name}, ${court.slots[i]!.start} to ${court.slots[i]!.end}, ${describe(court.slots[i]!)}`"
              @click="canSelect(court.slots[i]) && emit('select', court.courtId, court.slots[i]!)"
            >
              <span v-if="court.slots[i]!.status === 'available' && court.slots[i]!.price !== null" class="pbc-num">
                {{ formatBaht(court.slots[i]!.price!) }}
              </span>
              <span v-else>{{ STATUS_LABEL[court.slots[i]!.status] }}</span>
            </component>
          </td>
        </tr>
        <tr v-if="rows.length === 0">
          <td :colspan="availability.courts.length + 1" class="grid__empty">Closed on this day.</td>
        </tr>
      </tbody>
    </table>
  </div>
</template>

<script setup lang="ts">
import { computed } from 'vue';
import type { Availability, Slot, SlotStatus } from '@pbc/api';
import { formatBaht } from '../format';

defineOptions({ name: 'AvailabilityGrid' });

const props = withDefaults(
  defineProps<{
    availability: Availability;
    /** When true, `available` Slots are buttons and emit `select`. */
    selectable?: boolean;
    /** Keys of the selected Slots: `${courtId}|${startAt}`. */
    selected?: string[];
  }>(),
  { selectable: false, selected: () => [] },
);

const emit = defineEmits<{ select: [courtId: string, slot: Slot] }>();

const STATUS_LABEL: Record<SlotStatus, string> = {
  available: 'Available',
  booked: 'Booked',
  held: 'On hold',
  blocked: 'Unavailable',
  past: '—',
  closed: 'Closed',
};

// Every Court has the same Slots on a day (same Operating Hours), so the first Court gives the rows.
const rows = computed(() => props.availability.courts[0]?.slots ?? []);

const canSelect = (slot: Slot | undefined) => props.selectable && slot?.status === 'available';
const isSelected = (courtId: string, slot: Slot) => props.selected.includes(`${courtId}|${slot.startAt}`);
const describe = (slot: Slot) =>
  slot.status === 'available' && slot.price !== null ? `available, ${formatBaht(slot.price)}` : STATUS_LABEL[slot.status].replace('—', 'past');
</script>

<style scoped>
.grid-scroll { overflow-x: auto; border: 1px solid var(--pbc-line); border-radius: var(--pbc-r-lg); background: var(--pbc-surface); }
.grid { width: 100%; min-width: 520px; border-collapse: separate; border-spacing: 0; }
.grid th, .grid td { padding: 4px; text-align: center; }
.grid thead th { position: sticky; top: 0; background: var(--pbc-surface); padding: 12px 4px; border-bottom: 1px solid var(--pbc-line); z-index: 1; }
.grid__court { font-family: var(--pbc-font-display); font-size: 1.05rem; font-weight: 500; }
.grid__meta { font-size: 0.75rem; color: var(--pbc-text-2); font-weight: 400; }
.grid__time { width: 72px; font-size: 0.85rem; font-weight: 500; color: var(--pbc-text-2); position: sticky; left: 0; background: var(--pbc-surface); }
.grid__empty { padding: 32px 16px; color: var(--pbc-text-2); }

.slot {
  display: flex; align-items: center; justify-content: center;
  width: 100%; min-height: 40px; padding: 0 8px;
  border: 1px solid transparent; border-radius: var(--pbc-r-sm);
  font: inherit; font-size: 0.85rem;
}
.slot--available { background: var(--pbc-positive-surface); color: var(--pbc-positive); font-weight: 600; }
button.slot--available { cursor: pointer; }
button.slot--available:hover { border-color: var(--pbc-positive); }
button.slot:focus-visible { outline: 2px solid var(--pbc-ink); outline-offset: 2px; }
.slot--selected { background: var(--pbc-ink) !important; color: #fff !important; }
.slot--booked, .slot--blocked, .slot--closed { background: var(--pbc-surface-2); color: var(--pbc-text-3); }
.slot--held { background: var(--pbc-brass-surface); color: var(--pbc-brass-ink); }
.slot--past { color: var(--pbc-text-3); }
</style>
