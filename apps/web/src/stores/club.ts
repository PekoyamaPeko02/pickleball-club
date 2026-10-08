import { defineStore } from 'pinia';
import { untilReachable, type ClubInfo } from '@pbc/api';
import { useApi } from 'boot/api';

// The layout and the page ask at the same moment: one request (and one wait) serves both.
let loading: Promise<ClubInfo | null> | null = null;

/** The Club's public information, fetched once per page load. */
export const useClubStore = defineStore('club', {
  state: () => ({
    club: null as ClubInfo | null,
    error: '',
    /** The server is not answering yet (it may be starting up) and we are still trying. */
    connecting: false,
  }),
  actions: {
    load() {
      if (this.club) return Promise.resolve(this.club);
      loading ??= (async () => {
        try {
          this.club = await untilReachable(() => useApi().getClub(), { onWaiting: (waiting) => { this.connecting = waiting; } });
          this.error = '';
        } catch (e) {
          this.error = e instanceof Error ? e.message : 'Could not load the club information.';
        } finally {
          loading = null;
        }
        return this.club;
      })();
      return loading;
    },
  },
});
