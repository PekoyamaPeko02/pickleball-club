<template>
  <q-page class="pbc-page">
    <h1 class="pbc-h2 page-title">The courts</h1>
    <p class="pbc-muted lead">Each court is rented whole, by the hour, for you and the people you bring.<span v-if="priceToday"> Today: {{ priceToday }}.</span></p>

    <q-banner v-if="store.error" class="text-negative" role="alert">{{ store.error }}</q-banner>
    <div v-else-if="courts.length === 0" class="courts">
      <q-skeleton v-for="n in 4" :key="n" height="300px" />
    </div>
    <ul v-else class="courts">
      <li v-for="court in courts" :key="court.courtId" class="pbc-card court">
        <!-- PLACEHOLDER: a photograph of the court belongs here once the Club has one; until then, its plan. -->
        <div class="court__plan"><CourtPlan :label="`${court.name}, seen from above`" :free="court.free" /></div>
        <div class="court__body">
          <h2 class="pbc-h3">{{ court.name }}</h2>
          <p class="pbc-muted">{{ court.indoor ? 'Indoor' : 'Outdoor' }}</p>
          <p v-if="court.status" class="court__status" :class="{ 'court__status--taken': !court.free }">{{ court.status }}</p>
          <q-btn outline no-caps :to="{ name: 'book' }" :label="`Book ${court.name}`" />
        </div>
      </li>
    </ul>

    <section v-if="club" class="hours" aria-labelledby="hours-title">
      <h2 id="hours-title" class="pbc-h3">Opening hours</h2>
      <table>
        <tbody>
          <tr v-for="line in hours" :key="line.days"><th scope="row">{{ line.days }}</th><td class="pbc-num">{{ line.hours }}</td></tr>
        </tbody>
      </table>
    </section>
  </q-page>
</template>

<script setup lang="ts">
import { computed } from 'vue';
import { CourtPlan } from '@pbc/ui';
import { hoursLines, useToday } from 'src/composables/today';
import { useClubStore } from 'stores/club';

defineOptions({ name: 'CourtsPage' });

const store = useClubStore();
const { club, courts, priceToday } = useToday();
const hours = computed(() => (club.value ? hoursLines(club.value) : []));
</script>

<style scoped>
.page-title { margin: 0 0 8px; color: var(--pbc-ink); }
.lead { max-width: 60ch; margin-bottom: 28px; font-size: 1.05rem; line-height: 1.55; }
.courts { list-style: none; margin: 0; padding: 0; display: grid; gap: 16px; grid-template-columns: repeat(auto-fit, minmax(230px, 1fr)); }
.court { display: grid; grid-template-columns: 96px 1fr; gap: 20px; align-items: center; padding: 20px; }
.court__plan { --plan-apron: var(--pbc-surface-2); --plan-taken: #9fb3c8; --plan-taken-kitchen: #b9c9d9; }
.court__body h2 { margin: 0 0 2px; color: var(--pbc-ink); }
.court__body p { margin: 0 0 8px; }
.court__status { font-weight: 600; color: var(--pbc-positive); }
.court__status--taken { color: var(--pbc-text-2); font-weight: 500; }
.hours { margin-top: 48px; }
.hours h2 { margin: 0 0 8px; color: var(--pbc-ink); }
.hours table { border-collapse: collapse; }
.hours th { text-align: left; font-weight: 500; padding: 6px 40px 6px 0; }
.hours td { padding: 6px 0; color: var(--pbc-text-2); }
</style>
