import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// Development only: the SPA and the API share one origin in production through the reverse proxy.
// Here Vite proxies /api and /health to the local API (no CORS needed). The API runs on HTTPS
// (ASP.NET Core development certificate) because the __Host- session and anti-forgery cookies are Secure-only.
export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    strictPort: true,
    proxy: {
      '/api': { target: 'https://localhost:5081', changeOrigin: false, secure: false },
      '/health': { target: 'https://localhost:5081', changeOrigin: false, secure: false },
    },
  },
  build: {
    sourcemap: false,
  },
});
