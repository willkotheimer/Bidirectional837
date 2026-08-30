using System.Text;
using Governance.Domain.Entities;

namespace Governance.Edi;

/// <summary>
/// Governance User Story 2.1: serialises a governed <see cref="ClaimHeader"/> into an ASC X12 837
/// Professional (005010X222A2) transaction, wrapped in its own interchange.
/// </summary>
/// <remarks>
/// PROVENANCE: ADR-016 - the 837 requires elements that governance Section 2 does not store. Every
/// one of them is a constant of this writer or a function of the record, and none is read from the
/// clock, a counter or a random source. That is what makes serialisation a pure function of the
/// claim, and a pure function is the precondition for the Section 1 Zero-Mutation Rule: an export
/// that varied between calls could never reproduce the file it came from.
/// </remarks>
public sealed class Edi837Serializer
{
    /// <summary>The interchange sender. ISA06 pads it to the fixed width that element requires.</summary>
    public const string SenderId = "GOVERNANCE837";

    /// <summary>The interchange receiver. A deployment behind a real trading partner replaces it.</summary>
    public const string ReceiverId = "CLEARINGHOUSE";

    /// <summary>The 837 Professional implementation guide this writer emits.</summary>
    public const string ImplementationGuide = "005010X222A2";

    private const string InterchangeControlNumber = "000000001";
    private const string GroupControlNumber = "1";
    private const string TransactionControlNumber = "0001";

    public Edi837Serializer(X12Delimiters? delimiters = null)
        => Delimiters = delimiters ?? X12Delimiters.Default;

    public X12Delimiters Delimiters { get; }

    /// <summary>The complete interchange for one claim, ISA through IEA.</summary>
    public string Serialize(ClaimHeader claim)
    {
        ArgumentNullException.ThrowIfNull(claim);

        var createdAt = claim.BHT04_TransactionSetCreationDate;
        var transaction = TransactionSegments(claim, createdAt).ToList();

        var builder = new StringBuilder();

        AppendInterchangeHeader(builder, createdAt);

        Append(builder, new Segment("GS", [
            ["HC"], [SenderId], [ReceiverId],
            [createdAt.ToString("yyyyMMdd")], [createdAt.ToString("HHmm")],
            [GroupControlNumber], ["X"], [ImplementationGuide]]));

        foreach (var segment in transaction)
        {
            Append(builder, segment);
        }

        // SE01 counts every segment from ST through SE inclusive, SE itself included.
        Append(builder, new Segment("SE", [[(transaction.Count + 1).ToString()], [TransactionControlNumber]]));
        Append(builder, new Segment("GE", [["1"], [GroupControlNumber]]));
        Append(builder, new Segment("IEA", [["1"], [InterchangeControlNumber]]));

        return builder.ToString();
    }

    /// <summary>Every segment from ST to the last service line, in implementation guide order.</summary>
    private static IEnumerable<Segment> TransactionSegments(ClaimHeader claim, DateTime createdAt)
    {
        yield return new Segment("ST", [["837"], [TransactionControlNumber], [ImplementationGuide]]);

        yield return new Segment("BHT", [
            ["0019"], ["00"], [claim.BHT03_ClaimSubmitterTransactionId],
            [createdAt.ToString("yyyyMMdd")], [createdAt.ToString("HHmm")], ["CH"]]);

        // Loop 1000A / 1000B. Governance Section 2 stores neither party, so both are constants.
        yield return new Segment("NM1", [["41"], ["2"], [SenderId], [""], [""], [""], [""], ["46"], [SenderId]]);
        yield return new Segment("PER", [["IC"], [SenderId], ["TE"], ["8005550100"]]);
        yield return new Segment("NM1", [["40"], ["2"], [ReceiverId], [""], [""], [""], [""], ["46"], [ReceiverId]]);

        // Loop 2000A / 2010AA - billing provider. NM102 distinguishes a person from an
        // organisation, and Loop2010AA_NM104 being the one nullable column is where governance
        // carries that distinction.
        yield return new Segment("HL", [["1"], [""], ["20"], ["1"]]);
        yield return new Segment("NM1", [
            ["85"],
            [claim.Loop2010AA_NM104_BillingProviderFirstName is null ? "2" : "1"],
            [claim.Loop2010AA_NM103_BillingProviderLastNameOrOrg],
            [claim.Loop2010AA_NM104_BillingProviderFirstName ?? ""],
            [""], [""], [""],
            ["XX"], [claim.Loop2010AA_NM109_BillingProviderNpi]]);
        yield return new Segment("N3", [[claim.Loop2010AA_N301_BillingProviderAddressLine]]);
        yield return new Segment("N4", [
            [claim.Loop2010AA_N401_BillingProviderCity],
            [claim.Loop2010AA_N402_BillingProviderState],
            [claim.Loop2010AA_N403_BillingProviderZipCode]]);

        // Loop 2000B / 2010BA - subscriber, who is the patient here: SBR02 is 18, "self".
        yield return new Segment("HL", [["2"], ["1"], ["22"], ["0"]]);
        yield return new Segment("SBR", [["P"], ["18"], [""], [""], [""], [""], [""], [""], ["CI"]]);
        yield return new Segment("NM1", [
            ["IL"], ["1"],
            [claim.Loop2010BA_NM103_SubscriberLastName],
            [claim.Loop2010BA_NM104_SubscriberFirstName],
            [""], [""], [""],
            ["MI"], [claim.CLM01_ClaimControlNumber]]);
        yield return new Segment("DMG", [
            ["D8"], [claim.Loop2010BA_DMG02_SubscriberDob], [claim.Loop2010BA_DMG03_SubscriberGender]]);

        // Loop 2010BB - payer.
        yield return new Segment("NM1", [
            ["PR"], ["2"], [claim.Loop2010BB_NM103_PayerName], [""], [""], [""], [""],
            ["PI"], [claim.Loop2010BB_NM109_PayerId]]);

        // Loop 2300 - claim. CLM05 is a composite whose first and third components are governed
        // columns; the second is the facility code qualifier, B for a professional claim.
        yield return new Segment("CLM", [
            [claim.CLM01_ClaimControlNumber],
            [X12Number.Render(claim.CLM02_TotalClaimChargeAmount)],
            [""], [""],
            [claim.CLM05_1_PlaceOfServiceCode, "B", claim.CLM05_3_ClaimFrequencyCode],
            ["Y"], ["A"], ["Y"], ["Y"]]);
        yield return new Segment("HI", [["ABK", Icd10Code.ToX12(claim.HI01_2_PrincipalDiagnosisCode)]]);

        // Loop 2400 - service lines, in governed line number order.
        foreach (var line in claim.LineItems.OrderBy(item => item.LX01_AssignedLineNumber))
        {
            yield return new Segment("LX", [[line.LX01_AssignedLineNumber.ToString()]]);
            yield return new Segment("SV1", [
                ["HC", line.SV101_2_ProcedureCode],
                [X12Number.Render(line.SV102_LineItemChargeAmount)],
                [line.SV103_UnitOfMeasure],
                [X12Number.Render(line.SV104_ServiceUnitCount)],
                [""], [""], ["1"]]);
            yield return new Segment("DTP", [["472"], ["D8"], [line.DTP03_ServiceDate]]);
        }
    }

