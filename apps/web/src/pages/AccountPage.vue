<template>
  <q-page class="pbc-page account">
    <div class="row items-center q-mb-lg">
      <h1 class="pbc-h2 account__title">My account</h1>
      <q-space />
      <q-btn flat no-caps label="Sign out" @click="signOut" />
    </div>

    <q-banner v-if="user && !user.profileComplete" class="account__notice" role="status">
      Add your name and phone number below before you book.
    </q-banner>

    <section class="pbc-card panel">
      <h2 class="pbc-h3">My bookings</h2>
      <q-skeleton v-if="bookings === null && !bookingsError" height="64px" />
      <p v-else-if="bookingsError" class="text-negative q-mb-none" role="alert">{{ bookingsError }}</p>
      <template v-else-if="bookings && bookings.length === 0">
        <p class="pbc-muted">You have no bookings yet.</p>
        <q-btn unelevated no-caps class="pbc-btn-brass" :to="{ name: 'book' }" label="Book a court" />
      </template>
      <ul v-else class="bookings">
        <li v-for="b in bookings" :key="b.id">
          <router-link :to="{ name: 'booking', params: { id: b.id } }" class="booking-row">
            <span>
              <strong>{{ b.courtName }}</strong> · {{ whenOf(b) }}
              <span class="booking-row__code pbc-num">{{ b.code }}</span>
            </span>
            <span class="chip" :class="`chip--${b.status}`">{{ STATUS_LABEL[b.status] }}</span>
          </router-link>
        </li>
      </ul>
    </section>

    <section class="pbc-card panel">
      <h2 class="pbc-h3">Your details</h2>
      <q-form class="form" @submit.prevent="saveProfile">
        <q-input v-model.trim="profile.displayName" outlined label="Name" autocomplete="name" :rules="[required]" lazy-rules />
        <q-input v-model.trim="profile.phone" outlined type="tel" label="Phone number" autocomplete="tel" :rules="[required]" lazy-rules
                 :error="!!profileError" :error-message="profileError" />
        <div><q-btn unelevated no-caps type="submit" class="pbc-btn-ink" label="Save" :loading="savingProfile" /></div>
      </q-form>
    </section>

    <section class="pbc-card panel">
      <h2 class="pbc-h3">Email</h2>
      <p class="q-mb-sm">{{ user?.email }}</p>
      <p v-if="user?.emailVerified" class="pbc-muted q-mb-none">Confirmed.</p>
      <template v-else>
        <p class="pbc-muted">Not confirmed yet. Open the link in the email we sent you.</p>
        <q-btn outline no-caps label="Send the email again" :loading="resending" @click="resend" />
      </template>
    </section>

    <section class="pbc-card panel">
      <h2 class="pbc-h3">Password</h2>
      <p v-if="user && !user.hasPassword" class="pbc-muted q-mb-none">
        You sign in with Google. To also sign in with a password,
        <router-link :to="{ name: 'forgot-password' }">set one by email</router-link>.
      </p>
      <q-form v-else ref="passwordForm" class="form" @submit.prevent="savePassword">
        <q-input v-model="passwords.current" outlined type="password" label="Current password" autocomplete="current-password" :rules="[required]" lazy-rules />
        <q-input v-model="passwords.next" outlined type="password" label="New password" autocomplete="new-password"
                 :rules="[required, longEnough]" lazy-rules hint="At least 10 characters. Other devices will be signed out." />
        <p v-if="passwordError" class="text-negative q-mb-none" role="alert">{{ passwordError }}</p>
        <div><q-btn unelevated no-caps type="submit" class="pbc-btn-ink" label="Change password" :loading="savingPassword" /></div>
      </q-form>
    </section>
  </q-page>
</template>

<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue';
import { useRouter } from 'vue-router';
import { QForm, useQuasar } from 'quasar';
import { ApiError, type Booking } from '@pbc/api';
import { useApi } from 'boot/api';
import { STATUS_LABEL, whenOf } from 'src/composables/bookings';
import { fieldError, messageOf } from 'src/composables/errors';
import { useAuthStore } from 'stores/auth';

defineOptions({ name: 'AccountPage' });

