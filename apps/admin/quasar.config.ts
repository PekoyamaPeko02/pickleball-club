// https://v2.quasar.dev/quasar-cli-vite/quasar-config-file
import { defineConfig } from '#q-app/wrappers';

export default defineConfig(() => ({
  boot: ['api'],
  css: ['app.scss'],
  extras: ['material-icons'],

  build: {
    target: { browser: ['es2022', 'firefox115', 'chrome115', 'safari15'], node: 'node22' },
    typescript: { strict: true, vueShim: true },
    vueRouterMode: 'history',
    env: {
      // Mock API (in-browser) unless API_BASE is given: `API_BASE=http://localhost:5090 pnpm dev`
      API_BASE: process.env.API_BASE ?? '',
      API_MOCK: process.env.API_MOCK ?? (process.env.API_BASE ? 'false' : 'true'),
    },
    extendViteConf(viteConf) {
      // workspace packages ship .vue/.ts sources
      viteConf.optimizeDeps = { ...viteConf.optimizeDeps, exclude: ['@pbc/ui', '@pbc/api'] };
    },
  },

  devServer: { port: 9011, open: false },

  framework: {
    config: {
      brand: {
        primary: '#14233b',
        secondary: '#2b5a85',
        accent: '#e3a72b',
        dark: '#0d1829',
        positive: '#23705a',
        negative: '#b3261e',
        info: '#3b6ea5',
        warning: '#c7861b',
      },
      notify: { position: 'top' },
    },
    lang: 'en-US',
    plugins: ['Notify', 'Dialog', 'Loading'],
  },

  animations: [],
}));
