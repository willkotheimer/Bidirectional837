// PROVENANCE: GOVERNANCE-2, ADR-003 - transcribed verbatim from governance.txt Section 2 ("Mandatory Database
// Schema (EF Core Code-First Model)"). This file is a normative transcription, not a
// design artifact. Any change to a property name, type, nullability, or length here is a
// governance amendment and must be recorded in docs/PROVENANCE.md with justification.
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Translator.Domain.Entities;

[Table("Claims")]
public class ClaimHeader
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    // BHT Loop - Beginning of Hierarchical Transaction
    [Required, StringLength(50)]
    public string BHT03_ClaimSubmitterTransactionId { get; set; } = string.Empty;

    [Required]
    public DateTime BHT04_TransactionSetCreationDate { get; set; }

    // Loop 2010AA - Billing Provider
    [Required, StringLength(100)]
    public string Loop2010AA_NM103_BillingProviderLastNameOrOrg { get; set; } = string.Empty;

    [StringLength(35)]
    public string? Loop2010AA_NM104_BillingProviderFirstName { get; set; }

    [Required, StringLength(10)] // NPI
    public string Loop2010AA_NM109_BillingProviderNpi { get; set; } = string.Empty;

    [Required, StringLength(55)]
    public string Loop2010AA_N301_BillingProviderAddressLine { get; set; } = string.Empty;

    [Required, StringLength(30)]
    public string Loop2010AA_N401_BillingProviderCity { get; set; } = string.Empty;

    [Required, StringLength(2)]
    public string Loop2010AA_N402_BillingProviderState { get; set; } = string.Empty;

    [Required, StringLength(15)]
    public string Loop2010AA_N403_BillingProviderZipCode { get; set; } = string.Empty;

    // Loop 2010BA - Subscriber / Patient
    [Required, StringLength(60)]
    public string Loop2010BA_NM103_SubscriberLastName { get; set; } = string.Empty;

    [Required, StringLength(35)]
    public string Loop2010BA_NM104_SubscriberFirstName { get; set; } = string.Empty;

    [Required, StringLength(8)] // CCYYMMDD
    public string Loop2010BA_DMG02_SubscriberDob { get; set; } = string.Empty;

    [Required, StringLength(1)] // M / F / U
    public string Loop2010BA_DMG03_SubscriberGender { get; set; } = string.Empty;

    // Loop 2010BB - Payer Information
    [Required, StringLength(60)]
    public string Loop2010BB_NM103_PayerName { get; set; } = string.Empty;

    [Required, StringLength(80)]
    public string Loop2010BB_NM109_PayerId { get; set; } = string.Empty;

    // Loop 2300 - Claim Details
    [Required, StringLength(38)]
    public string CLM01_ClaimControlNumber { get; set; } = string.Empty;

    [Column(TypeName = "decimal(18,2)")]
    public decimal CLM02_TotalClaimChargeAmount { get; set; }

    [Required, StringLength(2)]
    public string CLM05_1_PlaceOfServiceCode { get; set; } = string.Empty;

    [Required, StringLength(1)]
    public string CLM05_3_ClaimFrequencyCode { get; set; } = "1";

    [Required, StringLength(10)] // Principal ICD-10 Code
    public string HI01_2_PrincipalDiagnosisCode { get; set; } = string.Empty;

    public List<ClaimLineItem> LineItems { get; set; } = new();
}

[Table("ClaimLineItems")]
public class ClaimLineItem
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ClaimHeaderId { get; set; }
    [ForeignKey(nameof(ClaimHeaderId))]
    public ClaimHeader ClaimHeader { get; set; } = null!;

    [Required]
    public int LX01_AssignedLineNumber { get; set; }

    // Loop 2400 - Professional Service
    [Required, StringLength(5)] // CPT / HCPCS Code
    public string SV101_2_ProcedureCode { get; set; } = string.Empty;

    [Column(TypeName = "decimal(18,2)")]
    public decimal SV102_LineItemChargeAmount { get; set; }

    [Column(TypeName = "decimal(18,4)")]
    public decimal SV104_ServiceUnitCount { get; set; }

    [Required, StringLength(2)] // UN = Units, MJ = Minutes
    public string SV103_UnitOfMeasure { get; set; } = "UN";

    [Required, StringLength(8)] // CCYYMMDD
    public string DTP03_ServiceDate { get; set; } = string.Empty;
}
