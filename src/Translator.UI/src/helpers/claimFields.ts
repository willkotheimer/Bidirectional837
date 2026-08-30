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

/** A governed column name rendered as a column heading a person can read. */
export function headingFor(_column: keyof ClaimHeader): string {
  throw new Error('not implemented');
}

/** The value of a governed column, formatted for display. */
export function displayValue(_claim: ClaimHeader, _column: keyof ClaimHeader): string {
  throw new Error('not implemented');
}

/** A governed CCYYMMDD date as an ISO date, or the input unchanged if it is not one. */
export function formatGovernedDate(_value: string): string {
  throw new Error('not implemented');
}

/** A monetary amount at the governed scale of two decimal places. */
export function formatAmount(_value: number): string {
  throw new Error('not implemented');
}
