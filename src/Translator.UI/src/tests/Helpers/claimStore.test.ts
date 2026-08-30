import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { clearAllClaims, readClaims, writeClaims, type StoreKey } from '../../helpers/claimStore';
import { aClaim } from '../fixtures';

/**
 * PROVENANCE: ADR-027 - frontend governance Section 5: local storage is the only thing that ever
 * saves a bill, it is cleared at startup before anything reads, and every access is wrapped because
 * the API throws in a private window rather than returning null.
 */
beforeEach(() => {
  localStorage.clear();
});

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

const KEYS: StoreKey[] = ['generated', 'imported'];

describe('writeClaims and readClaims', () => {
  it.each(KEYS)('a claim written under %s is read back whole', (key) => {
    const claims = [aClaim({ CLM01_ClaimControlNumber: 'CLM-A' })];

    writeClaims(key, claims);

    expect(readClaims(key)).toEqual(claims);
  });

  it.each(KEYS)('%s starts empty', (key) => {
    expect(readClaims(key)).toEqual([]);
  });

  it.each(KEYS)('writing to %s replaces rather than appends', (key) => {
    writeClaims(key, [aClaim({ CLM01_ClaimControlNumber: 'FIRST' })]);
    writeClaims(key, [aClaim({ CLM01_ClaimControlNumber: 'SECOND' })]);

    const held = readClaims(key);

    expect(held).toHaveLength(1);
    expect(held[0].CLM01_ClaimControlNumber).toBe('SECOND');
  });

  it('the two tabs do not overwrite each other', () => {
    writeClaims('generated', [aClaim({ CLM01_ClaimControlNumber: 'GEN' })]);
    writeClaims('imported', [aClaim({ CLM01_ClaimControlNumber: 'IMP' })]);

    expect(readClaims('generated')[0].CLM01_ClaimControlNumber).toBe('GEN');
    expect(readClaims('imported')[0].CLM01_ClaimControlNumber).toBe('IMP');
  });

  /**
   * PROVENANCE: FIND-020 - the governed ASC X12 names survive the store. A round trip through JSON
   * that renamed a column would be the client repeating the defect the server just stopped making.
   */
  it('governed column names survive the round trip through storage', () => {
    writeClaims('generated', [aClaim()]);

    const [claim] = readClaims('generated');

    expect(claim.CLM02_TotalClaimChargeAmount).toBe(15.36);
    expect(claim.Loop2010AA_NM109_BillingProviderNpi).toBe('1932102084');
    expect(claim.LineItems[0].SV101_2_ProcedureCode).toBe('G0403');
  });
});

describe('clearAllClaims', () => {
  it('empties every key', () => {
    writeClaims('generated', [aClaim()]);
    writeClaims('imported', [aClaim()]);

    clearAllClaims();

    expect(readClaims('generated')).toEqual([]);
    expect(readClaims('imported')).toEqual([]);
  });

  it('leaves storage that is not ours alone', () => {
    localStorage.setItem('unrelated', 'keep me');
    writeClaims('generated', [aClaim()]);

    clearAllClaims();

    expect(localStorage.getItem('unrelated')).toBe('keep me');
  });
});

/**
 * Section 5 rule 2: the page works with no store at all. Local storage throws rather than returning
 * null in a private window or where site data is blocked, and a translator that will not render
 * because it cannot save a draft is worse than one that simply does not save.
 */
describe('when local storage is unavailable', () => {
  const throwing = {
    getItem: () => {
      throw new Error('The operation is insecure.');
    },
    setItem: () => {
      throw new Error('The operation is insecure.');
    },
    removeItem: () => {
      throw new Error('The operation is insecure.');
    },
    clear: () => {
      throw new Error('The operation is insecure.');
    },
    key: () => null,
    length: 0,
  };

  beforeEach(() => {
    vi.stubGlobal('localStorage', throwing);
  });

  it.each(KEYS)('reading %s returns nothing rather than throwing', (key) => {
    expect(() => readClaims(key)).not.toThrow();
    expect(readClaims(key)).toEqual([]);
  });

  it.each(KEYS)('writing %s does not throw', (key) => {
    expect(() => writeClaims(key, [aClaim()])).not.toThrow();
  });

  it('clearing does not throw', () => {
    expect(() => clearAllClaims()).not.toThrow();
  });
});

describe('when the stored value is not what we wrote', () => {
  it.each([
    ['not JSON at all', 'this is not json'],
    ['JSON that is not an array', '{"CLM01_ClaimControlNumber":"X"}'],
    ['JSON null', 'null'],
    ['an empty string', ''],
  ])('%s reads as empty rather than throwing', (_description, stored) => {
    localStorage.setItem('translator.claims.generated', stored);

    expect(() => readClaims('generated')).not.toThrow();
    expect(readClaims('generated')).toEqual([]);
  });
});