const $q = useQuasar();
const router = useRouter();
const auth = useAuthStore();
const user = computed(() => auth.user);

const required = (v: string) => !!v || 'Required';
const longEnough = (v: string) => v.length >= 10 || 'Use at least 10 characters';

// ---- bookings
const bookings = ref<Booking[] | null>(null);
const bookingsError = ref('');
async function loadBookings() {
  try {
    bookings.value = await useApi().myBookings();
  } catch (e) {
    bookingsError.value = messageOf(e);
  }
}

// ---- details
const profile = reactive({ displayName: user.value?.displayName ?? '', phone: user.value?.phone ?? '' });
const savingProfile = ref(false);
const profileError = ref('');

async function saveProfile() {
  savingProfile.value = true;
  profileError.value = '';
  try {
    auth.setUser(await useApi().updateProfile({ ...profile }));
    profile.phone = auth.user?.phone ?? profile.phone;
    $q.notify({ type: 'positive', message: 'Saved.' });
  } catch (e) {
    if (e instanceof ApiError && e.code === 'bad_phone') profileError.value = e.message;
    else $q.notify({ type: 'negative', message: fieldError(e, 'displayName') || fieldError(e, 'phone') || messageOf(e) });
  } finally {
    savingProfile.value = false;
  }
}

// ---- email
const resending = ref(false);
async function resend() {
  resending.value = true;
  try {
    await useApi().resendVerification();
    $q.notify({ type: 'positive', message: 'Sent. Check your inbox.' });
  } catch (e) {
    $q.notify({ type: 'negative', message: messageOf(e) });
  } finally {
    resending.value = false;
  }
}

// ---- password
const passwords = reactive({ current: '', next: '' });
const passwordForm = ref<QForm | null>(null);
const savingPassword = ref(false);
const passwordError = ref('');

async function savePassword() {
  savingPassword.value = true;
  passwordError.value = '';
  try {
    auth.setSession(await useApi().changePassword(passwords.current, passwords.next));
    passwords.current = passwords.next = '';
    passwordForm.value?.resetValidation();
    $q.notify({ type: 'positive', message: 'Password changed.' });
  } catch (e) {
    passwordError.value = fieldError(e, 'newPassword') || messageOf(e);
  } finally {
    savingPassword.value = false;
  }
}

function signOut() {
  auth.logout();
  void router.push({ name: 'home' });
}

// The stored copy may be stale (e.g. the email was confirmed on another device).
onMounted(async () => {
  void loadBookings();
  try {
    auth.setUser(await useApi().me());
    profile.displayName = auth.user?.displayName ?? '';
    profile.phone = auth.user?.phone ?? '';
  } catch {
    /* a 401 already sent the person to sign in; anything else: keep showing the stored copy */
  }
});
</script>

<style scoped>
.account { max-width: 640px; }
.account__title { margin: 0; color: var(--pbc-ink); }
.account__notice { background: var(--pbc-brass-surface); color: var(--pbc-brass-ink); border-radius: var(--pbc-r-lg); margin-bottom: 16px; }
.panel { padding: 20px; margin-bottom: 16px; }
.panel h2 { margin: 0 0 12px; color: var(--pbc-ink); }
.form { display: grid; gap: 12px; }
.bookings { list-style: none; margin: 0; padding: 0; }
.bookings li + li { border-top: 1px solid var(--pbc-line); }
.booking-row { display: flex; align-items: center; justify-content: space-between; gap: 12px; padding: 12px 0; text-decoration: none; color: var(--pbc-text); }
.booking-row:hover strong { text-decoration: underline; }
.booking-row__code { display: block; font-size: 0.8rem; color: var(--pbc-text-2); }
.chip { flex: 0 0 auto; padding: 2px 10px; border-radius: 999px; font-size: 0.75rem; font-weight: 600; background: var(--pbc-surface-2); color: var(--pbc-text-2); }
.chip--held { background: var(--pbc-brass-surface); color: var(--pbc-brass-ink); }
.chip--confirmed, .chip--checked_in { background: var(--pbc-positive-surface); color: var(--pbc-positive); }
.chip--cancelled, .chip--no_show { background: #fbeae8; color: var(--pbc-error); }
</style>
