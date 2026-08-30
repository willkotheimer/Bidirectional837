import react from '@vitejs/plugin-react'
import { defineConfig } from 'vitest/config'

// PROVENANCE: ADR-028 - the dev server runs on 5173, which is the origin the API grants CORS to.
// Pinned rather than left to pick a free port, because a port the API does not name is a port the
// browser refuses.
export default defineConfig({
  plugins: [react()],
  server: { port: 5173, strictPort: true },
  test: {
    // The default forks pool times out waiting for workers on this Windows machine; threads is the
    // supported alternative and runs the same suite.
    pool: 'threads',
    environment: 'jsdom',
    globals: false,
    setupFiles: ['./src/tests/setup.ts'],
    include: ['src/**/*.test.{ts,tsx}'],
  },
})
