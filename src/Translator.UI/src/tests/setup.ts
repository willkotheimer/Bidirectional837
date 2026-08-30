import '@testing-library/jest-dom/vitest';
import { cleanup } from '@testing-library/react';
import { afterEach } from 'vitest';

// Testing Library registers its own cleanup through the global afterEach, which vitest only exposes
// when `globals` is on. It is off here - an imported `it` and `expect` are easier to trace than
// ambient ones - so cleanup is registered by hand. Without it every render in an it.each accumulates
// and queries start finding several of everything.
afterEach(cleanup);
