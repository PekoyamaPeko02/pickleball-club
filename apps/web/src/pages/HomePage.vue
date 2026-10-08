<template>
  <q-page class="home">
    <!-- The courts themselves, drawn to scale and lit when free, are the picture. -->
    <section class="hero">
      <div class="hero__inner">
        <div class="hero__copy">
          <p v-if="club" class="pbc-label hero__eyebrow">{{ club.courts.length }} courts · {{ hours[0]?.days === 'Every day' ? `open every day ${hours[0].hours}` : 'pickleball club' }}</p>
          <h1 class="pbc-h1 hero__title">Pick an hour.<br>The court is <em>yours.</em></h1>
          <p class="hero__lead">See what is free, choose your court and pay online. No membership and no phone calls.</p>
          <div class="hero__actions">
            <q-btn unelevated no-caps size="lg" class="pbc-btn-brass" :to="{ name: 'book' }" label="Book a court" />
            <span v-if="priceToday" class="hero__price pbc-num">{{ priceToday }}</span>
          </div>
        </div>

        <div class="night">
          <p class="night__title">
            <span class="pbc-label">Today at the club</span>
            <span v-if="club" class="night__date">{{ formatWeekday(club.today) }} {{ formatDayMonth(club.today) }}</span>
          </p>
          <ul class="night__courts" :style="{ '--courts': Math.max(courts.length, 1) }">
            <li v-for="court in courts" :key="court.courtId">
              <router-link :to="{ name: 'book' }" class="court" :class="{ 'court--taken': !court.free }">
                <CourtPlan :label="`${court.name}, ${court.status || (court.indoor ? 'indoor' : 'outdoor')}`" :free="court.free" draw />
                <span class="court__name">{{ court.name }}</span>
                <span class="court__meta">{{ court.indoor ? 'Indoor' : 'Outdoor' }}</span>
                <span v-if="court.status" class="court__status">
                  <template v-if="court.from">
                    <span class="court__when">Free from</span>
                    <strong class="pbc-num"><span class="court__dot" aria-hidden="true" />{{ court.from }}</strong>
                  </template>
                  <span v-else class="court__when"><span class="court__dot" aria-hidden="true" />{{ court.status }}</span>
                </span>
              </router-link>
            </li>
          </ul>
        </div>
      </div>
    </section>

    <!-- A real sequence, so it is numbered. -->
    <section class="pbc-page steps" aria-labelledby="steps-title">
      <h2 id="steps-title" class="pbc-h2 section-title">Booked in three steps</h2>
      <ol class="steps__list">
        <li>
          <h3 class="pbc-h3">Choose your hours</h3>
          <p>Open the day you want and tap one hour, or several in a row, on one court. Every hour shows its price.</p>
        </li>
        <li>
          <h3 class="pbc-h3">Pay by PromptPay or card</h3>
          <p>We hold the court for 10 minutes while you pay. The booking is confirmed the moment the payment arrives.</p>
        </li>
        <li>
          <h3 class="pbc-h3">Show your code</h3>
          <p>Your booking code comes by email. Show it at the club and play.</p>
        </li>
      </ol>
    </section>

    <section class="know" aria-labelledby="know-title">
      <div class="pbc-page know__inner">
        <h2 id="know-title" class="pbc-h2 section-title">Good to know before you book</h2>
        <dl class="know__list">
          <div><dt>Book ahead</dt><dd>Up to {{ club?.bookingWindowDays ?? 14 }} days in advance.</dd></div>
          <div><dt>Plans change</dt><dd>Move a booking once, up to {{ club?.rescheduleNoticeHours ?? 48 }} hours before you play, to a time of the same length that costs the same or less.</dd></div>
          <div><dt>No refunds</dt><dd>A paid booking is not refunded, so moving it is the way to change it.</dd></div>
          <div><dt>By phone or at the desk</dt><dd>Our staff can book for you and take payment at the counter.</dd></div>
        </dl>
        <p class="know__more"><router-link :to="{ name: 'faq' }">All questions and answers</router-link></p>
      </div>
    </section>

    <section v-if="club" class="pbc-page visit" aria-labelledby="visit-title">
      <div>
        <h2 id="visit-title" class="pbc-h2 section-title">Opening hours</h2>
        <table class="visit__hours">
          <tbody>
            <tr v-for="line in hours" :key="line.days"><th scope="row">{{ line.days }}</th><td class="pbc-num">{{ line.hours }}</td></tr>
          </tbody>
        </table>
      </div>
      <div class="visit__cta">
        <p class="pbc-h3">Ready when you are.</p>
        <q-btn unelevated no-caps size="lg" class="pbc-btn-ink" :to="{ name: 'book' }" label="Book a court" />
      </div>
    </section>
  </q-page>
</template>

<script setup lang="ts">
import { computed } from 'vue';
import { CourtPlan, formatDayMonth, formatWeekday } from '@pbc/ui';
import { hoursLines, useToday } from 'src/composables/today';

defineOptions({ name: 'HomePage' });

