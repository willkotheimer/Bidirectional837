import { useMemo } from 'react';
import { CLAIM_COLUMNS, displayValue, headingFor, type ClaimHeader } from '../helpers/claimFields';

/**
 * PROVENANCE: ADR-027 - frontend governance Section 9: the client never truncates a governed value
 * to fit a layout. Wrapping and column width are the table's problem; the value arrives whole.
 *
 * The heading is a rendering of the governed name for a reader. CLAIM_COLUMNS remains what a column
 * *is*, and the CSV export carries the governed names rather than these headings.
 */
export function ClaimTable({ claims, caption }: { claims: readonly ClaimHeader[]; caption: string }) {
  // PROVENANCE: ADR-027 - Section 4a: derived during render, not synchronised into state by an
  // effect that would arrive one render late.
  const headings = useMemo(() => CLAIM_COLUMNS.map((column) => ({ column, label: headingFor(column) })), []);

  if (claims.length === 0) return null;

  return (
    <table>
      <caption>{caption}</caption>
      <thead>
        <tr>
          {headings.map(({ column, label }) => (
            <th key={column} scope="col">
              {label}
            </th>
          ))}
        </tr>
      </thead>
      <tbody>
        {claims.map((claim, index) => (
          <tr key={claim.Id ?? `${claim.CLM01_ClaimControlNumber}-${index}`}>
            {CLAIM_COLUMNS.map((column) => (
              <td key={column}>{displayValue(claim, column)}</td>
            ))}
          </tr>
        ))}
      </tbody>
    </table>
  );
}
