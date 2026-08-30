import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import {
  ApiError,
  fetchJurisdictions,
  fetchMedicalCodes,
  fileNameFrom,
  generateBatch,
  getJson,
} from '../../data/api';
import { aClaim } from '../fixtures';

/**
 * PROVENANCE: ADR-027 - frontend governance Section 7: the server is faked at the network boundary,
 * not by stubbing the module under test. A test that replaced fetchMedicalCodes would prove the
 * caller renders what it was handed and nothing about whether it asked the right question, which is
 * the FIND-017 failure in a different layer.
 */
const jsonResponse = (body: unknown, status = 200, headers: Record<string, string> = {}) =>
  new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': status >= 400 ? 'application/problem+json' : 'application/json', ...headers },
  });

let fetchMock: ReturnType<typeof vi.fn>;

beforeEach(() => {
  fetchMock = vi.fn();
  vi.stubGlobal('fetch', fetchMock);
});

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('fileNameFrom', () => {
  it.each([
    ['a quoted filename', 'attachment; filename="claims-837.zip"', 'claims-837.zip'],
    ['an unquoted filename', 'attachment; filename=claims-837.zip', 'claims-837.zip'],
    ['a filename* takes precedence', "attachment; filename=\"a.zip\"; filename*=UTF-8''b.zip", 'b.zip'],
    ['no header at all falls back', null, 'download.zip'],
    ['a header with no filename falls back', 'attachment', 'download.zip'],
    ['an empty header falls back', '', 'download.zip'],
  ])('%s', (_description, header, expected) => {
    expect(fileNameFrom(header, 'download.zip')).toBe(expected);
  });
});

describe('getJson', () => {
  it('requests the path against the API base address', async () => {
    fetchMock.mockResolvedValue(jsonResponse([]));

    await getJson('/api/v1/codes');

    const [url] = fetchMock.mock.calls[0];
    expect(String(url)).toContain('/api/v1/codes');
  });

  /**
   * PROVENANCE: ADR-021 - a refusal names the segment at fault, and the client shows it. These are
   * the API's real messages.
   */
  it.each([
    [400, { detail: 'The interchange carries 0 CLM segment(s); exactly one is required.' }],
    [404, { detail: "No medical codes are catalogued for 'Dentistry'." }],
  ])('surfaces the server message for a %i', async (status, problem) => {
    fetchMock.mockResolvedValue(jsonResponse(problem, status));

    const failure = await getJson('/api/v1/codes').catch((error: unknown) => error);

    expect(failure).toBeInstanceOf(ApiError);
    expect((failure as ApiError).message).toBe(problem.detail);
    expect((failure as ApiError).status).toBe(status);
  });

  it('does not invent a message when the server gives none', async () => {
    fetchMock.mockResolvedValue(new Response('', { status: 500 }));

    const failure = (await getJson('/api/v1/codes').catch((error: unknown) => error)) as ApiError;

    expect(failure).toBeInstanceOf(ApiError);
    expect(failure.message.length).toBeGreaterThan(0);
  });
});

describe('fetchMedicalCodes', () => {
  it('returns the catalogue with its governed field names intact', async () => {
    const codes = [{ Code: 'G0403', Category: 'Cardiac', Description: 'ECG', StandardCharge: 15.36 }];
    fetchMock.mockResolvedValue(jsonResponse(codes));

    await expect(fetchMedicalCodes()).resolves.toEqual(codes);
  });

  it('asks the published route', async () => {
    fetchMock.mockResolvedValue(jsonResponse([]));

    await fetchMedicalCodes();

    expect(String(fetchMock.mock.calls[0][0])).toContain('/api/v1/codes');
  });
});

describe('fetchJurisdictions', () => {
  it('asks the published route', async () => {
    fetchMock.mockResolvedValue(jsonResponse([]));

    await fetchJurisdictions();

    expect(String(fetchMock.mock.calls[0][0])).toContain('/api/v1/jurisdictions');
  });
});

describe('generateBatch', () => {
  it('posts the governed request contract', async () => {
    fetchMock.mockResolvedValue(jsonResponse([aClaim()], 201));

    await generateBatch({ BillCount: 5, JurisdictionState: 'OH', MedicalCodeCategories: ['Cardiac'] });

    const [url, init] = fetchMock.mock.calls[0];

    expect(String(url)).toContain('/api/v1/bills/batch-generate');
    expect(init.method).toBe('POST');
    expect(JSON.parse(init.body as string)).toEqual({
      BillCount: 5,
      JurisdictionState: 'OH',
      MedicalCodeCategories: ['Cardiac'],
    });
  });

  /**
   * PROVENANCE: FIND-020 - the payload carries the governed ASC X12 names, and the client reads
   * them as they are. A client written against clM02_ would have carried the mangling inward.
   */
  it('returns claims carrying their governed column names', async () => {
    fetchMock.mockResolvedValue(jsonResponse([aClaim()], 201));

    const [claim] = await generateBatch({
      BillCount: 1,
      JurisdictionState: 'OH',
      MedicalCodeCategories: ['Cardiac'],
    });

    expect(claim.CLM02_TotalClaimChargeAmount).toBe(15.36);
    expect(claim.Loop2010AA_NM109_BillingProviderNpi).toBe('1932102084');
    expect(claim.LineItems[0].SV101_2_ProcedureCode).toBe('G0403');
  });

  it('surfaces the governed ceiling refusal verbatim', async () => {
    fetchMock.mockResolvedValue(
      jsonResponse({ errors: { BillCount: ['The field BillCount must be between 1 and 500.'] } }, 400),
    );

    const failure = (await generateBatch({
      BillCount: 5000,
      JurisdictionState: 'OH',
      MedicalCodeCategories: ['Cardiac'],
    }).catch((error: unknown) => error)) as ApiError;

    expect(failure.message).toContain('between 1 and 500');
  });
});
