<template>
  <q-page class="pbc-page auth">
    <h1 class="pbc-h2 auth__title">Confirm your email</h1>
    <q-spinner v-if="state === 'working'" size="32px" color="primary" aria-label="Confirming" />
    <template v-else-if="state === 'done'">
      <p class="auth__note" role="status">Thank you — your email address is confirmed.</p>
      <q-btn unelevated no-caps size="lg" class="pbc-btn-brass full-width" :to="{ name: 'book' }" label="Book a court" />
    </template>
    <p v-else class="auth__note" role="alert">
      {{ error }}
      <router-link :to="{ name: 'account' }">Go to your account</router-link> to send a new link.
    </p>
  </q-page>
</template>

<script setup lang="ts">
import { onMounted, ref } from 'vue';
import { useRoute } from 'vue-router';
import { useApi } from 'boot/api';
import { messageOf } from 'src/composables/errors';
import { useAuthStore } from 'stores/auth';

defineOptions({ name: 'VerifyEmailPage' });

const route = useRoute();
const auth = useAuthStore();
const state = ref<'working' | 'done' | 'failed'>('working');
const error = ref('');

onMounted(async () => {
  const token = typeof route.query.token === 'string' ? route.query.token : '';
  if (!token) {
    state.value = 'failed';
    error.value = 'This link is incomplete.';
    return;
  }
  try {
    await useApi().verifyEmail(token);
    state.value = 'done';
    if (auth.isLoggedIn) auth.setUser(await useApi().me()); // refresh the "confirmed" flag when signed in on this device
  } catch (e) {
    state.value = 'failed';
    error.value = messageOf(e);
  }
});
</script>

<style src="../css/auth.scss" lang="scss" scoped />
