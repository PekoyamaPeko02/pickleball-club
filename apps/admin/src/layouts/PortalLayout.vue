<template>
  <q-layout view="hHh Lpr lFf">
    <q-header class="portal-header">
      <q-toolbar>
        <q-btn flat round icon="menu" aria-label="Toggle menu" @click="drawer = !drawer" />
        <q-toolbar-title class="pbc-display">{{ clubName }} <span class="portal-header__tag">Admin</span></q-toolbar-title>
        <span class="gt-xs q-mr-sm portal-header__user">{{ auth.user?.displayName ?? auth.user?.email }}</span>
        <q-btn flat no-caps label="Sign out" @click="signOut" />
      </q-toolbar>
    </q-header>

    <q-drawer v-model="drawer" show-if-above bordered :width="220">
      <q-list padding>
        <q-item v-for="link in links" :key="link.name" v-ripple clickable exact :to="{ name: link.name }" active-class="portal-nav--active">
          <q-item-section avatar><q-icon :name="link.icon" /></q-item-section>
          <q-item-section>{{ link.label }}</q-item-section>
        </q-item>
      </q-list>
    </q-drawer>

    <q-page-container>
      <q-banner v-if="isMockApi" dense class="mock-banner">Demo data — the back office needs the server to do anything.</q-banner>
      <router-view />
    </q-page-container>
  </q-layout>
</template>

<script setup lang="ts">
import { computed, onMounted, ref } from 'vue';
import { useRouter } from 'vue-router';
import { isMockApi } from 'boot/api';
import { useAuthStore } from 'stores/auth';
import { useClubStore } from 'stores/club';

defineOptions({ name: 'PortalLayout' });

const links = [
  { name: 'schedule', label: 'Schedule', icon: 'calendar_month' },
  { name: 'refunds', label: 'Refunds to make', icon: 'currency_exchange' },
  { name: 'reports', label: 'Revenue', icon: 'bar_chart' },
  { name: 'settings', label: 'Settings', icon: 'settings' },
];
const drawer = ref(false);

const router = useRouter();
const auth = useAuthStore();
const clubStore = useClubStore();
const clubName = computed(() => clubStore.club?.name ?? 'Pickleball Club');
onMounted(() => clubStore.load());

function signOut() {
  auth.logout();
  void router.push({ name: 'login' });
}
</script>

<style scoped>
.portal-header { background: var(--pbc-ink-deep); }
.portal-header__tag { font-family: var(--pbc-font-body); font-size: 0.7rem; font-weight: 600; letter-spacing: 0.08em; text-transform: uppercase; opacity: 0.7; margin-left: 8px; }
.portal-header__user { font-size: 0.85rem; opacity: 0.8; }
.portal-nav--active { color: var(--pbc-ink); background: var(--pbc-surface-2); font-weight: 600; }
.mock-banner { background: var(--pbc-brass-surface); color: var(--pbc-brass-ink); text-align: center; font-size: 0.85rem; }
</style>
