import type { ClaimHeader } from './claimFields';

/**
 * PROVENANCE: ADR-027 - frontend governance Section 5. Local storage is the only thing that ever
 * saves a bill, and it is cleared on page load.
 *
 * The effect is a clean slate every time the page opens; the mechanism is one named place rather
 * than component state, so both tabs are views over a single working set. It agrees with the
 * server: ADR-015 makes the store ephemeral and a restart a clean slate, so a client remembering
 * more than the server does would show claims that no longer exist.
 */

/** Where each tab's working set lives. Both tabs are views over this one store. */
export type StoreKey = 'generated' | 'imported';

const KEYS: readonly StoreKey[] = ['generated', 'imported'];

const nameOf = (key: StoreKey) => `translator.claims.${key}`;

/**
 * Every access is wrapped.
 *
 * Local storage throws rather than returning null in a private window or where site data is
 * blocked. A translator that will not render because it cannot save a draft is worse than one that
 * simply does not save, so a failure here is absorbed and the page carries on with no store.
 */
function attempt<T>(action: () => T, whenUnavailable: T): T {
  try {
    return action();
  } catch {
    return whenUnavailable;
  }
}

/** Clears every bill the store holds. Called once at startup, before anything reads. */
export function clearAllClaims(): void {
  attempt(() => KEYS.forEach((key) => localStorage.removeItem(nameOf(key))), undefined);
}

/** The claims held under a key, or none if the store is unreadable or empty. */
export function readClaims(key: StoreKey): ClaimHeader[] {
  return attempt(() => {
    const stored = localStorage.getItem(nameOf(key));
    if (!stored) return [];

    const parsed: unknown = JSON.parse(stored);

    // Anything that is not the array we wrote is treated as nothing. The store is a cache of this
    // session's work, so discarding an unreadable value costs nothing and guessing at it could
    // put a malformed claim in front of a user as though the server had sent it.
    return Array.isArray(parsed) ? (parsed as ClaimHeader[]) : [];
  }, []);
}

/** Replaces the claims held under a key. */
export function writeClaims(key: StoreKey, claims: readonly ClaimHeader[]): void {
  attempt(() => localStorage.setItem(nameOf(key), JSON.stringify(claims)), undefined);
}
