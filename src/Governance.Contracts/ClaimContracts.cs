// PROVENANCE: GOVERNANCE-3 - transcribed from governance.txt Section 3 ("Mandatory Data Transfer
// Objects"). Field names, types, order and optionality are the governed text unchanged; see ADR-003.
//
// Governance Section 3 requires that the DTO match the schema and 837 mappings directly, and that
// custom field additions or deviations carry explicit documentation and architect approval.
//
// PROVENANCE: ADR-005, ADR-010 - the governed text carries no validation metadata. ADR-005 records
// that the ephemeral SQLite store cannot enforce the Section 2 StringLength limits and makes this
// layer responsible for them. The annotations below are that compensating control. They add no field,
// remove none, and rename none: every limit mirrors the Section 2 column it corresponds to, and
// ContractValidationTheories proves the mirroring against the entity model rather than trusting it.

using System.ComponentModel.DataAnnotations;

namespace Governance.Contracts.DTOs;

public record ClaimHeaderDto(
    Guid? Id,
    [Required, StringLength(50)]
    string BHT03_ClaimSubmitterTransactionId,
    [Required]
    DateTime BHT04_TransactionSetCreationDate,
    [Required, StringLength(100)]
    string Loop2010AA_NM103_BillingProviderLastNameOrOrg,
    [StringLength(35)]
    string? Loop2010AA_NM104_BillingProviderFirstName,
    [Required, StringLength(10)] // NPI
    string Loop2010AA_NM109_BillingProviderNpi,
    [Required, StringLength(55)]
    string Loop2010AA_N301_BillingProviderAddressLine,
    [Required, StringLength(30)]
    string Loop2010AA_N401_BillingProviderCity,
    [Required, StringLength(2)]
    string Loop2010AA_N402_BillingProviderState,
    [Required, StringLength(15)]
    string Loop2010AA_N403_BillingProviderZipCode,
    [Required, StringLength(60)]
    string Loop2010BA_NM103_SubscriberLastName,
    [Required, StringLength(35)]
    string Loop2010BA_NM104_SubscriberFirstName,
    [Required, StringLength(8)] // CCYYMMDD
    string Loop2010BA_DMG02_SubscriberDob,
    [Required, StringLength(1)] // M / F / U
    string Loop2010BA_DMG03_SubscriberGender,
    [Required, StringLength(60)]
    string Loop2010BB_NM103_PayerName,
    [Required, StringLength(80)]
    string Loop2010BB_NM109_PayerId,
    [Required, StringLength(38)]
    string CLM01_ClaimControlNumber,
    decimal CLM02_TotalClaimChargeAmount,
    [Required, StringLength(2)]
    string CLM05_1_PlaceOfServiceCode,
    [Required, StringLength(1)]
    string CLM05_3_ClaimFrequencyCode,
    [Required, StringLength(10)] // Principal ICD-10 Code
    string HI01_2_PrincipalDiagnosisCode,
    List<ClaimLineItemDto> LineItems
);

public record ClaimLineItemDto(
    Guid? Id,
    [Required]
    int LX01_AssignedLineNumber,
    [Required, StringLength(5)] // CPT / HCPCS Code
    string SV101_2_ProcedureCode,
    decimal SV102_LineItemChargeAmount,
    decimal SV104_ServiceUnitCount,
    [Required, StringLength(2)] // UN = Units, MJ = Minutes
    string SV103_UnitOfMeasure,
    [Required, StringLength(8)] // CCYYMMDD
    string DTP03_ServiceDate
);

public record BatchGenerationRequestDto(
    // PROVENANCE: ADR-010 - the ceiling of 500 is governed directly by User Story 1.3, which
    // requires a request above it to return 400. The floor of 1 is an addition: a batch of zero
    // or fewer bills has no meaningful result.
    [Range(1, 500)]
    int BillCount, // Max 500
    // Mirrors the governed Loop2010AA_N402_BillingProviderState column this ultimately populates.
    [Required, StringLength(2, MinimumLength = 2)]
    string JurisdictionState,
    [Required]
    List<string> MedicalCodeCategories
);
