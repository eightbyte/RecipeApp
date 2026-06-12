import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import vuetify from 'vite-plugin-vuetify'
import { VitePWA } from 'vite-plugin-pwa'
import { fileURLToPath, URL } from 'node:url'

// The service worker / Workbox machinery is irrelevant under Vitest (jsdom) and
// only slows test startup — load the PWA plugin for dev/build only.
const isTest = process.env.VITEST === 'true'

// https://vite.dev/config/
export default defineConfig({
  plugins: [
    vue(),
    vuetify({ autoImport: true }),
    ...(isTest ? [] : [
      VitePWA({
        registerType: 'autoUpdate',
        includeAssets: ['favicon.svg', 'icons.svg'],
        manifest: {
          name: 'RecipeApp',
          short_name: 'RecipeApp',
          description: 'Store recipes, plan meals, and generate shopping lists.',
          theme_color: '#2E7D32',       // matches recipeLight primary / index.html theme-color
          background_color: '#F5F5F5',  // matches the theme background
          display: 'standalone',
          start_url: '/',
          icons: [
            { src: '/icons/pwa-192.png', sizes: '192x192', type: 'image/png' },
            { src: '/icons/pwa-512.png', sizes: '512x512', type: 'image/png' },
            { src: '/icons/pwa-maskable-512.png', sizes: '512x512', type: 'image/png', purpose: 'maskable' },
          ],
        },
        workbox: {
          navigateFallback: '/index.html',
          runtimeCaching: [
            {
              // Recipe API reads — viewed recipes & the list become available offline.
              urlPattern: ({ url }) => url.pathname.includes('/api/v1/recipes'),
              handler: 'StaleWhileRevalidate',
              options: {
                cacheName: 'recipe-api',
                expiration: { maxEntries: 100, maxAgeSeconds: 60 * 60 * 24 * 7 },
                cacheableResponse: { statuses: [0, 200] },
              },
            },
            {
              // Recipe images served from the backend uploads path.
              urlPattern: ({ url }) => url.pathname.startsWith('/uploads/images/'),
              handler: 'CacheFirst',
              options: {
                cacheName: 'recipe-images',
                expiration: { maxEntries: 100, maxAgeSeconds: 60 * 60 * 24 * 30 },
                cacheableResponse: { statuses: [0, 200] },
              },
            },
          ],
        },
        devOptions: { enabled: false },  // SW disabled in `npm run dev`; enable ad-hoc to test
      }),
    ]),
  ],

  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
    },
  },

  server: {
    port: 3000,
    proxy: {
      // Forward /api requests to the .NET backend during development
      '/api': {
        target: 'http://localhost:5000',
        changeOrigin: true,
      },
    },
  },

  test: {
    environment: 'jsdom',
    globals: true,
    setupFiles: ['./src/test/setup.js'],
    env: {
      VITE_API_BASE_URL: 'http://localhost:5000/api/v1',
    },
    server: {
      deps: {
        inline: ['vuetify'],
      },
    },
  },
})
