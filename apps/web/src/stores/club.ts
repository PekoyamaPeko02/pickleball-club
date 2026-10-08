import { defineStore } from 'pinia';
import type { ClubInfo } from '@pbc/api';
import { useApi } from 'boot/api';

/** The Club's public information, fetched once per page load. */
export const useClubStore = defineStore('club', {
  state: () => ({ club: null as ClubInfo | null, error: '' }),
  actions: {
    async load() {
      if (this.club) return this.club;
      try {
        this.club = await useApi().getClub();
        this.error = '';
      } catch (e) {
        this.error = e instanceof Error ? e.message : 'Could not load the club information.';
      }
      return this.club;
    },
  },
});
