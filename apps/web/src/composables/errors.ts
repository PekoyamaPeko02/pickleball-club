import { ApiError } from '@pbc/api';

/** The sentence to show for a failed call. */
export const messageOf = (e: unknown, fallback = 'Something went wrong. Please try again.') =>
  e instanceof ApiError || e instanceof Error ? e.message || fallback : fallback;

/** First validation message for a request field (`validation_failed`), or '' when there is none. */
export const fieldError = (e: unknown, field: string) => (e instanceof ApiError ? e.errors[field]?.[0] ?? '' : '');
