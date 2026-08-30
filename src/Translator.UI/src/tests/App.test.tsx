import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import App from '../App';
import { aClaim } from './fixtures';

/**
 * PROVENANCE: ADR-027 - frontend governance Section 7: a test asserts what a user can do, queried by
 * role and label, and the server is faked at the network boundary rather than by stubbing hooks.
 */
const CODES = [
  { Code: 'G0403', Category: 'Cardiac', Description: 'Electrocardiogram, routine ecg', StandardCharge: 15.36 },
  { Code: 'G0422', Category: 'Cardiac', Description: 'Intensive cardiac rehabilitation', StandardCharge: 131.6 },
  { Code: 'J0670', Category: 'Anesthesia', Description: 'Injection, mepivacaine', StandardCharge: 4.15 },
];

const JURISDICTIONS = [
  { Code: 'OH', Name: 'Ohio', ProviderCount: 60 },
  { Code: 'CA', Name: 'California', ProviderCount: 60 },
];

let fetchMock: ReturnType<typeof vi.fn>;

const jsonOk = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });

beforeEach(() => {
  localStorage.clear();

  fetchMock = vi.fn(async (input: RequestInfo | URL) => {
    const url = String(input);

    if (url.includes('/api/v1/codes')) return jsonOk(CODES);
    if (url.includes('/api/v1/jurisdictions')) return jsonOk(JURISDICTIONS);
    if (url.includes('/api/v1/bills/batch-generate')) {
      return jsonOk([aClaim({ CLM01_ClaimControlNumber: 'CLM-1' }), aClaim({ CLM01_ClaimControlNumber: 'CLM-2' })], 201);
    }
    if (url.includes('/api/v1/claims/export-zip')) {
      return new Response(new Blob(['PK']), {
        status: 200,
        headers: { 'Content-Disposition': 'attachment; filename="claims-837.zip"' },
      });
    }

    return new Response('', { status: 404 });
  });

  vi.stubGlobal('fetch', fetchMock);
  vi.stubGlobal('URL', Object.assign(URL, { createObjectURL: vi.fn(() => 'blob:stub'), revokeObjectURL: vi.fn() }));
});

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('the page', () => {
  it('offers both directions of the translator as tabs', () => {
    render(<App />);

    expect(screen.getByRole('tab', { name: /837 → Model/i })).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: /Model → 837/i })).toBeInTheDocument();
  });

  it.each([
    ['837 → Model', /837 → Model/i],
    ['Model → 837', /Model → 837/i],
  ])('selecting %s shows its panel', async (_name, label) => {
    const user = userEvent.setup();
    render(<App />);

    await user.click(screen.getByRole('tab', { name: label }));

    expect(screen.getByRole('tab', { name: label })).toHaveAttribute('aria-selected', 'true');
  });

  /**
   * PROVENANCE: ADR-024 - the charges are real published CMS figures of the right order of
   * magnitude, which is exactly why the caveat is needed: they look like prices. Section 6 of the
   * frontend governance requires this where a user can see it, not in a footnote.
   */
  it('says its output is example data where a user can see it', () => {
    render(<App />);

    expect(screen.getByText(/example data/i)).toBeInTheDocument();
  });

  /**
   * PROVENANCE: ADR-027 - Section 5: the store is cleared at startup, before anything reads it, so
   * a previous session's bills never greet the next one.
   */
  it('clears any bills a previous session left behind', () => {
    localStorage.setItem('translator.claims.generated', JSON.stringify([aClaim()]));

    render(<App />);

    expect(localStorage.getItem('translator.claims.generated')).toBeNull();
  });
});

