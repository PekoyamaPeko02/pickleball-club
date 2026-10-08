<template>
  <q-page class="pbc-page narrow">
    <h1 class="pbc-h2 page-title">{{ kind === 'privacy' ? 'Privacy Policy' : 'Terms & Conditions' }}</h1>
    <!-- PLACEHOLDER: the Club must supply the real legal text before launch. -->
    <p class="pbc-muted">This page is not written yet.</p>
  </q-page>
</template>

<script setup lang="ts">
defineOptions({ name: 'PolicyPage' });
defineProps<{ kind: 'privacy' | 'terms' }>();
</script>

<style scoped>
.narrow { max-width: 720px; }
.page-title { margin: 0 0 8px; color: var(--pbc-ink); }
</style>
