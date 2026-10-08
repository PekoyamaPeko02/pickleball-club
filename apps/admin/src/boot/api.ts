import { defineBoot } from '#q-app/wrappers';
import { createHttpApi, createMockApi, type PbcApi } from '@pbc/api';
import '@pbc/ui';
import { useAuthStore } from 'stores/auth';

export const isMockApi = process.env.API_MOCK === 'true';
let api: PbcApi;
export const useApi = () => api;

export default defineBoot(({ router }) => {
  const auth = useAuthStore();
  api = isMockApi
    ? createMockApi()
    : createHttpApi({
        baseUrl: `${process.env.API_BASE}/api/v1`,
        getToken: () => auth.token,
        // The session ended (expired, or the password was changed elsewhere): sign out and ask to sign in again.
        onUnauthorized: () => {
          if (!auth.isLoggedIn) return;
          auth.logout();
          void router.push({ name: 'login', query: { redirect: router.currentRoute.value.fullPath } });
        },
      });
});
