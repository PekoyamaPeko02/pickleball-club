import 'vue-router';

declare module 'vue-router' {
  interface RouteMeta {
    /** Only for a signed-in Customer; others are sent to sign in first. */
    auth?: boolean;
    /** Only for someone who is not signed in (sign-in, register). */
    guest?: boolean;
  }
}
