import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// In development the app talks to /api and Vite proxies it to the .NET API, so the browser
// sees a single origin and no CORS preflight sits in front of every validation call.
// A project page on GitHub Pages is served from /<repository>/, so the asset URLs have to
// carry that prefix; everywhere else the app sits at the root of its origin.
export default defineConfig({
  base: process.env.VITE_BASE_PATH ?? '/',
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      '/api': {
        target: process.env.VITE_API_PROXY_TARGET ?? 'http://localhost:5080',
        changeOrigin: true,
      },
    },
  },
  build: {
    outDir: 'dist',
    sourcemap: true,
  },
});
