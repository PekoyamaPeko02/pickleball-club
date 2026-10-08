<template>
  <div>
    <div v-if="clientId" ref="holder" class="google" />
    <q-btn v-else outline no-caps disable class="full-width" label="Continue with Google">
      <q-tooltip>Sign in with Google is not set up yet.</q-tooltip>
    </q-btn>
    <p v-if="!clientId" class="pbc-muted hint">Sign in with Google is not available yet.</p>
    <p v-if="failed" class="text-negative hint" role="alert">Could not load Google sign-in. Check your connection and reload.</p>
  </div>
</template>

<script setup lang="ts">
import { onMounted, ref, watch } from 'vue';

defineOptions({ name: 'GoogleButton' });

// Google Identity Services: https://developers.google.com/identity/gsi/web
// The button is rendered by Google's own script; it hands back an ID token ("credential") that the API verifies.
const props = defineProps<{ clientId: string | null }>();
const emit = defineEmits<{ credential: [credential: string] }>();

interface GoogleId {
  initialize(options: { client_id: string; callback: (response: { credential: string }) => void }): void;
  renderButton(parent: HTMLElement, options: Record<string, unknown>): void;
}
type WithGoogle = Window & { google?: { accounts: { id: GoogleId } } };

const SRC = 'https://accounts.google.com/gsi/client';
const holder = ref<HTMLElement | null>(null);
const failed = ref(false);

function loadScript(): Promise<void> {
  if ((window as WithGoogle).google) return Promise.resolve();
  return new Promise((resolve, reject) => {
    const existing = document.querySelector<HTMLScriptElement>(`script[src="${SRC}"]`);
    const script = existing ?? Object.assign(document.createElement('script'), { src: SRC, async: true });
    script.addEventListener('load', () => resolve());
    script.addEventListener('error', () => reject(new Error('google script failed')));
    if (!existing) document.head.appendChild(script);
  });
}

async function render() {
  if (!props.clientId || !holder.value) return;
  try {
    await loadScript();
    const id = (window as WithGoogle).google!.accounts.id;
    id.initialize({ client_id: props.clientId, callback: (response) => emit('credential', response.credential) });
    id.renderButton(holder.value, { theme: 'outline', size: 'large', text: 'continue_with', width: 320 });
  } catch {
    failed.value = true;
  }
}

onMounted(render);
watch(() => props.clientId, render, { flush: 'post' });
</script>

<style scoped>
.google { display: flex; justify-content: center; min-height: 44px; }
.hint { font-size: 0.8rem; margin: 6px 0 0; text-align: center; }
</style>
