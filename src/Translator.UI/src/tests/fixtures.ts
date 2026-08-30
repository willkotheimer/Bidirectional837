import type { ClaimHeader, ClaimLineItem } from '../helpers/claimFields';

/**
 * A claim shaped exactly as the contract publishes it, for tests to vary one field of.
 *
 * PROVENANCE: FIND-020 - the governed names appear here unmangled, because that is what the payload
 * now carries. A fixture written against clM02_ would have hidden the defect from the client the way
 * the backend suite hid it from the server.
 */
export function aLine(overrides: Partial<ClaimLineItem> = {}): ClaimLineItem {
  return {
    Id: '5f2a1d84-0000-4000-8000-000000000001',
    LX01_AssignedLineNumber: 1,
    SV101_2_ProcedureCode: 'G0403',
    SV102_LineItemChargeAmount: 15.36,
    SV104_ServiceUnitCount: 1.0,
    SV103_UnitOfMeasure: 'UN',
    DTP03_ServiceDate: '20260216',
    ...overrides,
  };
}

export function aClaim(overrides: Partial<ClaimHeader> = {}): ClaimHeader {
  return {
    Id: '9c1e7b20-0000-4000-8000-000000000001',
    BHT03_ClaimSubmitterTransactionId: 'BATCH000001-00001',
    BHT04_TransactionSetCreationDate: '2026-01-01T00:00:00Z',
    Loop2010AA_NM103_BillingProviderLastNameOrOrg: 'ADUSUMILLI',
    Loop2010AA_NM104_BillingProviderFirstName: 'RAVI',
    Loop2010AA_NM109_BillingProviderNpi: '1932102084',
    Loop2010AA_N301_BillingProviderAddressLine: '2100 W CENTRAL AVE',
    Loop2010AA_N401_BillingProviderCity: 'TOLEDO',
    Loop2010AA_N402_BillingProviderState: 'OH',
    Loop2010AA_N403_BillingProviderZipCode: '436151753',
    Loop2010BA_NM103_SubscriberLastName: 'MERCER',
    Loop2010BA_NM104_SubscriberFirstName: 'BRIAN',
    Loop2010BA_DMG02_SubscriberDob: '19550321',
    Loop2010BA_DMG03_SubscriberGender: 'M',
    Loop2010BB_NM103_PayerName: 'NORTHSTAR INSURANCE',
    Loop2010BB_NM109_PayerId: 'PAYER5716',
    CLM01_ClaimControlNumber: 'CLM152784923000001',
    CLM02_TotalClaimChargeAmount: 15.36,
    CLM05_1_PlaceOfServiceCode: '11',
    CLM05_3_ClaimFrequencyCode: '1',
    HI01_2_PrincipalDiagnosisCode: 'I10',
    LineItems: [aLine()],
    ...overrides,
  };
}
