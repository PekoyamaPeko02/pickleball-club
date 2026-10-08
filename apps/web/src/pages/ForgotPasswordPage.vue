<template>
  <q-page class="pbc-page auth">
    <h1 class="pbc-h2 auth__title">Forgot your password?</h1>

    <p v-if="sent" class="auth__note" role="status">
      If <strong>{{ email }}</strong> has an account, we have sent it a link to choose a new password. The link works for 2 hours.
    </p>
    <template v-else>
      <p class="auth__note">Enter your email and we will send you a link to choose a new password.</p>
      <q-form class="auth__form" @submit.prevent="submit">
        <q-input v-model.trim="email" outlined type="email" label="Email" autocomplete="email" :rules="[required]" lazy-rules />
        <p v-if="error" class="text-negative q-mb-none" role="alert">{{ error }}</p>
        <q-btn unelevated no-caps size="lg" type="submit" class="pbc-btn-ink full-width" label="Send the link" :loading="busy" />
      </q-form>
    </template>

    <p class="auth__links"><router-link :to="{ name: 'login' }">Back to sign in</router-link></p>
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

<style src="../css/auth.scss" lang="scss" scoped />
