<template>
  <q-page class="pbc-page narrow">
    <h1 class="pbc-h2 page-title">Page not found</h1>
    <p class="pbc-muted">The page you are looking for does not exist.</p>
    <q-btn unelevated no-caps class="pbc-btn-ink" :to="{ name: 'home' }" label="Back to the home page" />
  </q-page>
</template>

<script setup lang="ts">
defineOptions({ name: 'ErrorNotFound' });
</script>

<style scoped>
.narrow { max-width: 480px; }
.page-title { margin: 0 0 8px; color: var(--pbc-ink); }
</style>
