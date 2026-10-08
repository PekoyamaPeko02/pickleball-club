<template>
  <q-page class="flex flex-center">
    <div class="pbc-card login">
      <h1 class="pbc-h3">Admin sign-in</h1>
      <q-form class="form" @submit.prevent="submit">
        <q-input v-model.trim="email" outlined type="email" label="Email" autocomplete="username" :rules="[required]" lazy-rules />
        <q-input v-model="password" outlined type="password" label="Password" autocomplete="current-password" :rules="[required]" lazy-rules />
        <p v-if="error" class="text-negative q-mb-none" role="alert">{{ error }}</p>
        <p v-if="clubStore.connecting" class="pbc-muted q-mb-none" role="status">Connecting to the server… this can take a minute or two.</p>
        <q-btn unelevated no-caps size="lg" type="submit" class="pbc-btn-ink full-width" label="Sign in" :loading="busy" :disable="clubStore.connecting" />
      </q-form>
      <p class="links"><router-link :to="{ name: 'forgot-password' }">Forgot your password?</router-link></p>
    </div>
  </q-page>
</template>

<script setup lang="ts">
import { onMounted, ref } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import { useApi } from 'boot/api';
import { messageOf } from 'src/composables/errors';
import { useAuthStore } from 'stores/auth';
import { useClubStore } from 'stores/club';

defineOptions({ name: 'LoginPage' });

const route = useRoute();
const router = useRouter();
const auth = useAuthStore();
// Signing in is the first thing an Admin does: find out here whether the server is awake, not on the button.
const clubStore = useClubStore();
onMounted(() => void clubStore.load());
const email = ref('');
const password = ref('');
const busy = ref(false);
const error = ref('');
const required = (v: string) => !!v || 'Required';

async function submit() {
  busy.value = true;
  error.value = '';
  try {
    auth.setSession(await useApi().adminLogin(email.value, password.value));
    const redirect = typeof route.query.redirect === 'string' ? route.query.redirect : '';
    await router.replace(redirect.startsWith('/') && !redirect.startsWith('//') ? redirect : { name: 'schedule' });
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
