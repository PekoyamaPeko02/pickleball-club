import { defineStore } from 'pinia';
import type { ClubDate } from '@pbc/api';

/** The Court Rental the Customer picked on the booking page and is about to pay for. */
export interface Selection {
  courtId: string;
  courtName: string;
  date: ClubDate;
  startHour: number;
  hours: number;
  /** As shown on the grid; the server prices the Booking again. */
  total: number;
}

const KEY = 'pbc.cart';

function read(): Selection | null {
  try {
    const raw = sessionStorage.getItem(KEY);
    return raw ? (JSON.parse(raw) as Selection) : null;
  } catch {
    return null;
  }
}

/** Kept in sessionStorage so it survives the detour through sign-in. */
export const useCartStore = defineStore('cart', {
  state: () => ({ selection: read() }),
  actions: {
    set(selection: Selection | null) {
      this.selection = selection;
      try {
        if (selection) sessionStorage.setItem(KEY, JSON.stringify(selection));
        else sessionStorage.removeItem(KEY);
      } catch {
        /* storage blocked: the selection lasts until the page reloads */
      }
    },
  },
});
