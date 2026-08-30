import { useState } from 'react';
import { ClaimTable } from './ClaimTable';
import { Alert, Button, Panel, Verdict } from './ui';
import { API_BASE_URL, ApiError, messageFor } from '../data/api';
import { writeClaims } from '../helpers/claimStore';
import { claimsToCsv } from '../helpers/csv';
import { downloadBlob } from '../helpers/download';
import type { ClaimHeader } from '../helpers/claimFields';

/**
 * Governance Feature 3 at the client, User Story 3.1: an 837 file or a ZIP of them is uploaded and
 * the bills it reconstructs are displayed.
 *
 * PROVENANCE: ADR-022 - an import applies whole or not at all, so a refusal leaves the table exactly
 * as it was rather than partly replacing it.
 */

/** The per-claim verdict `POST /api/v1/claims/{id}/verify-reversibility` returns. */
interface ReversibilityVerdict {
  EdiTextIsIdentical: boolean;
  RecordIsIdentical: boolean;
  Differences: string[];
}

async function importFile(file: File): Promise<ClaimHeader[]> {
  const body = new FormData();
  body.append('file', file);

  const response = await fetch(`${API_BASE_URL}/api/v1/claims/import`, { method: 'POST', body });

  if (!response.ok) {
    const problem = await response.json().catch(() => null);
    throw new ApiError(messageFor(problem, 'The import was refused.'), response.status);
  }

  return (await response.json()) as ClaimHeader[];
}

export function ImportTab() {
  const [claims, setClaims] = useState<ClaimHeader[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [verdicts, setVerdicts] = useState<Record<string, ReversibilityVerdict>>({});

  const onFileChosen = async (file: File | undefined) => {
    if (!file) return;

    setBusy(true);
    setError(null);

    try {
      const imported = await importFile(file);

      setClaims(imported);
      setVerdicts({});
      writeClaims('imported', imported);
    } catch (failure) {
      // ADR-022: nothing is applied, so nothing on screen changes but the message.
      setError((failure as Error).message);
    } finally {
      setBusy(false);
    }
  };

  /**
   * PROVENANCE: ADR-023 - verification is per claim and only when asked. There is no bulk verify
   * endpoint, so verifying a whole table would be one request per claim, which is the anti-pattern
   * a whole section was spent removing from provider lookups.
   */
  const verify = async (claim: ClaimHeader) => {
    const response = await fetch(`${API_BASE_URL}/api/v1/claims/${claim.Id}/verify-reversibility`, {
      method: 'POST',
    });

    if (!response.ok) return;

    const verdict = (await response.json()) as ReversibilityVerdict;

    setVerdicts((held) => ({ ...held, [claim.CLM01_ClaimControlNumber]: verdict }));
  };

  return (
    <Panel
      title="837 → Model"
      description="Upload a single 837 file, or a ZIP archive of them. Every claim is reconstructed into the governed
        schema and shown below; a refused file changes nothing."
    >
      <div className="flex flex-wrap items-end gap-4">
        <label className="flex flex-col gap-1.5" htmlFor="interchange">
          <span className="text-sm font-medium">837 file or ZIP archive</span>
          <input
            id="interchange"
            type="file"
            accept=".837,.txt,.zip,application/zip,text/plain"
            disabled={busy}
            onChange={(event) => void onFileChosen(event.target.files?.[0])}
            className="text-sm file:mr-3 file:rounded-md file:border file:border-line file:bg-white
              file:px-3 file:py-1.5 file:text-sm file:font-medium hover:file:bg-surface"
          />
        </label>

        {claims.length > 0 && (
          <Button
            type="button"
            onClick={() => downloadBlob(new Blob([claimsToCsv(claims)], { type: 'text/csv' }), 'imported-claims.csv')}
          >
            Export CSV
          </Button>
        )}
      </div>

      {busy && (
        <p className="mt-4 flex items-center gap-2 text-sm text-accent">
          <span aria-hidden="true" className="h-2 w-2 animate-pulse rounded-full bg-accent" />
          Reading the interchange…
        </p>
      )}
      {error && <Alert>{error}</Alert>}

      {claims.length > 0 && (
        <div className="mt-5">
          <ClaimTable
            claims={claims}
            caption={`${claims.length} bill${claims.length === 1 ? '' : 's'} reconstructed`}
            action={{
              heading: 'Reversibility',
              render: (claim) => {
                const verdict = verdicts[claim.CLM01_ClaimControlNumber];

                if (!verdict) {
                  return (
                    <Button type="button" onClick={() => void verify(claim)}>
                      Verify
                    </Button>
                  );
                }

                return (
                  <span data-testid={`verdict-${claim.CLM01_ClaimControlNumber}`} className="flex flex-col gap-1">
                    {/*
                      PROVENANCE: ADR-027 - Section 9. The two verdicts are shown separately or not at
                      all. The endpoint compares the stored record against its own re-export and never
                      sees the bytes the user uploaded, so a claim can be perfectly preserved while
                      the text differs. One tick would hide that.
                    */}
                    <Verdict label="Text" identical={verdict.EdiTextIsIdentical} />
                    <Verdict label="Record" identical={verdict.RecordIsIdentical} />
                    {verdict.Differences.map((difference) => (
                      <span key={difference} className="max-w-xs text-xs text-moved">
                        {difference}
                      </span>
                    ))}
                  </span>
                );
              },
            }}
          />
        </div>
      )}
    </Panel>
  );
}
