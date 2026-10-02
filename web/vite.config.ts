import { defineConfig } from 'vite';
import { svelte } from '@sveltejs/vite-plugin-svelte';

// API_TARGET=https://sts2stats.fly.dev npm run dev で、本番のデータを読んで手元の UI を確認できる (読むだけ)
const target = process.env.API_TARGET ?? 'http://localhost:8080';
const remote = target.startsWith('https://');

export default defineConfig({
  plugins: [svelte()],
  server: {
    port: 5173,
    proxy: {
      '/api': { target, changeOrigin: remote },
      ...(remote ? { '/catalog.ja.json': { target, changeOrigin: true } } : {}),
    },
  },
});
