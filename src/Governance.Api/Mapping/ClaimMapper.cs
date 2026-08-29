using Governance.Contracts.DTOs;
using Governance.Domain.Entities;

namespace Governance.Api.Mapping;

/// <summary>
/// PROVENANCE: GOVERNANCE-1 - "APIs, DTOs, and React state models are downstream views of these
/// entities." The mapping is field-for-field by governed name; there is no renaming to do, which is
/// the point of the Section 1 naming alignment rule.
/// </summary>
public static class ClaimMapper
{
    public static ClaimHeaderDto ToDto(ClaimHeader claim) => new(
        claim.Id,
        claim.BHT03_ClaimSubmitterTransactionId,
        claim.BHT04_TransactionSetCreationDate,
        claim.Loop2010AA_NM103_BillingProviderLastNameOrOrg,
        claim.Loop2010AA_NM104_BillingProviderFirstName,
        claim.Loop2010AA_NM109_BillingProviderNpi,
        claim.Loop2010AA_N301_BillingProviderAddressLine,
        claim.Loop2010AA_N401_BillingProviderCity,
        claim.Loop2010AA_N402_BillingProviderState,
        claim.Loop2010AA_N403_BillingProviderZipCode,
        claim.Loop2010BA_NM103_SubscriberLastName,
        claim.Loop2010BA_NM104_SubscriberFirstName,
        claim.Loop2010BA_DMG02_SubscriberDob,
        claim.Loop2010BA_DMG03_SubscriberGender,
        claim.Loop2010BB_NM103_PayerName,
        claim.Loop2010BB_NM109_PayerId,
        claim.CLM01_ClaimControlNumber,
        claim.CLM02_TotalClaimChargeAmount,
        claim.CLM05_1_PlaceOfServiceCode,
        claim.CLM05_3_ClaimFrequencyCode,
        claim.HI01_2_PrincipalDiagnosisCode,
        claim.LineItems
            .OrderBy(line => line.LX01_AssignedLineNumber)
            .Select(ToDto)
            .ToList());

    public static ClaimLineItemDto ToDto(ClaimLineItem line) => new(
        line.Id,
        line.LX01_AssignedLineNumber,
        line.SV101_2_ProcedureCode,
        line.SV102_LineItemChargeAmount,
        line.SV104_ServiceUnitCount,
        line.SV103_UnitOfMeasure,
        line.DTP03_ServiceDate);
}
