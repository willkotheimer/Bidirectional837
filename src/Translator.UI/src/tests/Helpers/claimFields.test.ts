import { describe, expect, it } from 'vitest';
import {
  CLAIM_COLUMNS,
  displayValue,
  formatAmount,
  formatGovernedDate,
  headingFor,
} from '../../helpers/claimFields';
import { aClaim } from '../fixtures';

/**
 * PROVENANCE: GOVERNANCE-1 - the governed column names reach the client unmangled and are the state
 * the table is built from. A heading is a rendering of that name for a reader; it never replaces it.
 */
describe('headingFor', () => {
  it.each([
    ['strips the segment prefix from a claim column', 'CLM01_ClaimControlNumber', 'Claim Control Number'],
    ['strips a loop and element prefix', 'Loop2010BA_NM103_SubscriberLastName', 'Subscriber Last Name'],
    ['handles a numbered component', 'HI01_2_PrincipalDiagnosisCode', 'Principal Diagnosis Code'],
    ['splits a charge amount', 'CLM02_TotalClaimChargeAmount', 'Total Claim Charge Amount'],
    ['keeps an initialism whole', 'Loop2010AA_NM109_BillingProviderNpi', 'Billing Provider Npi'],
    ['handles a two-letter state column', 'Loop2010AA_N402_BillingProviderState', 'Billing Provider State'],
  ] as const)('%s', (_description, column, expected) => {
    expect(headingFor(column)).toBe(expected);
  });

  it('produces a heading for every column the table shows', () => {
    CLAIM_COLUMNS.forEach((column) => {
      const heading = headingFor(column);

      expect(heading.length).toBeGreaterThan(0);
      expect(heading).not.toContain('_');
    });
  });
});

describe('formatGovernedDate', () => {
  it.each([
    ['a governed CCYYMMDD date becomes ISO', '20260216', '2026-02-16'],
    ['a leap day is a real date', '20240229', '2024-02-29'],
    ['the first of a year', '20260101', '2026-01-01'],
    ['an empty value is left alone', '', ''],
    ['a value that is not eight digits is left alone', '2026-02-16', '2026-02-16'],
    ['a non-numeric value is left alone', 'UNKNOWN', 'UNKNOWN'],
  ])('%s', (_description, value, expected) => {
    expect(formatGovernedDate(value)).toBe(expected);
  });
});

describe('formatAmount', () => {
  it.each([
    ['a whole amount keeps the governed two places', 15, '15.00'],
    ['a scaled amount is unchanged', 15.36, '15.36'],
    ['zero is shown, not blanked', 0, '0.00'],
    ['one decimal place is padded', 1980.6, '1980.60'],
    ['a large amount is not abbreviated', 9999999.99, '9999999.99'],
  ])('%s', (_description, value, expected) => {
    expect(formatAmount(value)).toBe(expected);
  });
});

describe('displayValue', () => {
  it.each([
    ['a control number is shown verbatim', 'CLM01_ClaimControlNumber', 'CLM152784923000001'],
    ['a provider NPI is shown verbatim', 'Loop2010AA_NM109_BillingProviderNpi', '1932102084'],
    ['a diagnosis keeps its decimal point', 'HI01_2_PrincipalDiagnosisCode', 'I10'],
    ['an amount is formatted at the governed scale', 'CLM02_TotalClaimChargeAmount', '15.36'],
    ['a governed date is made readable', 'Loop2010BA_DMG02_SubscriberDob', '1955-03-21'],
  ] as const)('%s', (_description, column, expected) => {
    expect(displayValue(aClaim(), column)).toBe(expected);
  });

  it('renders an absent optional name as empty rather than as the word null', () => {
    const claim = aClaim({ Loop2010AA_NM104_BillingProviderFirstName: null });

    expect(displayValue(claim, 'Loop2010AA_NM104_BillingProviderFirstName')).toBe('');
  });

  /**
   * PROVENANCE: ADR-027 - the client never truncates a governed value to fit a layout. Wrapping and
   * column width are the table's problem; the value arrives whole.
   */
  it('does not truncate a long governed value', () => {
    const long = 'A'.repeat(100);
    const claim = aClaim({ Loop2010AA_NM103_BillingProviderLastNameOrOrg: long });

    expect(displayValue(claim, 'Loop2010AA_NM103_BillingProviderLastNameOrOrg')).toBe(long);
  });
});
