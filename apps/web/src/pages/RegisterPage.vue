<template>
  <q-page class="pbc-page auth">
    <h1 class="pbc-h2 auth__title">Create an account</h1>

    <q-form class="auth__form" @submit.prevent="submit">
      <q-input v-model.trim="form.displayName" outlined label="Name" autocomplete="name" :rules="[required]" lazy-rules
               :error="!!errors.displayName" :error-message="errors.displayName" />
      <q-input v-model.trim="form.email" outlined type="email" label="Email" autocomplete="email" :rules="[required]" lazy-rules
               :error="!!errors.email" :error-message="errors.email" />
      <q-input v-model.trim="form.phone" outlined type="tel" label="Phone number" autocomplete="tel" :rules="[required]" lazy-rules
               hint="So the club can reach you about your booking." :error="!!errors.phone" :error-message="errors.phone" />
      <q-input v-model="form.password" outlined :type="show ? 'text' : 'password'" label="Password" autocomplete="new-password"
               :rules="[required, longEnough]" lazy-rules hint="At least 10 characters." :error="!!errors.password" :error-message="errors.password">
        <template #append>
          <q-icon :name="show ? 'visibility_off' : 'visibility'" class="cursor-pointer" role="button" tabindex="0"
                  :aria-label="show ? 'Hide password' : 'Show password'" @click="show = !show" @keydown.enter.prevent="show = !show" />
        </template>
      </q-input>
      <p v-if="error" class="text-negative q-mb-none" role="alert">{{ error }}</p>
      <q-btn unelevated no-caps size="lg" type="submit" class="pbc-btn-ink full-width" label="Create account" :loading="busy" />
    </q-form>

    <p class="auth__links">
      Already have an account? <router-link :to="{ name: 'login', query: route.query }">Sign in</router-link>
    </p>
  </q-page>
</template>

<script setup lang="ts">
import { reactive, ref } from 'vue';
import { useRoute } from 'vue-router';
import { useApi } from 'boot/api';
import { fieldError, messageOf } from 'src/composables/errors';
import { useSignedIn } from 'src/composables/session';

defineOptions({ name: 'RegisterPage' });

const route = useRoute();
const signedIn = useSignedIn();
const form = reactive({ displayName: '', email: '', phone: '', password: '' });
const errors = reactive({ displayName: '', email: '', phone: '', password: '' });
const show = ref(false);
const busy = ref(false);
const error = ref('');

const required = (v: string) => !!v || 'Required';
const longEnough = (v: string) => v.length >= 10 || 'Use at least 10 characters';

async function submit() {
  busy.value = true;
  error.value = '';
  Object.assign(errors, { displayName: '', email: '', phone: '', password: '' });
  try {
    await signedIn(await useApi().register({ ...form }));
  } catch (e) {
    for (const key of Object.keys(errors) as (keyof typeof errors)[]) errors[key] = fieldError(e, key);
    // A message that belongs to one field is shown under that field only.
    const onField = Object.values(errors).some(Boolean);
    error.value = onField ? '' : messageOf(e);
  } finally {
    busy.value = false;
  }
}
</script>

<style src="../css/auth.scss" lang="scss" scoped />
