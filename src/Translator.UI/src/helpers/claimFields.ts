/**
 * PROVENANCE: GOVERNANCE-1 - "Attribute names across the database, DTOs, and React forms must
 * reflect ASC X12 nomenclature." A governed column is named here exactly as governance Section 2
 * names it, and as the payload now carries it (FIND-020).
 *
 * NOT YET IMPLEMENTED - see helpers tests.
 */

/** A service line, as the contract publishes it. */
export interface ClaimLineItem {
  Id: string | null;
  LX01_AssignedLineNumber: number;
  SV101_2_ProcedureCode: string;
  SV102_LineItemChargeAmount: number;
  SV104_ServiceUnitCount: number;
  SV103_UnitOfMeasure: string;
  DTP03_ServiceDate: string;
}

/** A claim, as the contract publishes it. */
export interface ClaimHeader {
  Id: string | null;
  BHT03_ClaimSubmitterTransactionId: string;
  BHT04_TransactionSetCreationDate: string;
  Loop2010AA_NM103_BillingProviderLastNameOrOrg: string;
  Loop2010AA_NM104_BillingProviderFirstName: string | null;
  Loop2010AA_NM109_BillingProviderNpi: string;
  Loop2010AA_N301_BillingProviderAddressLine: string;
  Loop2010AA_N401_BillingProviderCity: string;
  Loop2010AA_N402_BillingProviderState: string;
  Loop2010AA_N403_BillingProviderZipCode: string;
  Loop2010BA_NM103_SubscriberLastName: string;
  Loop2010BA_NM104_SubscriberFirstName: string;
  Loop2010BA_DMG02_SubscriberDob: string;
  Loop2010BA_DMG03_SubscriberGender: string;
  Loop2010BB_NM103_PayerName: string;
  Loop2010BB_NM109_PayerId: string;
  CLM01_ClaimControlNumber: string;
  CLM02_TotalClaimChargeAmount: number;
  CLM05_1_PlaceOfServiceCode: string;
  CLM05_3_ClaimFrequencyCode: string;
  HI01_2_PrincipalDiagnosisCode: string;
  LineItems: ClaimLineItem[];
}

/**
 * The governed columns shown as table columns, in the order a reader wants them: who was billed
 * for, by whom, for how much.
 */
export const CLAIM_COLUMNS: readonly (keyof ClaimHeader)[] = [
  'CLM01_ClaimControlNumber',
  'Loop2010BA_NM103_SubscriberLastName',
  'Loop2010BA_NM104_SubscriberFirstName',
  'Loop2010AA_NM103_BillingProviderLastNameOrOrg',
  'Loop2010AA_NM109_BillingProviderNpi',
  'Loop2010AA_N402_BillingProviderState',
  'Loop2010BB_NM103_PayerName',
  'HI01_2_PrincipalDiagnosisCode',
  'CLM02_TotalClaimChargeAmount',
] as const;

/**
 * A governed column name rendered as a column heading a person can read.
 *
 * The governed name is `Loop2010BA_NM103_SubscriberLastName`: a loop, an element, then what the
 * field is. The heading is the last part, spaced. The governed name never leaves the state - this
 * is presentation only, and `CLAIM_COLUMNS` remains the source of truth for what a column *is*.
 */
export function headingFor(column: keyof ClaimHeader): string {
  const segments = column.split('_');

  // Everything up to and including the element identifier is the address of the field within the
  // 837; what follows is its name. A component like HI01_2 leaves a bare number, which is address
  // rather than name too.
  const name = segments[segments.length - 1];

  return name
    .replace(/([a-z0-9])([A-Z])/g, '$1 $2')
    .replace(/\s+/g, ' ')
    .trim();
}

/** The value of a governed column, formatted for display. */
export function displayValue(claim: ClaimHeader, column: keyof ClaimHeader): string {
  const value = claim[column];

  if (value === null || value === undefined) return '';
  if (typeof value === 'number') return formatAmount(value);
  if (Array.isArray(value)) return String(value.length);

  // The two governed CCYYMMDD columns are dates a person reads; everything else is an identifier,
  // a code or a name, and is shown exactly as the governed column holds it.
  return column === 'Loop2010BA_DMG02_SubscriberDob' ? formatGovernedDate(value) : value;
}

/** A governed CCYYMMDD date as an ISO date, or the input unchanged if it is not one. */
export function formatGovernedDate(value: string): string {
  if (!/^[0-9]{8}$/.test(value)) return value;

  return `${value.slice(0, 4)}-${value.slice(4, 6)}-${value.slice(6, 8)}`;
}

/**
 * A monetary amount at the governed scale of two decimal places.
 *
 * PROVENANCE: FIND-014 - the governed columns declare decimal(18,2), and a value that renders as
 * "15.4" where the column says 15.40 is the same difference in representation that made a
 * generated claim look unlike an imported one. The client shows the governed scale.
 */
export function formatAmount(value: number): string {
  return value.toFixed(2);
}
