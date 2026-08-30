import { describe, expect, it } from 'vitest';
import { claimsToCsv, csvField, csvRow } from '../../helpers/csv';
import { CLAIM_COLUMNS } from '../../helpers/claimFields';
import { aClaim } from '../fixtures';

/**
 * PROVENANCE: ADR-027 - helpers carry the testable logic, so this is where the suite lives, and it
 * is driven by it.each over a table of variants rather than by one example each.
 */
describe('csvField', () => {
  it.each([
    ['a plain value needs no quoting', 'PATEL', 'PATEL'],
    ['a number is rendered as itself', 1234.5, '1234.5'],
    ['zero is not blank', 0, '0'],
    ['null is an empty field', null, ''],
    ['undefined is an empty field', undefined, ''],
    ['a comma forces quoting', 'ALABAMA CARDIOVASCULAR GROUP, P.C.', '"ALABAMA CARDIOVASCULAR GROUP, P.C."'],
    ['a quote is doubled and the field quoted', 'THE "BIG" CLINIC', '"THE ""BIG"" CLINIC"'],
    ['a newline forces quoting', 'LINE ONE\nLINE TWO', '"LINE ONE\nLINE TWO"'],
    ['a carriage return forces quoting', 'A\rB', '"A\rB"'],
    ['a leading space is preserved', ' PADDED', ' PADDED'],
  ])('%s', (_description, value, expected) => {
    expect(csvField(value)).toBe(expected);
  });
});

describe('csvRow', () => {
  it.each([
    ['joins fields with commas', ['A', 'B', 'C'], 'A,B,C'],
    ['quotes only the field that needs it', ['A', 'B,C', 'D'], 'A,"B,C",D'],
    ['keeps empty fields positional', ['A', null, 'C'], 'A,,C'],
    ['handles a single field', ['ONLY'], 'ONLY'],
    ['handles no fields at all', [], ''],
  ])('%s', (_description, fields, expected) => {
    expect(csvRow(fields)).toBe(expected);
  });
});

describe('claimsToCsv', () => {
  it('writes a heading row naming every governed column', () => {
    const csv = claimsToCsv([aClaim()]);
    const [heading] = csv.split('\n');

    expect(heading.split(',')).toHaveLength(CLAIM_COLUMNS.length);
    CLAIM_COLUMNS.forEach((column) => expect(heading).toContain(column));
  });

  it.each([
    ['no claims is a heading row alone', 0, 1],
    ['one claim is heading plus one', 1, 2],
    ['many claims are heading plus each', 12, 13],
  ])('%s', (_description, claimCount, expectedLines) => {
    const claims = Array.from({ length: claimCount }, (_, index) => aClaim({ CLM01_ClaimControlNumber: `CLM${index}` }));

    expect(claimsToCsv(claims).split('\n')).toHaveLength(expectedLines);
  });

  it('quotes a provider name carrying a comma rather than splitting the row', () => {
    const claim = aClaim({
      Loop2010AA_NM103_BillingProviderLastNameOrOrg: 'ALABAMA CARDIOVASCULAR GROUP, P.C.',
    });

    const [, row] = claimsToCsv([claim]).split('\n');

    expect(row).toContain('"ALABAMA CARDIOVASCULAR GROUP, P.C."');
    expect(row.split(',')).toHaveLength(CLAIM_COLUMNS.length + 1);
  });

  it('carries the governed control number of every claim', () => {
    const claims = [aClaim({ CLM01_ClaimControlNumber: 'CLM-A' }), aClaim({ CLM01_ClaimControlNumber: 'CLM-B' })];

    const csv = claimsToCsv(claims);

    expect(csv).toContain('CLM-A');
    expect(csv).toContain('CLM-B');
  });
});
