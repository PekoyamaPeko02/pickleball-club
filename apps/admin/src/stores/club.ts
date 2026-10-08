import { defineStore } from 'pinia';
import { untilReachable, type ClubInfo } from '@pbc/api';
import { useApi } from 'boot/api';

/** The Club's public information, fetched once per page load. */
export const useClubStore = defineStore('club', {
  state: () => ({
    club: null as ClubInfo | null,
    error: '',
    /** The server is not answering yet (it may be starting up) and we are still trying. */
    connecting: false,
  }),
  actions: {
    /** @param fresh Fetch again even when it is already loaded (after the Admin changed a setting). */
    async load(fresh = false) {
      if (this.club && !fresh) return this.club;
      try {
        // Only the first load waits for a sleeping server (the layout shows "Connecting…" instead of the page);
        // a refresh after a change must never take the page away from the Admin.
        this.club = await untilReachable(() => useApi().getClub(),
          this.club ? { tries: 1 } : { onWaiting: (waiting) => { this.connecting = waiting; } });
        this.error = '';
      } catch (e) {
        this.error = e instanceof Error ? e.message : 'Could not load the club information.';
      }
      return this.club;
    },
  },
});
