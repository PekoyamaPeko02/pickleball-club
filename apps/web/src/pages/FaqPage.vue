<template>
  <q-page class="pbc-page">
    <h1 class="pbc-h2 page-title">Frequently asked questions</h1>
    <q-list bordered separator class="pbc-card faq">
      <q-expansion-item v-for="item in items" :key="item.q" :label="item.q" header-class="faq__q" expand-separator>
        <div class="faq__a pbc-muted">{{ item.a }}</div>
      </q-expansion-item>
    </q-list>
  </q-page>
</template>

<script setup lang="ts">
import { computed, onMounted } from 'vue';
import { useClubStore } from 'stores/club';

defineOptions({ name: 'FaqPage' });

const store = useClubStore();
onMounted(() => store.load());

// The booking rules below are the Club's real rules (see CONTEXT.md); numbers come from the Club's settings.
// PLACEHOLDER: add the Club's own answers on parking, shoes, equipment and facilities.
const items = computed(() => {
  const window = store.club?.bookingWindowDays ?? 14;
  const notice = store.club?.rescheduleNoticeHours ?? 48;
  return [
    { q: 'How do I book a court?', a: 'Open “Book a court”, choose a day and the hours you want on one court, then sign in and pay online. Your booking is confirmed as soon as the payment goes through.' },
    { q: 'How far ahead can I book?', a: `Up to ${window} days in advance.` },
    { q: 'How do I pay?', a: 'By PromptPay QR or by credit or debit card. The court is held for you for 10 minutes while you pay.' },
    { q: 'Can I cancel and get a refund?', a: 'Bookings are non-refundable.' },
    {
      q: 'Can I move my booking?',
      a: `Yes, once per booking, at least ${notice} hours before you play. The new time must have the same number of hours, cost the same or less, and fall within the next ${window} days. You may change court.`,
    },
    { q: 'Can I book by phone or at the club?', a: 'Yes. Our staff can make the booking for you and take payment at the counter.' },
  ];
});
</script>

<style scoped>
.page-title { margin: 0 0 24px; color: var(--pbc-ink); }
.faq { overflow: hidden; }
.faq :deep(.faq__q) { font-weight: 600; padding: 16px 20px; }
.faq__a { padding: 0 20px 20px; max-width: 70ch; line-height: 1.6; }
</style>
