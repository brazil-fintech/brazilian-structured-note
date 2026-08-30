import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// In development the app talks to /api and Vite proxies it to the .NET API, so the browser
// sees a single origin and no CORS preflight sits in front of every validation call.
export default defineConfig({
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
