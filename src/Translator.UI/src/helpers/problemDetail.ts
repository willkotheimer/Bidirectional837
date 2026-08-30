/**
 * PROVENANCE: ADR-027 - frontend governance Section 4: errors surface the server's problem document
 * `detail` verbatim.
 *
 * The reader refuses a malformed 837 by naming the segment at fault (ADR-021), and batch generation
 * names the categories it does not know. Replacing either with "request failed" throws away the only
 * part a user can act on.
 */

/** An RFC 7807 problem document, as the API serves for every 400 and 404. */
export interface ProblemDocument {
  title?: string;
  detail?: string;
  status?: number;
  errors?: Record<string, string[]>;
}

/**
 * The message to show a user for a failed request: the server's own words where it gave any, and a
 * statement of what failed where it gave none.
 */
export function messageFor(_problem: unknown, _fallback: string): string {
  throw new Error('not implemented');
}
