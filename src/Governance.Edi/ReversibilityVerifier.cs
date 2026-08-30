using System.Globalization;
using Governance.Domain.Entities;

namespace Governance.Edi;

/// <summary>
/// The verdict of a zero-mutation round trip, as governance User Story 3.2 states it: the emitted
/// text compared against its re-emission, and the stored record compared against its reconstruction.
/// </summary>
/// <remarks>
/// Both halves are reported separately and both are reported honestly. A verdict that collapsed
/// them into one boolean could be made true by comparing less, which is the failure mode a
/// reversibility check exists to prevent.
/// </remarks>
public sealed record ReversibilityVerdict(
    bool EdiTextIsIdentical,
    bool RecordIsIdentical,
    IReadOnlyList<string> Differences);

/// <summary>
/// Governance User Story 3.2, and the governance Section 4 Roundtrip Reversibility Test Standard:
/// export a stored claim, read the result back, and prove that neither the text nor the record
/// moved.
/// </summary>
public sealed class ReversibilityVerifier
{
    public ReversibilityVerifier(Edi837Serializer serializer, Edi837Parser parser)
    {
        Serializer = serializer;
        Parser = parser;
    }

    /// <summary>The scales governance Section 2 declares on the monetary and quantity columns.</summary>
    private const int MoneyScale = 2;
    private const int QuantityScale = 4;

    public Edi837Serializer Serializer { get; }
    public Edi837Parser Parser { get; }

    /// <summary>Exports the claim, re-imports it, and reports whether anything changed.</summary>
    /// <remarks>
    /// A file the reader refuses is a failed round trip, not an exception for the caller to handle.
    /// The dashboard exists to report that a claim does not round-trip, and a claim the writer can
    /// emit but the reader cannot read is the most serious form of that answer.
    /// </remarks>
    public ReversibilityVerdict Verify(ClaimHeader stored)
    {
        ArgumentNullException.ThrowIfNull(stored);

        var exported = Serializer.Serialize(stored);

        ClaimHeader reimported;
        try
        {
            reimported = Parser.Parse(exported);
        }
        catch (EdiFormatException refusal)
        {
            return new ReversibilityVerdict(false, false,
                [$"The exported interchange could not be read back: {refusal.Message}"]);
        }

        var reexported = Serializer.Serialize(reimported);

        return new ReversibilityVerdict(
            EdiTextIsIdentical: string.Equals(exported, reexported, StringComparison.Ordinal),
            RecordIsIdentical: Differences(stored, reimported).Count == 0,
            Differences: Differences(stored, reimported));
    }

    /// <summary>
    /// Every governed column on which two claims differ, named by its Section 2 column name.
    /// </summary>
    /// <remarks>
    /// PROVENANCE: ADR-016 - storage identity is not compared. It has no 837 counterpart, so a
    /// reader cannot recover it, and comparing it would report every correct round trip as a
    /// mutation.
    ///
    /// Amounts are compared as text rather than by value, because 1.00m and 1m compare equal and
    /// are not the same 837. That distinction is the whole of FIND-002, and a comparison that
    /// missed it would report a scale loss as no difference at all.
    /// </remarks>
    public static IReadOnlyList<string> Differences(ClaimHeader left, ClaimHeader right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        var differences = new List<string>();

        void Compare(string column, string? one, string? other)
        {
            if (!string.Equals(one, other, StringComparison.Ordinal))
            {
                differences.Add($"{column}: '{one}' became '{other}'.");
            }
        }

        Compare(nameof(ClaimHeader.BHT03_ClaimSubmitterTransactionId),
            left.BHT03_ClaimSubmitterTransactionId, right.BHT03_ClaimSubmitterTransactionId);
        Compare(nameof(ClaimHeader.BHT04_TransactionSetCreationDate),
            Moment(left.BHT04_TransactionSetCreationDate), Moment(right.BHT04_TransactionSetCreationDate));

        Compare(nameof(ClaimHeader.Loop2010AA_NM103_BillingProviderLastNameOrOrg),
            left.Loop2010AA_NM103_BillingProviderLastNameOrOrg, right.Loop2010AA_NM103_BillingProviderLastNameOrOrg);
        Compare(nameof(ClaimHeader.Loop2010AA_NM104_BillingProviderFirstName),
            left.Loop2010AA_NM104_BillingProviderFirstName, right.Loop2010AA_NM104_BillingProviderFirstName);
        Compare(nameof(ClaimHeader.Loop2010AA_NM109_BillingProviderNpi),
            left.Loop2010AA_NM109_BillingProviderNpi, right.Loop2010AA_NM109_BillingProviderNpi);
        Compare(nameof(ClaimHeader.Loop2010AA_N301_BillingProviderAddressLine),
            left.Loop2010AA_N301_BillingProviderAddressLine, right.Loop2010AA_N301_BillingProviderAddressLine);
        Compare(nameof(ClaimHeader.Loop2010AA_N401_BillingProviderCity),
            left.Loop2010AA_N401_BillingProviderCity, right.Loop2010AA_N401_BillingProviderCity);
        Compare(nameof(ClaimHeader.Loop2010AA_N402_BillingProviderState),
            left.Loop2010AA_N402_BillingProviderState, right.Loop2010AA_N402_BillingProviderState);
        Compare(nameof(ClaimHeader.Loop2010AA_N403_BillingProviderZipCode),
            left.Loop2010AA_N403_BillingProviderZipCode, right.Loop2010AA_N403_BillingProviderZipCode);

        Compare(nameof(ClaimHeader.Loop2010BA_NM103_SubscriberLastName),
            left.Loop2010BA_NM103_SubscriberLastName, right.Loop2010BA_NM103_SubscriberLastName);
        Compare(nameof(ClaimHeader.Loop2010BA_NM104_SubscriberFirstName),
            left.Loop2010BA_NM104_SubscriberFirstName, right.Loop2010BA_NM104_SubscriberFirstName);
        Compare(nameof(ClaimHeader.Loop2010BA_DMG02_SubscriberDob),
            left.Loop2010BA_DMG02_SubscriberDob, right.Loop2010BA_DMG02_SubscriberDob);
        Compare(nameof(ClaimHeader.Loop2010BA_DMG03_SubscriberGender),
            left.Loop2010BA_DMG03_SubscriberGender, right.Loop2010BA_DMG03_SubscriberGender);

        Compare(nameof(ClaimHeader.Loop2010BB_NM103_PayerName),
            left.Loop2010BB_NM103_PayerName, right.Loop2010BB_NM103_PayerName);
        Compare(nameof(ClaimHeader.Loop2010BB_NM109_PayerId),
            left.Loop2010BB_NM109_PayerId, right.Loop2010BB_NM109_PayerId);

        Compare(nameof(ClaimHeader.CLM01_ClaimControlNumber),
            left.CLM01_ClaimControlNumber, right.CLM01_ClaimControlNumber);
        Compare(nameof(ClaimHeader.CLM02_TotalClaimChargeAmount),
            Amount(left.CLM02_TotalClaimChargeAmount, MoneyScale),
            Amount(right.CLM02_TotalClaimChargeAmount, MoneyScale));
        Compare(nameof(ClaimHeader.CLM05_1_PlaceOfServiceCode),
            left.CLM05_1_PlaceOfServiceCode, right.CLM05_1_PlaceOfServiceCode);
        Compare(nameof(ClaimHeader.CLM05_3_ClaimFrequencyCode),
            left.CLM05_3_ClaimFrequencyCode, right.CLM05_3_ClaimFrequencyCode);
        Compare(nameof(ClaimHeader.HI01_2_PrincipalDiagnosisCode),
            left.HI01_2_PrincipalDiagnosisCode, right.HI01_2_PrincipalDiagnosisCode);

        CompareLines(left, right, differences);

        return differences;
    }