    /// <summary>
    /// ISA is the only fixed-width segment in X12: every element has a defined length, so a reader
    /// can locate the delimiters by offset before it knows what they are. Its padding is content,
    /// not formatting, and the segment is 105 characters before its terminator.
    /// </summary>
    private void AppendInterchangeHeader(StringBuilder builder, DateTime createdAt)
    {
        builder
            .Append("ISA").Append(Delimiters.Element)
            .Append("00").Append(Delimiters.Element)              // ISA01 no authorization
            .Append(new string(' ', 10)).Append(Delimiters.Element)
            .Append("00").Append(Delimiters.Element)              // ISA03 no security
            .Append(new string(' ', 10)).Append(Delimiters.Element)
            .Append("ZZ").Append(Delimiters.Element)              // ISA05 mutually defined
            .Append(SenderId.PadRight(15)).Append(Delimiters.Element)
            .Append("ZZ").Append(Delimiters.Element)
            .Append(ReceiverId.PadRight(15)).Append(Delimiters.Element)
            .Append(createdAt.ToString("yyMMdd")).Append(Delimiters.Element)
            .Append(createdAt.ToString("HHmm")).Append(Delimiters.Element)
            .Append(Delimiters.Repetition).Append(Delimiters.Element)
            .Append("00501").Append(Delimiters.Element)           // ISA12 control version
            .Append(InterchangeControlNumber).Append(Delimiters.Element)
            .Append('0').Append(Delimiters.Element)               // ISA14 no acknowledgment
            .Append('P').Append(Delimiters.Element)               // ISA15 production
            .Append(Delimiters.Component)                         // ISA16 declares the separator
            .Append(Delimiters.Segment).Append('\n');
    }

    /// <summary>
    /// Writes one segment, refusing any component that carries a delimiter. An unescaped separator
    /// inside a name or an address splits one element into two and shifts every element after it,
    /// which is how an EDI file becomes silently wrong rather than loudly invalid. Components are
    /// checked individually and joined afterwards, so the separators this writer places are never
    /// mistaken for separators a governed value smuggled in.
    /// </summary>
    private void Append(StringBuilder builder, Segment segment)
    {
        builder.Append(segment.Id);

        foreach (var element in segment.Elements)
        {
            foreach (var component in element)
            {
                if (Delimiters.CollidesWith(component))
                {
                    throw new InvalidOperationException(
                        $"Segment {segment.Id} cannot be written: '{component}' carries an X12 " +
                        "delimiter, which would split the element and shift every element after it.");
                }
            }

            builder.Append(Delimiters.Element).Append(string.Join(Delimiters.Component, element));
        }

        builder.Append(Delimiters.Segment).Append('\n');
    }

    /// <summary>
    /// One segment: an identifier and its elements, each of which is a list of components. A
    /// simple element is a list of one, which is the same shape the standard gives it.
    /// </summary>
    private sealed record Segment(string Id, string[][] Elements);
}
