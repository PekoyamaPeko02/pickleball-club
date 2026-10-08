<template>
  <q-page class="flex flex-center">
    <div class="pbc-card login">
      <h1 class="pbc-h3">Choose a new password</h1>
      <template v-if="done">
        <p class="pbc-muted" role="status">Your password has been changed.</p>
        <q-btn unelevated no-caps size="lg" class="pbc-btn-ink full-width" :to="{ name: 'login' }" label="Sign in" />
      </template>
      <p v-else-if="!token" class="pbc-muted" role="alert">
        This link is incomplete. Open the link from the email again, or <router-link :to="{ name: 'forgot-password' }">ask for a new one</router-link>.
      </p>
      <q-form v-else class="form" @submit.prevent="submit">
        <q-input v-model="password" outlined type="password" label="New password" autocomplete="new-password" :rules="[required, longEnough]" lazy-rules hint="At least 10 characters." />
        <p v-if="error" class="text-negative q-mb-none" role="alert">{{ error }}</p>
        <q-btn unelevated no-caps size="lg" type="submit" class="pbc-btn-ink full-width" label="Save the new password" :loading="busy" />
      </q-form>
    </div>
  </q-page>
</template>

<script setup lang="ts">
import { ref } from 'vue';
import { useRoute } from 'vue-router';
import { useApi } from 'boot/api';
import { fieldError, messageOf } from 'src/composables/errors';

defineOptions({ name: 'ResetPasswordPage' });

const route = useRoute();
const token = typeof route.query.token === 'string' ? route.query.token : '';
const password = ref('');
const busy = ref(false);
const done = ref(false);
const error = ref('');
const required = (v: string) => !!v || 'Required';
const longEnough = (v: string) => v.length >= 10 || 'Use at least 10 characters';

async function submit() {
  busy.value = true;
  error.value = '';
  try {
    await useApi().resetPassword(token, password.value);
    done.value = true;
  } catch (e) {
    error.value = fieldError(e, 'newPassword') || messageOf(e);
  } finally {
    busy.value = false;
  }
}
</script>

<style scoped>
.login { padding: 32px; width: min(92vw, 380px); }
.login h1 { margin: 0 0 16px; color: var(--pbc-ink); }
.form { display: grid; gap: 12px; }
</style>
