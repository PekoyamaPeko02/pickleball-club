import { ApiError } from './client';

export interface ReachOptions {
  /** Told `true` once the server has kept us waiting or could not be reached, and `false` when that wait is over. */
  onWaiting?: (waiting: boolean) => void;
  /** How many times to call before giving up. */
  tries?: number;
  /** Pause between two tries. */
  pauseMs?: number;
  /** A call that has not answered after this long counts as waiting. */
  slowAfterMs?: number;
}

/**
 * Makes a call to a server that may be asleep. A host that stops idle services needs a minute or two to start one again
 * (on a fraction of a CPU the API alone takes half a minute to boot); until then requests hang or fail. Keeps calling
 * while the server cannot be reached — any answer, even an error, ends the wait — and gives up after three minutes.
 */
export async function untilReachable<T>(call: () => Promise<T>, opts: ReachOptions = {}): Promise<T> {
  const { onWaiting, tries = 36, pauseMs = 5000, slowAfterMs = 4000 } = opts;
  let waiting = false;
  const startWaiting = () => {
    if (waiting) return;
    waiting = true;
    onWaiting?.(true);
  };
  const slow = setTimeout(startWaiting, slowAfterMs);
  try {
    for (let attempt = 1; ; attempt++) {
      try {
        return await call();
      } catch (e) {
        if (!(e instanceof ApiError && e.code === 'network_error') || attempt >= tries) throw e;
        startWaiting();
        await new Promise((resolve) => setTimeout(resolve, pauseMs));
      }
    }
  } finally {
    clearTimeout(slow);
    if (waiting) onWaiting?.(false);
  }
}
