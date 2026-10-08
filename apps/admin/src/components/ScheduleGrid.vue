<template>
  <div class="grid-scroll">
    <table class="grid" aria-label="Schedule">
      <thead>
        <tr>
          <th scope="col" class="grid__time">Time</th>
          <th v-for="court in courts" :key="court.courtId" scope="col">
            <div class="grid__court">{{ court.name }}</div>
            <div class="grid__meta">{{ court.indoor ? 'Indoor' : 'Outdoor' }}</div>
          </th>
        </tr>
      </thead>
      <tbody>
        <tr v-for="(row, i) in rows" :key="row.start">
          <th scope="row" class="grid__time pbc-num">{{ row.start }}</th>
          <td v-for="court in courts" :key="court.courtId">
            <template v-for="cell in [cellOf(court.courtId, court.slots[i]!)]" :key="cell.kind">
              <button v-if="cell.kind === 'booking'" type="button" class="cell cell--booking" :class="`cell--${cell.booking.status}`"
                      :aria-label="`${court.name} ${row.start}: ${cell.booking.customerName}, ${STATUS_LABEL[cell.booking.status]}`"
                      @click="emit('booking', cell.booking)">
                <template v-if="cell.first">
                  <span class="cell__name">{{ cell.booking.customerName || 'No name' }}</span>
                  <span class="cell__sub pbc-num">{{ cell.booking.code }} · {{ STATUS_LABEL[cell.booking.status] }}</span>
                </template>
                <span v-else class="cell__sub" aria-hidden="true">continues</span>
              </button>
              <button v-else-if="cell.kind === 'block'" type="button" class="cell cell--block"
                      :aria-label="`${court.name} ${row.start}: blocked, ${cell.block.reason}`" @click="emit('block', cell.block)">
                <span v-if="cell.first" class="cell__name">Blocked</span>
                <span class="cell__sub">{{ cell.first ? cell.block.reason : 'continues' }}</span>
              </button>
              <button v-else type="button" class="cell cell--free"
                      :aria-label="`${court.name} ${row.start}: free${cell.slot.price !== null ? ', ' + formatBaht(cell.slot.price) : ''}`"
                      @click="emit('free', court.courtId, cell.slot)">
                <span class="pbc-num">{{ cell.slot.price !== null ? formatBaht(cell.slot.price) : 'No price' }}</span>
              </button>
            </template>
          </td>
        </tr>
        <tr v-if="rows.length === 0">
          <td :colspan="courts.length + 1" class="grid__empty">Closed on this day.</td>
        </tr>
      </tbody>
    </table>
  </div>
</template>

<script setup lang="ts">
import { computed } from 'vue';
import type { AdminBooking, CourtBlock, Schedule, Slot } from '@pbc/api';
import { formatBaht } from '@pbc/ui';
import { ON_COURT, STATUS_LABEL } from 'src/composables/bookings';

defineOptions({ name: 'ScheduleGrid' });

const props = defineProps<{ schedule: Schedule }>();
const emit = defineEmits<{
  free: [courtId: string, slot: Slot];
  booking: [booking: AdminBooking];
  block: [block: CourtBlock];
}>();

type Cell =
  | { kind: 'booking'; booking: AdminBooking; first: boolean }
  | { kind: 'block'; block: CourtBlock; first: boolean }
  | { kind: 'free'; slot: Slot };

const courts = computed(() => props.schedule.availability.courts);
const rows = computed(() => courts.value[0]?.slots ?? []);

const covers = (x: { startAt: string; endAt: string }, t: number) => Date.parse(x.startAt) <= t && t < Date.parse(x.endAt);

function cellOf(courtId: string, slot: Slot): Cell {
  const t = Date.parse(slot.startAt);
  const block = props.schedule.blocks.find((b) => b.courtId === courtId && covers(b, t));
  if (block) return { kind: 'block', block, first: Date.parse(block.startAt) === t };
  const booking = props.schedule.bookings.find((b) => b.courtId === courtId && ON_COURT.includes(b.status) && covers(b, t));
  if (booking) return { kind: 'booking', booking, first: Date.parse(booking.startAt) === t };
  return { kind: 'free', slot };
}
</script>

<style scoped>
.grid-scroll { overflow-x: auto; border: 1px solid var(--pbc-line); border-radius: var(--pbc-r-lg); background: var(--pbc-surface); }
.grid { width: 100%; min-width: 640px; border-collapse: separate; border-spacing: 0; table-layout: fixed; }
.grid th, .grid td { padding: 3px; text-align: center; }
.grid thead th { position: sticky; top: 0; background: var(--pbc-surface); padding: 12px 4px; border-bottom: 1px solid var(--pbc-line); z-index: 1; }
.grid__court { font-family: var(--pbc-font-display); font-size: 1.05rem; font-weight: 500; }
.grid__meta { font-size: 0.75rem; color: var(--pbc-text-2); font-weight: 400; }
.grid__time { width: 68px; font-size: 0.85rem; font-weight: 500; color: var(--pbc-text-2); }
.grid__empty { padding: 32px 16px; color: var(--pbc-text-2); }

.cell {
  display: flex; flex-direction: column; align-items: center; justify-content: center; gap: 1px;
  width: 100%; min-height: 46px; padding: 2px 6px; cursor: pointer;
  border: 1px solid transparent; border-radius: var(--pbc-r-sm); font: inherit; font-size: 0.82rem; overflow: hidden;
}
.cell:hover { border-color: var(--pbc-ink); }
.cell:focus-visible { outline: 2px solid var(--pbc-ink); outline-offset: 1px; }
.cell__name { font-weight: 600; max-width: 100%; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
.cell__sub { font-size: 0.72rem; opacity: 0.85; max-width: 100%; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
.cell--free { background: transparent; color: var(--pbc-text-3); border: 1px dashed var(--pbc-line); }
.cell--free:hover { color: var(--pbc-ink); }
.cell--confirmed { background: var(--pbc-ink); color: #fff; }
.cell--held { background: var(--pbc-brass-surface); color: var(--pbc-brass-ink); }
.cell--checked_in { background: var(--pbc-positive); color: #fff; }
.cell--completed { background: var(--pbc-surface-2); color: var(--pbc-text-2); }
.cell--block { background: repeating-linear-gradient(135deg, #efe9dc, #efe9dc 6px, #e3dccb 6px, #e3dccb 12px); color: var(--pbc-text); }
</style>
