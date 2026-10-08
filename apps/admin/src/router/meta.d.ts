import 'vue-router';

declare module 'vue-router' {
  interface RouteMeta {
    /** Only for a signed-in Admin; others are sent to sign in first. */
    auth?: boolean;
  }
}
