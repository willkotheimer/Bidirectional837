import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { ImportTab } from '../components/ImportTab';
import { aClaim } from './fixtures';

/**
 * PROVENANCE: GOVERNANCE-5 - governance Feature 3, User Story 3.1 at the client: an 837 file or a
 * ZIP of them is uploaded and the bills it reconstructs are displayed.
 *
 * PROVENANCE: ADR-027 - Section 7: behaviour queried by role and label, and the server faked at the
 * network boundary rather than by stubbing the hook under test.
 */
let fetchMock: ReturnType<typeof vi.fn>;

const renderTab = () => {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });

  return {
    user: userEvent.setup(),
    ...render(
      <QueryClientProvider client={client}>
        <ImportTab />
      </QueryClientProvider>,
    ),
  };
};

const jsonOk = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });

const problem = (detail: string, status = 400) =>
  new Response(JSON.stringify({ detail }), {
    status,
    headers: { 'Content-Type': 'application/problem+json' },
  });

const anInterchange = () => new File(['ISA*00*...~'], 'claim.837', { type: 'application/octet-stream' });

beforeEach(() => {
  localStorage.clear();
  fetchMock = vi.fn(async () => jsonOk([aClaim({ CLM01_ClaimControlNumber: 'CLM-IMPORTED' })], 201));
  vi.stubGlobal('fetch', fetchMock);
  vi.stubGlobal('URL', Object.assign(URL, { createObjectURL: vi.fn(() => 'blob:stub'), revokeObjectURL: vi.fn() }));
});

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('the uploader', () => {
  it('offers a labelled file input', () => {
    renderTab();

    expect(screen.getByLabelText(/837 file/i)).toBeInTheDocument();
  });

  it('says it takes a single file or a ZIP of them', () => {
    renderTab();

    // Both the label and the description mention it, which is the point: a user reading either
    // learns the route takes a batch. The assertion is that it is said, not that it is said once.
    expect(screen.getAllByText(/zip/i).length).toBeGreaterThan(0);
    expect(screen.getByLabelText(/837 file/i)).toHaveAttribute('type', 'file');
  });

  it('uploads the chosen file to the published import route', async () => {
    const { user } = renderTab();

    await user.upload(screen.getByLabelText(/837 file/i), anInterchange());

    await waitFor(() => {
      const call = fetchMock.mock.calls.find(([url]) => String(url).includes('/api/v1/claims/import'));
      expect(call).toBeDefined();
      expect((call![1] as RequestInit).method).toBe('POST');
    });
  });

  /**
   * The route takes multipart/form-data with the file under `file`. Sending it any other way is a
   * 400 the user cannot act on, so the shape of the request is asserted rather than assumed.
   */
  it('sends the file as multipart form data under the published field name', async () => {
    const { user } = renderTab();

    await user.upload(screen.getByLabelText(/837 file/i), anInterchange());

    await waitFor(() => {
      const call = fetchMock.mock.calls.find(([url]) => String(url).includes('/api/v1/claims/import'));
      const body = (call![1] as RequestInit).body as FormData;

      expect(body).toBeInstanceOf(FormData);
      expect(body.get('file')).toBeInstanceOf(File);
    });
  });

  it('shows the reconstructed claims in a table', async () => {
    const { user } = renderTab();

    await user.upload(screen.getByLabelText(/837 file/i), anInterchange());

    const table = await screen.findByRole('table');

    expect(within(table).getByText('CLM-IMPORTED')).toBeInTheDocument();
  });

  it('offers a CSV export only once claims have arrived', async () => {
    const { user } = renderTab();

    expect(screen.queryByRole('button', { name: /csv/i })).not.toBeInTheDocument();

    await user.upload(screen.getByLabelText(/837 file/i), anInterchange());
    await screen.findByRole('table');

    expect(screen.getByRole('button', { name: /csv/i })).toBeInTheDocument();
  });
});

/**
 * PROVENANCE: ADR-021 - the reader refuses a malformed file by naming the segment at fault, and
 * that message is the only part a user can act on.
 *
 * PROVENANCE: ADR-022 - an import applies whole or not at all, so a refusal leaves the table as it
 * was rather than partially replacing it.
 */
