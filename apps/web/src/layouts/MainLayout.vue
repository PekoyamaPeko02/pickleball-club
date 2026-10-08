<template>
  <q-layout view="hHh lpR fff">
    <q-header class="site-header">
      <q-toolbar class="site-header__bar">
        <router-link :to="{ name: 'home' }" class="brand pbc-display">
          <img src="/favicon.svg" alt="" width="28" height="28" class="brand__mark">{{ clubName }}
        </router-link>
        <q-space />
        <nav class="gt-sm row items-center q-gutter-sm" aria-label="Main">
          <q-btn v-for="link in links" :key="link.name" flat no-caps :to="{ name: link.name }" :label="link.label" />
          <q-btn outline no-caps :to="{ name: auth.isLoggedIn ? 'account' : 'login' }" :label="auth.isLoggedIn ? 'My account' : 'Sign in'" />
          <q-btn unelevated no-caps class="pbc-btn-brass" :to="{ name: 'book' }" label="Book a court" />
        </nav>
        <q-btn class="lt-md" flat round icon="menu" aria-label="Open menu" @click="menu = true" />
      </q-toolbar>
    </q-header>

    <q-drawer v-model="menu" side="right" overlay behavior="mobile" :width="260">
      <q-list padding>
        <q-item v-for="link in links" :key="link.name" v-ripple clickable :to="{ name: link.name }">
          <q-item-section>{{ link.label }}</q-item-section>
        </q-item>
        <q-item v-ripple clickable :to="{ name: auth.isLoggedIn ? 'account' : 'login' }">
          <q-item-section>{{ auth.isLoggedIn ? 'My account' : 'Sign in' }}</q-item-section>
        </q-item>
        <q-item v-ripple clickable :to="{ name: 'book' }"><q-item-section class="text-weight-bold">Book a court</q-item-section></q-item>
      </q-list>
    </q-drawer>

    <q-page-container>
      <q-banner v-if="isMockApi" dense class="mock-banner">
        Demo data — this page is running without the server.
      </q-banner>
      <q-banner v-else-if="clubStore.club?.demo" dense class="mock-banner">
        Demo site — bookings here are not real and no money is taken.
      </q-banner>
      <!-- A server that was asleep needs a moment: wait here, then let the page load with it awake. -->
      <div v-if="clubStore.connecting" class="pbc-page connecting" role="status">
        <q-spinner size="32px" color="primary" />
        <p class="connecting__title">Connecting to the server…</p>
        <p class="pbc-muted">This can take a minute or two.</p>
      </div>
      <router-view v-else />
    </q-page-container>

    <q-footer class="site-footer">
      <div class="pbc-page site-footer__inner">
        <span>© {{ year }} {{ clubName }}</span>
        <span class="row q-gutter-md">
          <router-link :to="{ name: 'privacy' }">Privacy Policy</router-link>
          <router-link :to="{ name: 'terms' }">Terms &amp; Conditions</router-link>
        </span>
      </div>
    </q-footer>
  </q-layout>
</template>

<script setup lang="ts">
import { computed, onMounted, ref } from 'vue';
import { isMockApi } from 'boot/api';
import { useAuthStore } from 'stores/auth';
import { useClubStore } from 'stores/club';

defineOptions({ name: 'MainLayout' });

const links = [
  { name: 'courts', label: 'Courts' },
  { name: 'faq', label: 'FAQ' },
];
const menu = ref(false);
const year = new Date().getFullYear();

const auth = useAuthStore();
const clubStore = useClubStore();
const clubName = computed(() => clubStore.club?.name ?? 'Pickleball Club');
onMounted(() => clubStore.load());
</script>

<style scoped>
.site-header { background: var(--pbc-bg); color: var(--pbc-text); border-bottom: 1px solid var(--pbc-line); }
.site-header__bar { max-width: 1120px; margin: 0 auto; width: 100%; min-height: 64px; }
.brand { display: inline-flex; align-items: center; gap: 10px; font-size: 1.35rem; text-decoration: none; color: var(--pbc-ink); }
.brand__mark { border-radius: 7px; }
.mock-banner { background: var(--pbc-brass-surface); color: var(--pbc-brass-ink); text-align: center; font-size: 0.85rem; }
.connecting { min-height: 50vh; display: grid; place-content: center; justify-items: center; text-align: center; }
.connecting p { margin: 0; }
.connecting__title { margin-top: 16px; font-weight: 600; color: var(--pbc-ink); }
.site-footer { background: var(--pbc-ink-deep); color: rgba(255, 255, 255, 0.78); position: static; }
.site-footer__inner { display: flex; flex-wrap: wrap; gap: 12px; justify-content: space-between; padding-top: 24px; padding-bottom: 24px; font-size: 0.875rem; }
.site-footer a { color: inherit; }
</style>