// PLACEHOLDER COPY: the words on this page are a starting point for the Club to make its own.
const { club, courts, priceToday } = useToday();
const hours = computed(() => (club.value ? hoursLines(club.value) : []));
</script>

<style scoped>
.home { background: var(--pbc-bg); }

/* ---- hero */
.hero__inner { max-width: 1120px; margin: 0 auto; padding: 40px 16px 24px; display: grid; gap: 32px; align-items: center; }
.hero__eyebrow { color: var(--pbc-court); margin: 0 0 16px; }
.hero__title { margin: 0 0 20px; color: var(--pbc-ink); }
.hero__title em { font-style: italic; }
.hero__lead { font-size: 1.125rem; line-height: 1.55; color: var(--pbc-text-2); max-width: 40ch; margin: 0 0 28px; }
.hero__actions { display: flex; flex-wrap: wrap; align-items: center; gap: 16px 20px; }
.hero__price { color: var(--pbc-text-2); font-weight: 500; }

.night { background: var(--pbc-ink); color: #fff; border-radius: 20px; padding: 24px 20px 28px; }
.night__title { display: flex; align-items: baseline; justify-content: space-between; gap: 12px; margin: 0 0 20px; color: rgba(255, 255, 255, 0.72); }
.night__date { font-family: var(--pbc-font-display); font-size: 1.05rem; color: #fff; }
.night__courts { list-style: none; margin: 0; padding: 0; display: grid; grid-template-columns: repeat(var(--courts), minmax(0, 1fr)); gap: 12px; }
.court { display: grid; gap: 2px; text-decoration: none; color: #fff; border-radius: 10px; outline-offset: 6px; }
.court:focus-visible { outline: 2px solid var(--pbc-brass); }
.court :deep(.plan) { margin-bottom: 10px; transition: transform 0.25s ease; }
.court:hover :deep(.plan) { transform: translateY(-4px); }
.court__name { font-family: var(--pbc-font-display); font-size: 1.1rem; line-height: 1.2; }
.court__meta { font-size: 0.78rem; color: rgba(255, 255, 255, 0.6); }
.court__status { display: grid; gap: 1px; margin-top: 8px; font-size: 0.78rem; font-weight: 500; line-height: 1.3; color: rgba(255, 255, 255, 0.72); }
.court__status strong { display: flex; align-items: center; gap: 6px; font-size: 1rem; font-weight: 600; color: #fff; }
.court__when { display: flex; align-items: center; gap: 6px; white-space: nowrap; }
.court__dot { flex: 0 0 auto; width: 8px; height: 8px; border-radius: 50%; background: var(--pbc-brass); }
.court--taken .court__status { color: rgba(255, 255, 255, 0.6); }
.court--taken .court__dot { background: rgba(255, 255, 255, 0.35); }
@media (prefers-reduced-motion: reduce) { .court :deep(.plan) { transition: none; } .court:hover :deep(.plan) { transform: none; } }

@media (min-width: 900px) {
  .hero__inner { grid-template-columns: minmax(0, 1fr) minmax(0, 1.05fr); gap: 56px; padding: 72px 32px 56px; }
  .night { padding: 32px 32px 36px; }
  .night__courts { gap: 20px; }
}

/* ---- sections */
.section-title { margin: 0 0 28px; color: var(--pbc-ink); }
.steps { padding-top: 56px; padding-bottom: 56px; }
.steps__list { list-style: none; counter-reset: step; margin: 0; padding: 0; display: grid; gap: 28px; grid-template-columns: repeat(auto-fit, minmax(240px, 1fr)); }
.steps__list li { counter-increment: step; border-top: 2px solid var(--pbc-ink); padding-top: 16px; }
.steps__list li::before { content: counter(step); display: block; font-family: var(--pbc-font-display); font-size: 2.5rem; line-height: 1; color: var(--pbc-court); margin-bottom: 12px; }
.steps__list h3 { margin: 0 0 8px; color: var(--pbc-ink); }
.steps__list p { margin: 0; color: var(--pbc-text-2); line-height: 1.6; }

.know { background: var(--pbc-surface); border-top: 1px solid var(--pbc-line); border-bottom: 1px solid var(--pbc-line); }
.know__inner { padding-top: 56px; padding-bottom: 56px; }
.know__list { margin: 0; display: grid; gap: 24px 40px; grid-template-columns: repeat(auto-fit, minmax(260px, 1fr)); }
.know__list dt { font-weight: 700; color: var(--pbc-ink); margin-bottom: 4px; }
.know__list dd { margin: 0; color: var(--pbc-text-2); line-height: 1.6; }
.know__more { margin: 28px 0 0; font-weight: 600; }

.visit { display: grid; gap: 32px; align-items: end; grid-template-columns: repeat(auto-fit, minmax(260px, 1fr)); padding-top: 56px; }
.visit__hours { border-collapse: collapse; }
.visit__hours th { text-align: left; font-weight: 500; padding: 6px 40px 6px 0; }
.visit__hours td { padding: 6px 0; color: var(--pbc-text-2); }
.visit__cta p { margin: 0 0 16px; color: var(--pbc-ink); }
</style>
