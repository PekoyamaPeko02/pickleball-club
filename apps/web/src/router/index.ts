import { defineRouter } from '#q-app/wrappers';
import { createRouter, createWebHashHistory, createWebHistory } from 'vue-router';
import routes from './routes';
import { useAuthStore } from 'stores/auth';

export default defineRouter(function () {
  const router = createRouter({
    scrollBehavior: (to) => (to.hash ? { el: to.hash, top: 80 } : { left: 0, top: 0 }),
    routes,
    history: (process.env.VUE_ROUTER_MODE === 'history' ? createWebHistory : createWebHashHistory)(process.env.VUE_ROUTER_BASE),
  });

  router.beforeEach((to) => {
    const loggedIn = useAuthStore().isLoggedIn;
    if (to.meta.auth && !loggedIn) return { name: 'login', query: { redirect: to.fullPath } };
    if (to.meta.guest && loggedIn) return { name: 'account' }; // already signed in
  });

  return router;
});
