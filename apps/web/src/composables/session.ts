import { useRoute, useRouter } from 'vue-router';
import type { AuthResponse } from '@pbc/api';
import { useAuthStore } from 'stores/auth';

/** After a successful sign-in: remember the session and go where the person was heading (or to their account). */
export function useSignedIn() {
  const auth = useAuthStore();
  const route = useRoute();
  const router = useRouter();

  return (response: AuthResponse) => {
    auth.setSession(response);
    const redirect = typeof route.query.redirect === 'string' ? route.query.redirect : '';
    // Only follow in-site paths: a redirect that starts with // or a scheme would leave the site.
    return router.replace(redirect.startsWith('/') && !redirect.startsWith('//') ? redirect : { name: 'account' });
  };
}
