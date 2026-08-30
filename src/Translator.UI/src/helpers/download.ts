/**
 * Hands a blob to the browser as a download.
 *
 * PROVENANCE: ADR-027 - keeping the artefact reachable afterwards is out of scope for the MVP, so
 * the object URL is revoked as soon as the browser has taken it. A page cannot link to the user's
 * downloads folder in any case: it never learns the path the browser chose, and file:// navigation
 * is blocked from https://.
 */
export function downloadBlob(blob: Blob, fileName: string): void {
  const href = URL.createObjectURL(blob);
  const link = document.createElement('a');

  link.href = href;
  link.download = fileName;
  link.click();

  URL.revokeObjectURL(href);
}
