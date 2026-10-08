import type { RouteRecordRaw } from 'vue-router';

const routes: RouteRecordRaw[] = [
  {
    path: '/',
    component: () => import('layouts/MainLayout.vue'),
    children: [
      { path: '', name: 'home', component: () => import('pages/HomePage.vue') },
      { path: 'courts', name: 'courts', component: () => import('pages/CourtsPage.vue') },
      { path: 'faq', name: 'faq', component: () => import('pages/FaqPage.vue') },
      { path: 'book', name: 'book', component: () => import('pages/BookPage.vue') },
      { path: 'login', name: 'login', component: () => import('pages/LoginPage.vue'), meta: { guest: true } },
      { path: 'register', name: 'register', component: () => import('pages/RegisterPage.vue'), meta: { guest: true } },
      { path: 'forgot-password', name: 'forgot-password', component: () => import('pages/ForgotPasswordPage.vue') },
      { path: 'reset-password', name: 'reset-password', component: () => import('pages/ResetPasswordPage.vue') },
      { path: 'verify-email', name: 'verify-email', component: () => import('pages/VerifyEmailPage.vue') },
      { path: 'checkout', name: 'checkout', component: () => import('pages/CheckoutPage.vue'), meta: { auth: true } },
      { path: 'bookings/:id', name: 'booking', component: () => import('pages/BookingPage.vue'), meta: { auth: true } },
      { path: 'bookings/:id/move', name: 'reschedule', component: () => import('pages/ReschedulePage.vue'), meta: { auth: true } },
      { path: 'account', name: 'account', component: () => import('pages/AccountPage.vue'), meta: { auth: true } },
      { path: 'privacy', name: 'privacy', component: () => import('pages/PolicyPage.vue'), props: { kind: 'privacy' } },
      { path: 'terms', name: 'terms', component: () => import('pages/PolicyPage.vue'), props: { kind: 'terms' } },
      { path: ':catchAll(.*)*', name: 'not-found', component: () => import('pages/ErrorNotFound.vue') },
    ],
  },
];
export default routes;
