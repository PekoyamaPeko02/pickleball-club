<template>
  <q-page class="pbc-page auth">
    <h1 class="pbc-h2 auth__title">Choose a new password</h1>

    <template v-if="done">
      <p class="auth__note" role="status">Your password has been changed. You can sign in with it now.</p>
      <q-btn unelevated no-caps size="lg" class="pbc-btn-ink full-width" :to="{ name: 'login' }" label="Sign in" />
    </template>
    <p v-else-if="!token" class="auth__note" role="alert">
      This link is incomplete. Open the link from the email again, or
      <router-link :to="{ name: 'forgot-password' }">ask for a new one</router-link>.
    </p>
    <q-form v-else class="auth__form" @submit.prevent="submit">
      <q-input v-model="password" outlined :type="show ? 'text' : 'password'" label="New password" autocomplete="new-password"
               :rules="[required, longEnough]" lazy-rules hint="At least 10 characters.">
        <template #append>
          <q-icon :name="show ? 'visibility_off' : 'visibility'" class="cursor-pointer" role="button" tabindex="0"
                  :aria-label="show ? 'Hide password' : 'Show password'" @click="show = !show" @keydown.enter.prevent="show = !show" />
        </template>
      </q-input>
      <p v-if="error" class="text-negative q-mb-none" role="alert">
        {{ error }}
        <router-link v-if="expired" :to="{ name: 'forgot-password' }">Ask for a new link</router-link>
      </p>
      <q-btn unelevated no-caps size="lg" type="submit" class="pbc-btn-ink full-width" label="Save the new password" :loading="busy" />
    </q-form>
  </q-page>
</template>

<script setup lang="ts">
import { ref } from 'vue';
import { useRoute } from 'vue-router';
import { ApiError } from '@pbc/api';
import { useApi } from 'boot/api';
import { fieldError, messageOf } from 'src/composables/errors';

defineOptions({ name: 'ResetPasswordPage' });

const route = useRoute();
const token = typeof route.query.token === 'string' ? route.query.token : '';
const password = ref('');
const show = ref(false);
const busy = ref(false);
const done = ref(false);
const expired = ref(false);
const error = ref('');

const required = (v: string) => !!v || 'Required';
const longEnough = (v: string) => v.length >= 10 || 'Use at least 10 characters';

async function submit() {
  busy.value = true;
  error.value = '';
  expired.value = false;
  try {
    await useApi().resetPassword(token, password.value);
    done.value = true;
  } catch (e) {
    expired.value = e instanceof ApiError && e.code === 'reset_token_invalid';
    error.value = fieldError(e, 'newPassword') || messageOf(e);
  } finally {
    busy.value = false;
  }
}
</script>

<style src="../css/auth.scss" lang="scss" scoped />