    /// <summary>
    /// Service lines are matched by their governed line number rather than by position, so a
    /// reordering is not reported as a mutation of every line, and a missing line is reported as
    /// the missing line rather than as a cascade.
    /// </summary>
    private static void CompareLines(ClaimHeader left, ClaimHeader right, List<string> differences)
    {
        var ours = left.LineItems.ToDictionary(line => line.LX01_AssignedLineNumber);
        var theirs = right.LineItems.ToDictionary(line => line.LX01_AssignedLineNumber);

        foreach (var number in ours.Keys.Union(theirs.Keys).Order())
        {
            if (!ours.TryGetValue(number, out var one))
            {
                differences.Add($"{nameof(ClaimHeader.LineItems)}: line {number} was added.");
                continue;
            }

            if (!theirs.TryGetValue(number, out var other))
            {
                differences.Add($"{nameof(ClaimHeader.LineItems)}: line {number} was lost.");
                continue;
            }

            void CompareLine(string column, string? a, string? b)
            {
                if (!string.Equals(a, b, StringComparison.Ordinal))
                {
                    differences.Add($"{nameof(ClaimHeader.LineItems)}[{number}].{column}: '{a}' became '{b}'.");
                }
            }

            CompareLine(nameof(ClaimLineItem.SV101_2_ProcedureCode),
                one.SV101_2_ProcedureCode, other.SV101_2_ProcedureCode);
            CompareLine(nameof(ClaimLineItem.SV102_LineItemChargeAmount),
                Amount(one.SV102_LineItemChargeAmount, MoneyScale),
                Amount(other.SV102_LineItemChargeAmount, MoneyScale));
            CompareLine(nameof(ClaimLineItem.SV103_UnitOfMeasure),
                one.SV103_UnitOfMeasure, other.SV103_UnitOfMeasure);
            CompareLine(nameof(ClaimLineItem.SV104_ServiceUnitCount),
                Amount(one.SV104_ServiceUnitCount, QuantityScale),
                Amount(other.SV104_ServiceUnitCount, QuantityScale));
            CompareLine(nameof(ClaimLineItem.DTP03_ServiceDate),
                one.DTP03_ServiceDate, other.DTP03_ServiceDate);
        }
    }

    /// <summary>
    /// PROVENANCE: FIND-014 - the transaction date is compared at the precision the 837 carries,
    /// which is one minute. Anything finer is not representable in BHT04 and BHT05, so comparing
    /// at full DateTime precision would report a mutation the round trip cannot avoid, and hide
    /// the real ones underneath it.
    /// </summary>
    private static string Moment(DateTime value) =>
        value.ToString("yyyyMMdd HHmm", CultureInfo.InvariantCulture);

    /// <summary>
    /// PROVENANCE: FIND-014 - an amount is compared as text at the scale its governed column
    /// declares, not at the scale the value happens to carry.
    ///
    /// Text, because 1.00m and 1m are equal as decimals and are not the same 837, which is the
    /// whole of FIND-002. At the governed scale, because the Section 2 column declaration is what
    /// defines the canonical form: a value that has passed through neither the store nor the reader
    /// carries whatever scale the arithmetic that produced it left behind, and that is a difference
    /// in representation rather than in the governed amount.
    /// </summary>
    private static string Amount(decimal value, int governedScale) =>
        (decimal.Round(value, governedScale) + new decimal(0, 0, 0, false, (byte)governedScale))
            .ToString(CultureInfo.InvariantCulture);
}
