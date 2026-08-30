import { useMemo, useState } from 'react';
import { useFormik } from 'formik';
import { ClaimTable } from './ClaimTable';
import { Alert, Button, Field, Panel } from './ui';
import { useClaimArchive, useGenerateBatch, useJurisdictions, useMedicalCodes } from '../data/queries';
import { writeClaims } from '../helpers/claimStore';
import { claimsToCsv } from '../helpers/csv';
import { downloadBlob } from '../helpers/download';
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

export function GenerateTab() {
  const codes = useMedicalCodes();
  const jurisdictions = useJurisdictions();
  const generate = useGenerateBatch();
  const archive = useClaimArchive();
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

  const exportCsv = () =>
    downloadBlob(new Blob([claimsToCsv(claims)], { type: 'text/csv' }), 'generated-claims.csv');

  /**
   * PROVENANCE: ADR-027 - Section 9: the client never produces an 837. The governed output comes
   * from the server, which is the only thing holding a serializer answerable to the Section 1
   * Reversibility Guarantee.
   */
  const export837 = async () => {
    const { blob, fileName } = await archive.mutateAsync();

    downloadBlob(blob, fileName);
  };

  return (
    <Panel
      title="Model → 837"
      description="Generate synthetic bills against the governed schema, then export them as CSV or as
        an ASC X12 837 archive. Every option below comes from the server, so nothing offered here can
        be refused by it."
    >
      <form onSubmit={form.handleSubmit} className="flex flex-wrap items-end gap-4">
        <Field
          id="BillCount"
          name="BillCount"
          label="Number of bills"
          hint="Governance caps a batch at 500."
          value={form.values.BillCount}
          onChange={form.handleChange}
        >
          {BILL_COUNTS.map((count) => (
            <option key={count} value={count}>
              {count}
            </option>
          ))}
        </Field>

        <Field
          id="JurisdictionState"
          name="JurisdictionState"
          label="State"
          hint="Selects the billing provider."
          value={form.values.JurisdictionState}
          onChange={form.handleChange}
        >
          <option value="">Any</option>
          {(jurisdictions.data ?? []).map((jurisdiction) => (
            <option key={jurisdiction.Code} value={jurisdiction.Code}>
              {jurisdiction.Name}
            </option>
          ))}
        </Field>

        <Field
          id="MedicalCodeCategories"
          name="MedicalCodeCategories"
          label="Medical code categories"
          hint="None selected means all of them."
          multiple
          size={4}
          className="min-w-56"
          value={form.values.MedicalCodeCategories}
          onChange={form.handleChange}
        >
          {categories.map((category) => (
            <option key={category} value={category}>
              {category}
            </option>
          ))}
        </Field>

        <Button type="submit" variant="primary" disabled={generate.isPending}>
          {generate.isPending ? 'Generating…' : 'Generate bills'}
        </Button>
      </form>

      {generate.isError && <Alert>{(generate.error as Error).message}</Alert>}

      {claims.length > 0 && (
        <div className="mt-5 space-y-4">
          <div className="flex gap-2">
            <Button type="button" onClick={exportCsv}>
              Export CSV
            </Button>
            <Button type="button" onClick={() => void export837()} disabled={archive.isPending}>
              {archive.isPending ? 'Building the archive…' : 'Export 837'}
            </Button>
          </div>

          <ClaimTable claims={claims} caption={`${claims.length} generated bills`} />
        </div>
      )}
    </Panel>
  );
}
