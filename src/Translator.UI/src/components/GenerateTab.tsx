import { useMemo, useState } from 'react';
import { useFormik } from 'formik';
import { ClaimTable } from './ClaimTable';
import { useGenerateBatch, useJurisdictions, useMedicalCodes } from '../data/queries';
import { writeClaims } from '../helpers/claimStore';
import { claimsToCsv } from '../helpers/csv';
import type { ClaimHeader } from '../helpers/claimFields';

/**
 * Governance Feature 1, at the client: the Model → 837 direction. A form, a table, and two exports.
 *
 * PROVENANCE: ADR-025 - every value the selectors offer comes from the server, so a dropdown cannot
 * offer something batch generation would refuse.
 */

/**
 * Governance User Story 1.3 caps a batch at 500, and the API answers 400 above it. The control is
 * built from that ceiling rather than allowing free entry, so the refusal is unreachable.
 */
const GOVERNED_CEILING = 500;
const BILL_COUNTS = [1, 5, 10, 25, 50, 100, 250, GOVERNED_CEILING] as const;

function download(blob: Blob, fileName: string) {
  const href = URL.createObjectURL(blob);
  const link = document.createElement('a');

  link.href = href;
  link.download = fileName;
  link.click();

  URL.revokeObjectURL(href);
}

export function GenerateTab() {
  const codes = useMedicalCodes();
  const jurisdictions = useJurisdictions();
  const generate = useGenerateBatch();
  const [claims, setClaims] = useState<ClaimHeader[]>([]);

  // Section 4a: derived during render. The catalogue arrives flat and the selector wants its
  // categories, which is a computation over query data rather than a second copy of it.
  const categories = useMemo(
    () => [...new Set((codes.data ?? []).map((code) => code.Category))].sort(),
    [codes.data],
  );

  const form = useFormik({
    initialValues: { BillCount: 5, JurisdictionState: '', MedicalCodeCategories: [] as string[] },
    onSubmit: async (values) => {
      const request = {
        BillCount: Number(values.BillCount),
        JurisdictionState: values.JurisdictionState || (jurisdictions.data?.[0]?.Code ?? ''),
        MedicalCodeCategories: values.MedicalCodeCategories.length > 0 ? values.MedicalCodeCategories : categories,
      };

      const generated = await generate.mutateAsync(request);

      setClaims(generated);
      writeClaims('generated', generated);
    },
  });

  const exportCsv = () => {
    download(new Blob([claimsToCsv(claims)], { type: 'text/csv' }), 'claims.csv');
  };

  /**
   * PROVENANCE: ADR-027 - Section 9: the client never produces an 837. The governed output comes
   * from the server, which is the only thing that has a serializer held to the Section 1
   * Reversibility Guarantee.
   */
  const export837 = async () => {
    const response = await fetch(`${import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:5000'}/api/v1/claims/export-zip`);
    const disposition = response.headers.get('Content-Disposition');
    const name = /filename="?([^";]+)"?/.exec(disposition ?? '')?.[1] ?? 'claims-837.zip';

    download(await response.blob(), name);
  };

  return (
    <section>
      <form onSubmit={form.handleSubmit}>
        <p>
          <label htmlFor="BillCount">Number of bills</label>
          <select id="BillCount" name="BillCount" value={form.values.BillCount} onChange={form.handleChange}>
            {BILL_COUNTS.map((count) => (
              <option key={count} value={count}>
                {count}
              </option>
            ))}
          </select>
        </p>

        <p>
          <label htmlFor="JurisdictionState">State</label>
          <select
            id="JurisdictionState"
            name="JurisdictionState"
            value={form.values.JurisdictionState}
            onChange={form.handleChange}
          >
            <option value="">Any</option>
            {(jurisdictions.data ?? []).map((jurisdiction) => (
              <option key={jurisdiction.Code} value={jurisdiction.Code}>
                {jurisdiction.Name}
              </option>
            ))}
          </select>
        </p>

        <p>
          <label htmlFor="MedicalCodeCategories">Medical code categories</label>
          <select
            id="MedicalCodeCategories"
            name="MedicalCodeCategories"
            multiple
            value={form.values.MedicalCodeCategories}
            onChange={form.handleChange}
          >
            {categories.map((category) => (
              <option key={category} value={category}>
                {category}
              </option>
            ))}
          </select>
        </p>

        <button type="submit" disabled={generate.isPending}>
          {generate.isPending ? 'Generating…' : 'Generate bills'}
        </button>
      </form>

      {generate.isError && <p role="alert">{(generate.error as Error).message}</p>}

      {claims.length > 0 && (
        <>
          <p>
            <button type="button" onClick={exportCsv}>
              Export CSV
            </button>
            <button type="button" onClick={export837}>
              Export 837
            </button>
          </p>
          <ClaimTable claims={claims} caption={`${claims.length} generated bills`} />
        </>
      )}
    </section>
  );
}
