import { CLAIM_COLUMNS, displayValue, type ClaimHeader } from './claimFields';

/**
 * PROVENANCE: ADR-027 - CSV is a view of the table, not a governed artefact. The 837 is the governed
 * output and only the server produces it, so nothing here makes a fidelity claim.
 */

const NEEDS_QUOTING = /[",\r\n]/;

/**
 * One CSV field, quoted if it needs to be.
 *
 * PROVENANCE: FIND-019 - the backend's seed reader split on every comma and documented the
 * assumption that its data carried no quoted fields, which stopped being true the moment the data
 * was real. A CSV writer that does not quote is the same defect facing the other way: provider
 * names contain commas, and CMS descriptions contain both commas and quotes.
 */
export function csvField(value: unknown): string {
  if (value === null || value === undefined) return '';

  const text = String(value);

  return NEEDS_QUOTING.test(text) ? `"${text.replaceAll('"', '""')}"` : text;
}

/** A CSV row from already-prepared fields. */
export function csvRow(fields: readonly unknown[]): string {
  return fields.map(csvField).join(',');
}

/**
 * The whole claim table as CSV: a heading row, then one row per claim.
 *
 * The heading carries the governed column names rather than the readable headings, because a CSV is
 * usually opened by something that will process it. Governance Section 1 wants the ASC X12 names to
 * survive into anything downstream, and a column called "Total Claim Charge Amount" has lost the
 * link to CLM02 that the governed name carries.
 */
export function claimsToCsv(claims: readonly ClaimHeader[]): string {
  const heading = csvRow(CLAIM_COLUMNS);
  const rows = claims.map((claim) => csvRow(CLAIM_COLUMNS.map((column) => displayValue(claim, column))));

  return [heading, ...rows].join('\n');
}
