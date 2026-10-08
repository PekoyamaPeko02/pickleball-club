<template>
  <q-page class="flex flex-center">
    <div class="pbc-card login">
      <h1 class="pbc-h3">Forgot your password?</h1>
      <p v-if="sent" class="pbc-muted" role="status">
        If <strong>{{ email }}</strong> is an admin account, we have sent it a link to choose a new password. The link works for 2 hours.
      </p>
      <q-form v-else class="form" @submit.prevent="submit">
        <q-input v-model.trim="email" outlined type="email" label="Email" autocomplete="username" :rules="[required]" lazy-rules />
        <p v-if="error" class="text-negative q-mb-none" role="alert">{{ error }}</p>
        <q-btn unelevated no-caps size="lg" type="submit" class="pbc-btn-ink full-width" label="Send the link" :loading="busy" />
      </q-form>
      <p class="links"><router-link :to="{ name: 'login' }">Back to sign in</router-link></p>
    </div>
  </q-page>
</template>

<script setup lang="ts">
import { ref } from 'vue';
import { useApi } from 'boot/api';
import { messageOf } from 'src/composables/errors';

defineOptions({ name: 'ForgotPasswordPage' });

const email = ref('');
const busy = ref(false);
const sent = ref(false);
const error = ref('');
const required = (v: string) => !!v || 'Required';

async function submit() {
  busy.value = true;
  error.value = '';
  try {
    await useApi().forgotPassword(email.value);
    sent.value = true;
  } catch (e) {
    error.value = messageOf(e);
  } finally {
    busy.value = false;
  }
}
</script>

<style scoped>
.login { padding: 32px; width: min(92vw, 380px); }
.login h1 { margin: 0 0 16px; color: var(--pbc-ink); }
.form { display: grid; gap: 12px; }
.links { margin: 16px 0 0; text-align: center; }
</style>
