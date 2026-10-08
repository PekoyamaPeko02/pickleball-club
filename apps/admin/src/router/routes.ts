import type { RouteRecordRaw } from 'vue-router';

const routes: RouteRecordRaw[] = [
  {
    path: '/',
    component: () => import('layouts/PortalLayout.vue'),
    meta: { auth: true },
    children: [
      { path: '', name: 'schedule', component: () => import('pages/SchedulePage.vue') },
      { path: 'refunds', name: 'refunds', component: () => import('pages/RefundsPage.vue') },
      { path: 'reports', name: 'reports', component: () => import('pages/ReportsPage.vue') },
      { path: 'settings', name: 'settings', component: () => import('pages/SettingsPage.vue') },
    ],
  },
  {
    path: '/',
    component: () => import('layouts/BlankLayout.vue'),
    children: [
      { path: 'login', name: 'login', component: () => import('pages/LoginPage.vue') },
      { path: 'forgot-password', name: 'forgot-password', component: () => import('pages/ForgotPasswordPage.vue') },
      { path: 'reset-password', name: 'reset-password', component: () => import('pages/ResetPasswordPage.vue') },
    ],
  },
  { path: '/:catchAll(.*)*', redirect: '/' },
];
export default routes;
