using Translator.TestSupport;

namespace Translator.Edi.Tests;

/// <summary>
/// PROVENANCE: GOVERNANCE-5 - governance Feature 3, User Story 3.1: "As an Ingestion Engine, I want
/// to parse an incoming 837 file ... into ClaimHeader database entities." Acceptance criterion:
/// "Parsed properties map 1:1 to database columns (Loop2010BA_NM103, etc.)."
///
/// The mapping is asserted column by column rather than by comparing whole objects, so a failure
/// names the governed column that moved instead of reporting that two claims differ somewhere.
/// </summary>
public class Edi837ParserTheories
{
    private static readonly Edi837Serializer Serializer = new();
    private static readonly Edi837Parser Parser = new();

    public static IEnumerable<object[]> Claims() => GovernedClaimCorpus.ClaimIndices();

    [Theory]
    [MemberData(nameof(Claims))]
    public void Every_governed_claim_level_column_is_recovered(int index, int lineCount)
    {
        var original = GovernedClaimCorpus.Build(index, lineCount);
        var parsed = Parser.Parse(Serializer.Serialize(original));

        Assert.Equal(original.BHT03_ClaimSubmitterTransactionId, parsed.BHT03_ClaimSubmitterTransactionId);
        Assert.Equal(original.BHT04_TransactionSetCreationDate, parsed.BHT04_TransactionSetCreationDate);

        Assert.Equal(original.Loop2010AA_NM103_BillingProviderLastNameOrOrg, parsed.Loop2010AA_NM103_BillingProviderLastNameOrOrg);
        Assert.Equal(original.Loop2010AA_NM104_BillingProviderFirstName, parsed.Loop2010AA_NM104_BillingProviderFirstName);
        Assert.Equal(original.Loop2010AA_NM109_BillingProviderNpi, parsed.Loop2010AA_NM109_BillingProviderNpi);
        Assert.Equal(original.Loop2010AA_N301_BillingProviderAddressLine, parsed.Loop2010AA_N301_BillingProviderAddressLine);
        Assert.Equal(original.Loop2010AA_N401_BillingProviderCity, parsed.Loop2010AA_N401_BillingProviderCity);
        Assert.Equal(original.Loop2010AA_N402_BillingProviderState, parsed.Loop2010AA_N402_BillingProviderState);
        Assert.Equal(original.Loop2010AA_N403_BillingProviderZipCode, parsed.Loop2010AA_N403_BillingProviderZipCode);

        Assert.Equal(original.Loop2010BA_NM103_SubscriberLastName, parsed.Loop2010BA_NM103_SubscriberLastName);
        Assert.Equal(original.Loop2010BA_NM104_SubscriberFirstName, parsed.Loop2010BA_NM104_SubscriberFirstName);
        Assert.Equal(original.Loop2010BA_DMG02_SubscriberDob, parsed.Loop2010BA_DMG02_SubscriberDob);
        Assert.Equal(original.Loop2010BA_DMG03_SubscriberGender, parsed.Loop2010BA_DMG03_SubscriberGender);

        Assert.Equal(original.Loop2010BB_NM103_PayerName, parsed.Loop2010BB_NM103_PayerName);
        Assert.Equal(original.Loop2010BB_NM109_PayerId, parsed.Loop2010BB_NM109_PayerId);

        Assert.Equal(original.CLM01_ClaimControlNumber, parsed.CLM01_ClaimControlNumber);
        Assert.Equal(original.CLM02_TotalClaimChargeAmount, parsed.CLM02_TotalClaimChargeAmount);
        Assert.Equal(original.CLM05_1_PlaceOfServiceCode, parsed.CLM05_1_PlaceOfServiceCode);
        Assert.Equal(original.CLM05_3_ClaimFrequencyCode, parsed.CLM05_3_ClaimFrequencyCode);
        Assert.Equal(original.HI01_2_PrincipalDiagnosisCode, parsed.HI01_2_PrincipalDiagnosisCode);
    }

    [Theory]
    [MemberData(nameof(Claims))]
    public void Every_governed_service_line_column_is_recovered(int index, int lineCount)
    {
        var original = GovernedClaimCorpus.Build(index, lineCount);
        var parsed = Parser.Parse(Serializer.Serialize(original));

        var expected = original.LineItems.OrderBy(line => line.LX01_AssignedLineNumber).ToList();
        var actual = parsed.LineItems.OrderBy(line => line.LX01_AssignedLineNumber).ToList();

        Assert.Equal(expected.Count, actual.Count);

        for (var position = 0; position < expected.Count; position++)
        {
            Assert.Equal(expected[position].LX01_AssignedLineNumber, actual[position].LX01_AssignedLineNumber);
            Assert.Equal(expected[position].SV101_2_ProcedureCode, actual[position].SV101_2_ProcedureCode);
            Assert.Equal(expected[position].SV102_LineItemChargeAmount, actual[position].SV102_LineItemChargeAmount);
            Assert.Equal(expected[position].SV103_UnitOfMeasure, actual[position].SV103_UnitOfMeasure);
            Assert.Equal(expected[position].SV104_ServiceUnitCount, actual[position].SV104_ServiceUnitCount);
            Assert.Equal(expected[position].DTP03_ServiceDate, actual[position].DTP03_ServiceDate);
        }
    }

