<template>
  <q-page class="pbc-page auth">
    <h1 class="pbc-h2 auth__title">Sign in</h1>

    <q-form class="auth__form" @submit.prevent="submit">
      <q-input v-model.trim="email" outlined type="email" label="Email" autocomplete="email" :rules="[required]" lazy-rules />
      <q-input v-model="password" outlined :type="show ? 'text' : 'password'" label="Password" autocomplete="current-password" :rules="[required]" lazy-rules>
        <template #append>
          <q-icon :name="show ? 'visibility_off' : 'visibility'" class="cursor-pointer" role="button" tabindex="0"
                  :aria-label="show ? 'Hide password' : 'Show password'" @click="show = !show" @keydown.enter.prevent="show = !show" />
        </template>
      </q-input>
      <p v-if="error" class="text-negative q-mb-none" role="alert">{{ error }}</p>
      <q-btn unelevated no-caps size="lg" type="submit" class="pbc-btn-ink full-width" label="Sign in" :loading="busy" />
    </q-form>

    <p class="auth__links">
      <router-link :to="{ name: 'forgot-password' }">Forgot your password?</router-link>
    </p>

    <div class="auth__or"><span>or</span></div>
    <GoogleButton :client-id="googleClientId" @credential="withGoogle" />

    <p class="auth__links">
      New here? <router-link :to="{ name: 'register', query: route.query }">Create an account</router-link>
    </p>
  </q-page>
</template>

<script setup lang="ts">
import { onMounted, ref } from 'vue';
import { useRoute } from 'vue-router';
import { useApi } from 'boot/api';
import GoogleButton from 'components/GoogleButton.vue';
import { messageOf } from 'src/composables/errors';
import { useSignedIn } from 'src/composables/session';

defineOptions({ name: 'LoginPage' });

const route = useRoute();
const signedIn = useSignedIn();
const email = ref('');
const password = ref('');
const show = ref(false);
const busy = ref(false);
const error = ref('');
const googleClientId = ref<string | null>(null);

const required = (v: string) => !!v || 'Required';

async function run(call: () => ReturnType<ReturnType<typeof useApi>['login']>) {
  busy.value = true;
  error.value = '';
  try {
    await signedIn(await call());
  } catch (e) {
    error.value = messageOf(e);
  } finally {
    busy.value = false;
  }
}

const submit = () => run(() => useApi().login(email.value, password.value));
const withGoogle = (credential: string) => run(() => useApi().googleLogin(credential));

onMounted(async () => {
  try {
    googleClientId.value = (await useApi().getAuthConfig()).googleClientId;
  } catch {
    /* Google stays off; email sign-in still works */
  }
});
</script>

<style src="../css/auth.scss" lang="scss" scoped />