describe('when the server refuses the file', () => {
  it.each([
    ['a missing segment is named', 'The interchange carries 0 CLM segment(s); exactly one is required.'],
    ['a contradiction is quoted', 'CLM02 is 1980.60 but the SV102 line amounts sum to 1979.60.'],
    ['a wrong transaction set is named', "ST01 is '835'; this reader handles transaction set 837 only."],
    ['an empty payload is explained', 'No file was uploaded. Send one 837 file, or a ZIP archive of them, as file.'],
  ])('%s', async (_description, detail) => {
    fetchMock.mockResolvedValue(problem(detail));
    const { user } = renderTab();

    await user.upload(screen.getByLabelText(/837 file/i), anInterchange());

    expect(await screen.findByRole('alert')).toHaveTextContent(detail);
  });

  it('leaves the previous table untouched', async () => {
    const { user } = renderTab();

    await user.upload(screen.getByLabelText(/837 file/i), anInterchange());
    await screen.findByRole('table');

    fetchMock.mockResolvedValue(problem('The payload does not begin with an ISA segment.'));
    await user.upload(screen.getByLabelText(/837 file/i), new File(['nope'], 'bad.837'));

    await screen.findByRole('alert');

    expect(within(screen.getByRole('table')).getByText('CLM-IMPORTED')).toBeInTheDocument();
  });
});

/**
 * PROVENANCE: ADR-027 - Section 9, and the shape agreed in docs/UI-REQUIREMENTS.md: the verdict is
 * per row and on demand, never a batch summary, because there is no bulk verify endpoint and one
 * request per claim is the anti-pattern ADR-023 cost a section to remove.
 */
describe('the reversibility verdict', () => {
  const withVerdict = (verdict: Record<string, unknown>) => {
    fetchMock.mockImplementation(async (input: RequestInfo | URL) => {
      const url = String(input);
      if (url.includes('verify-reversibility')) return jsonOk(verdict);
      return jsonOk([aClaim({ CLM01_ClaimControlNumber: 'CLM-IMPORTED' })], 201);
    });
  };

  it('is not requested until it is asked for', async () => {
    withVerdict({ EdiTextIsIdentical: true, RecordIsIdentical: true, Differences: [] });
    const { user } = renderTab();

    await user.upload(screen.getByLabelText(/837 file/i), anInterchange());
    await screen.findByRole('table');

    expect(fetchMock.mock.calls.some(([url]) => String(url).includes('verify-reversibility'))).toBe(false);
  });

  it('reports both verdicts separately when asked', async () => {
    withVerdict({ EdiTextIsIdentical: true, RecordIsIdentical: true, Differences: [] });
    const { user } = renderTab();

    await user.upload(screen.getByLabelText(/837 file/i), anInterchange());
    await screen.findByRole('table');
    await user.click(screen.getByRole('button', { name: /verify/i }));

    expect(await screen.findByText(/text/i)).toBeInTheDocument();
    expect(await screen.findByText(/record/i)).toBeInTheDocument();
  });

  /**
   * The endpoint verifies stored record → 837 → stored record. It never sees the bytes the user
   * uploaded, so a claim can be perfectly preserved while the text differs. Collapsing the two into
   * one tick is the failure the verifier was shaped to prevent.
   */
  it('shows a differing text and an identical record as the two separate facts they are', async () => {
    withVerdict({ EdiTextIsIdentical: false, RecordIsIdentical: true, Differences: [] });
    const { user } = renderTab();

    await user.upload(screen.getByLabelText(/837 file/i), anInterchange());
    await screen.findByRole('table');
    await user.click(screen.getByRole('button', { name: /verify/i }));

    const verdict = await screen.findByTestId('verdict-CLM-IMPORTED');

    expect(verdict).toHaveTextContent(/text/i);
    expect(verdict).toHaveTextContent(/record/i);
    expect(verdict.textContent).not.toMatch(/^\s*(ok|pass|✓)\s*$/i);
  });

  it('names the governed column when one moved', async () => {
    withVerdict({
      EdiTextIsIdentical: false,
      RecordIsIdentical: false,
      Differences: ["CLM02_TotalClaimChargeAmount: '1980.60' became '1979.60'."],
    });
    const { user } = renderTab();

    await user.upload(screen.getByLabelText(/837 file/i), anInterchange());
    await screen.findByRole('table');
    await user.click(screen.getByRole('button', { name: /verify/i }));

    expect(await screen.findByText(/CLM02_TotalClaimChargeAmount/)).toBeInTheDocument();
  });
});