    /// <summary>
    /// Scale is not carried by the text (ADR-018), so it is restored from the governed column. A
    /// charge that compares equal but reads back at a different scale would change the next file
    /// written from the record, which is a Zero-Mutation violation that decimal equality misses.
    /// </summary>
    [Theory]
    [MemberData(nameof(Claims))]
    public void Recovered_amounts_carry_the_scale_their_governed_columns_declare(int index, int lineCount)
    {
        var original = GovernedClaimCorpus.Build(index, lineCount);
        var parsed = Parser.Parse(Serializer.Serialize(original));

        Assert.Equal(2, Scale(parsed.CLM02_TotalClaimChargeAmount));

        foreach (var line in parsed.LineItems)
        {
            Assert.Equal(2, Scale(line.SV102_LineItemChargeAmount));
            Assert.Equal(4, Scale(line.SV104_ServiceUnitCount));
        }
    }

    /// <summary>
    /// The delimiters are read from the interchange rather than assumed. A trading partner may send
    /// any set, and the standard says so: the element separator is the character at ISA position 4,
    /// the repetition separator is ISA11, the component separator ISA16, and the segment terminator
    /// the character after it.
    /// </summary>
    [Theory]
    [MemberData(nameof(Claims))]
    public void Interchange_written_with_other_delimiters_is_read_by_the_ones_it_declares(
        int index, int lineCount)
    {
        var original = GovernedClaimCorpus.Build(index, lineCount);
        var unusual = new Edi837Serializer(new X12Delimiters('|', '>', '^', '\''));

        var parsed = Parser.Parse(unusual.Serialize(original));

        Assert.Equal(original.CLM01_ClaimControlNumber, parsed.CLM01_ClaimControlNumber);
        Assert.Equal(original.CLM02_TotalClaimChargeAmount, parsed.CLM02_TotalClaimChargeAmount);
        Assert.Equal(original.CLM05_1_PlaceOfServiceCode, parsed.CLM05_1_PlaceOfServiceCode);
        Assert.Equal(original.HI01_2_PrincipalDiagnosisCode, parsed.HI01_2_PrincipalDiagnosisCode);
        Assert.Equal(original.LineItems.Count, parsed.LineItems.Count);
    }

    /// <summary>
    /// Storage identity has no 837 counterpart, so a parsed claim carries fresh identifiers rather
    /// than reusing anything found in the file. Its line items are attached to it.
    /// </summary>
    [Theory]
    [MemberData(nameof(Claims))]
    public void Parsed_claim_is_a_new_record_with_its_lines_attached(int index, int lineCount)
    {
        var original = GovernedClaimCorpus.Build(index, lineCount);
        var parsed = Parser.Parse(Serializer.Serialize(original));

        Assert.NotEqual(Guid.Empty, parsed.Id);
        Assert.NotEqual(original.Id, parsed.Id);
        Assert.All(parsed.LineItems, line => Assert.NotEqual(Guid.Empty, line.Id));
        Assert.Equal(parsed.LineItems.Count, parsed.LineItems.Select(line => line.Id).Distinct().Count());
    }

    /// <summary>Segments may be terminated with or without the newline the writer adds.</summary>
    [Theory]
    [MemberData(nameof(Claims))]
    public void Line_endings_between_segments_are_not_content(int index, int lineCount)
    {
        var original = GovernedClaimCorpus.Build(index, lineCount);
        var emitted = Serializer.Serialize(original);

        var unwrapped = Parser.Parse(emitted.Replace("\n", ""));
        var windows = Parser.Parse(emitted.Replace("\n", "\r\n"));

        Assert.Equal(original.CLM01_ClaimControlNumber, unwrapped.CLM01_ClaimControlNumber);
        Assert.Equal(original.CLM01_ClaimControlNumber, windows.CLM01_ClaimControlNumber);
        Assert.Equal(original.LineItems.Count, unwrapped.LineItems.Count);
        Assert.Equal(original.LineItems.Count, windows.LineItems.Count);
    }

    private static int Scale(decimal value) => (decimal.GetBits(value)[3] >> 16) & 0xFF;
}
