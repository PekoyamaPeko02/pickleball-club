import { defineStore } from 'pinia';
import type { AuthResponse, User } from '@pbc/api';

const KEY = 'pbc.admin.session';

interface Session {
  token: string;
  user: User;
}

function read(): Session | null {
  try {
    const raw = localStorage.getItem(KEY);
    return raw ? (JSON.parse(raw) as Session) : null;
  } catch {
    return null; // storage blocked or corrupt: start signed out
  }
}

function write(session: Session | null) {
  try {
    if (session) localStorage.setItem(KEY, JSON.stringify(session));
    else localStorage.removeItem(KEY);
  } catch {
    /* storage blocked: the session lasts until the tab closes */
  }
}

/** The signed-in Admin. Kept in localStorage so a reload stays signed in. */
export const useAuthStore = defineStore('auth', {
  state: () => {
    const session = read();
    return { token: session?.token ?? null as string | null, user: session?.user ?? null as User | null };
  },
  getters: {
    isLoggedIn: (s) => s.token !== null,
  },
  actions: {
    setSession(auth: AuthResponse) {
      this.token = auth.accessToken;
      this.user = auth.user;
      write({ token: auth.accessToken, user: auth.user });
    },
    logout() {
      this.token = null;
      this.user = null;
      write(null);
    },
  },
});
