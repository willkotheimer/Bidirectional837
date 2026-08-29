// PROVENANCE: GOVERNANCE-3 - transcribed verbatim from governance.txt Section 3 ("Mandatory Data
// Transfer Objects"). This file is a normative transcription, not a design artefact; see ADR-003.
//
// Governance Section 3 requires that the DTO match the schema and 837 mappings directly, and that
// custom field additions or deviations carry explicit documentation and architect approval.
//
// The governed text carries no validation metadata. ADR-005 records that the ephemeral SQLite store
// cannot enforce the Section 2 StringLength limits, and makes the API layer responsible for them
// instead. The annotations that discharge that debt are added here under ADR-010; they introduce no
// field, and mirror the Section 2 limits exactly.

using System.ComponentModel.DataAnnotations;

namespace Governance.Contracts.DTOs;

public record ClaimHeaderDto(
    Guid? Id,
    string BHT03_ClaimSubmitterTransactionId,
    DateTime BHT04_TransactionSetCreationDate,
    string Loop2010AA_NM103_BillingProviderLastNameOrOrg,
    string? Loop2010AA_NM104_BillingProviderFirstName,
    string Loop2010AA_NM109_BillingProviderNpi,
    string Loop2010AA_N301_BillingProviderAddressLine,
    string Loop2010AA_N401_BillingProviderCity,
    string Loop2010AA_N402_BillingProviderState,
    string Loop2010AA_N403_BillingProviderZipCode,
    string Loop2010BA_NM103_SubscriberLastName,
    string Loop2010BA_NM104_SubscriberFirstName,
    string Loop2010BA_DMG02_SubscriberDob,
    string Loop2010BA_DMG03_SubscriberGender,
    string Loop2010BB_NM103_PayerName,
    string Loop2010BB_NM109_PayerId,
    string CLM01_ClaimControlNumber,
    decimal CLM02_TotalClaimChargeAmount,
    string CLM05_1_PlaceOfServiceCode,
    string CLM05_3_ClaimFrequencyCode,
    string HI01_2_PrincipalDiagnosisCode,
    List<ClaimLineItemDto> LineItems
);

public record ClaimLineItemDto(
    Guid? Id,
    int LX01_AssignedLineNumber,
    string SV101_2_ProcedureCode,
    decimal SV102_LineItemChargeAmount,
    decimal SV104_ServiceUnitCount,
    string SV103_UnitOfMeasure,
    string DTP03_ServiceDate
);

public record BatchGenerationRequestDto(
    int BillCount, // Max 500
    string JurisdictionState,
    List<string> MedicalCodeCategories
);
