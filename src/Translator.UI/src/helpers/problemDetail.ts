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

const meaningful = (value: unknown): value is string =>
  typeof value === 'string' && value.trim().length > 0;

/**
 * The message to show a user for a failed request: the server's own words where it gave any, and a
 * statement of what failed where it gave none.
 *
 * `detail` is preferred over `title` because `title` is the status phrase - "Bad Request" tells a
 * user nothing they did not already know from the request having failed.
 */
export function messageFor(problem: unknown, fallback: string): string {
  if (problem === null || typeof problem !== 'object') return fallback;

  const document = problem as ProblemDocument;

  if (meaningful(document.detail)) return document.detail.trim();
  if (meaningful(document.title)) return document.title.trim();

  // Model validation answers with a map of field to messages rather than a detail. Every message is
  // shown: a form rejected for two reasons should not report one of them.
  const validation = Object.values(document.errors ?? {})
    .flat()
    .filter(meaningful)
    .map((message) => message.trim());

  return validation.length > 0 ? validation.join(' ') : fallback;
}
