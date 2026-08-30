import { useMemo, type ReactNode } from 'react';
import { CLAIM_COLUMNS, displayValue, headingFor, type ClaimHeader } from '../helpers/claimFields';
import { cn } from './ui';

/** Columns read character by character rather than as words. */
const IDENTIFIER_COLUMNS = new Set<keyof ClaimHeader>([
  'CLM01_ClaimControlNumber',
  'Loop2010AA_NM109_BillingProviderNpi',
  'HI01_2_PrincipalDiagnosisCode',
]);

/**
 * PROVENANCE: ADR-027 - frontend governance Section 9: the client never truncates a governed value
 * to fit a layout. The table scrolls sideways instead, and every cell keeps its whole value.
 *
 * The heading is a rendering of the governed name for a reader. CLAIM_COLUMNS remains what a column
 * *is*, and the CSV export carries the governed names rather than these headings.
 */
interface RowAction {
  heading: string;
  render: (claim: ClaimHeader) => ReactNode;
}

export function ClaimTable({
  claims,
  caption,
  action,
}: {
  claims: readonly ClaimHeader[];
  caption: string;
  action?: RowAction;
}) {
  // PROVENANCE: ADR-027 - Section 4a: derived during render, not synchronised into state by an
  // effect that would arrive one render late.
  const headings = useMemo(() => CLAIM_COLUMNS.map((column) => ({ column, label: headingFor(column) })), []);

  if (claims.length === 0) return null;

  return (
    <div className="overflow-x-auto rounded-lg border border-line">
      <table className="w-full border-collapse text-sm">
        <caption className="px-3 py-2 text-left text-sm text-muted">{caption}</caption>
        <thead>
          <tr className="border-b-2 border-accent/25 bg-accent-soft/60 text-accent-deep">
            {headings.map(({ column, label }) => (
              <th key={column} scope="col" className="whitespace-nowrap px-3 py-2 text-left font-medium">
                {label}
              </th>
            ))}
            {action && <th scope="col" className="px-3 py-2 text-left font-medium">{action.heading}</th>}
          </tr>
        </thead>
        <tbody>
          {claims.map((claim, index) => (
            <tr
              key={claim.Id ?? `${claim.CLM01_ClaimControlNumber}-${index}`}
              className={cn('transition-colors hover:bg-accent-soft/50', index % 2 === 1 && 'bg-surface')}
            >
              {CLAIM_COLUMNS.map((column) => (
                <td
                  key={column}
                  className={cn(
                    'whitespace-nowrap px-3 py-1.5 align-top',
                    // Identifiers and money are read a character at a time, so they get a face built
                    // for that and money is right-aligned where the eye expects to compare it.
                    IDENTIFIER_COLUMNS.has(column) && 'tabular',
                    column === 'CLM02_TotalClaimChargeAmount' && 'tabular text-right font-medium text-accent-deep',
                  )}
                >
                  {displayValue(claim, column)}
                </td>
              ))}
              {action && <td className="px-3 py-1.5 align-top">{action.render(claim)}</td>}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