describe('the Model → 837 tab', () => {
  const openGenerateTab = async () => {
    const user = userEvent.setup();
    render(<App />);
    await user.click(screen.getByRole('tab', { name: /Model → 837/i }));
    return user;
  };

  it('offers only jurisdictions the server can source a provider for', async () => {
    await openGenerateTab();

    const state = await screen.findByLabelText(/state/i);

    expect(within(state).getByRole('option', { name: /Ohio/ })).toBeInTheDocument();
    expect(within(state).getByRole('option', { name: /California/ })).toBeInTheDocument();
  });

  it('offers the categories the catalogue actually holds', async () => {
    await openGenerateTab();

    const categories = await screen.findByLabelText(/categor/i);

    expect(within(categories).getByRole('option', { name: /Cardiac/ })).toBeInTheDocument();
    expect(within(categories).getByRole('option', { name: /Anesthesia/ })).toBeInTheDocument();
  });

  /**
   * Governance User Story 1.3 caps a batch at 500 and the API answers 400 above it, so the control
   * must not offer a number the server will refuse.
   */
  it('never offers a bill count above the governed ceiling', async () => {
    await openGenerateTab();

    const count = await screen.findByLabelText(/number of bills/i);
    const offered = within(count)
      .getAllByRole('option')
      .map((option) => Number((option as HTMLOptionElement).value));

    expect(Math.max(...offered)).toBeLessThanOrEqual(500);
    expect(offered).toContain(500);
  });

  it('generates a batch and shows it in a table', async () => {
    const user = await openGenerateTab();

    await user.click(await screen.findByRole('button', { name: /generate/i }));

    const table = await screen.findByRole('table');

    expect(within(table).getByText('CLM-1')).toBeInTheDocument();
    expect(within(table).getByText('CLM-2')).toBeInTheDocument();
  });

  it('posts the governed request contract', async () => {
    const user = await openGenerateTab();

    await user.click(await screen.findByRole('button', { name: /generate/i }));

    await waitFor(() => {
      const call = fetchMock.mock.calls.find(([url]) => String(url).includes('batch-generate'));
      expect(call).toBeDefined();

      const body = JSON.parse((call![1] as RequestInit).body as string);
      expect(body).toHaveProperty('BillCount');
      expect(body).toHaveProperty('JurisdictionState');
      expect(body).toHaveProperty('MedicalCodeCategories');
    });
  });

  it('offers CSV and 837 downloads only once there is something to download', async () => {
    const user = await openGenerateTab();

    expect(screen.queryByRole('button', { name: /csv/i })).not.toBeInTheDocument();

    await user.click(await screen.findByRole('button', { name: /generate/i }));
    await screen.findByRole('table');

    expect(screen.getByRole('button', { name: /csv/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /837/i })).toBeInTheDocument();
  });

  it('asks the server for the 837 archive rather than building one', async () => {
    const user = await openGenerateTab();

    await user.click(await screen.findByRole('button', { name: /generate/i }));
    await screen.findByRole('table');
    await user.click(screen.getByRole('button', { name: /837/i }));

    await waitFor(() => {
      expect(fetchMock.mock.calls.some(([url]) => String(url).includes('export-zip'))).toBe(true);
    });
  });

  /**
   * PROVENANCE: ADR-021 - the server's refusal names what was wrong, and that is the only part a
   * user can act on. It is shown, not replaced.
   */
  it('shows the server refusal verbatim when generation is rejected', async () => {
    fetchMock.mockImplementation(async (input: RequestInfo | URL) => {
      const url = String(input);
      if (url.includes('/api/v1/codes')) return jsonOk(CODES);
      if (url.includes('/api/v1/jurisdictions')) return jsonOk(JURISDICTIONS);
      return new Response(JSON.stringify({ detail: 'No medical codes are catalogued for: Dentistry.' }), {
        status: 400,
        headers: { 'Content-Type': 'application/problem+json' },
      });
    });

    const user = await openGenerateTab();

    await user.click(await screen.findByRole('button', { name: /generate/i }));

    expect(await screen.findByText(/No medical codes are catalogued for: Dentistry\./)).toBeInTheDocument();
  });
});
