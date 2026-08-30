using Translator.Domain.Entities;
using Translator.TestSupport;

namespace Translator.Edi.Tests;

/// <summary>
/// PROVENANCE: GOVERNANCE-5 - governance Feature 2, User Story 2.1: "As a System, I want to
/// serialize ClaimHeader records into valid ASC X12 837 EDI format, so that clearinghouses can
/// process them." Acceptance criteria: "Unit tests validating segment headers (ISA, GS, ST, BHT,
/// CLM, SE) written first."
///
/// Each of the six segments governance names has its own Theory below, asserted over the whole
/// corpus rather than over one example claim.
/// </summary>
public class Edi837SegmentTheories
{
    private static readonly Edi837Serializer Serializer = new();

    public static IEnumerable<object[]> Claims() => GovernedClaimCorpus.ClaimIndices();

    private static string Serialize(int index, int lineCount) =>
        Serializer.Serialize(GovernedClaimCorpus.Build(index, lineCount));

    /// <summary>
    /// ISA is the interchange control header and the only fixed-width segment in X12: 105
    /// characters plus its terminator, with every element at a known offset. A reader that
    /// discovers the delimiters by position, as the standard directs, gets the wrong answer from
    /// an ISA of any other length.
    /// </summary>
    [Theory]
    [MemberData(nameof(Claims))]
    public void ISA_is_the_fixed_width_interchange_header_the_standard_defines(int index, int lineCount)
    {
        var edi = Serialize(index, lineCount);
        var isa = X12TestReader.Single(edi, "ISA");

        Assert.StartsWith("ISA*", edi, StringComparison.Ordinal);
        Assert.Equal(106, edi.IndexOf('~') + 1);
        Assert.Equal(16, isa.Elements.Count);

        Assert.Equal("00", isa[1]);                     // no authorization information
        Assert.Equal(new string(' ', 10), isa[2]);
        Assert.Equal("00", isa[3]);                     // no security information
        Assert.Equal(new string(' ', 10), isa[4]);
        Assert.Equal("ZZ", isa[5]);                     // mutually defined sender qualifier
        Assert.Equal(15, isa[6].Length);
        Assert.Equal("ZZ", isa[7]);                     // mutually defined receiver qualifier
        Assert.Equal(15, isa[8].Length);
        Assert.Equal("^", isa[11]);                     // 5010 repetition separator
        Assert.Equal("00501", isa[12]);                 // interchange control version
        Assert.Equal("0", isa[14]);                     // no acknowledgment requested
        Assert.Equal("P", isa[15]);                     // production usage
        Assert.Equal(":", isa[16]);                     // component separator
    }

    /// <summary>
    /// ISA09 and ISA10 are the interchange date and time. They are taken from the stored BHT04
    /// rather than from the clock, because a clock reading would make the same claim serialise
    /// differently on every call and break the Section 1 Reversibility Guarantee.
    /// </summary>
    [Theory]
    [MemberData(nameof(Claims))]
    public void ISA_date_and_time_come_from_the_stored_transaction_date(int index, int lineCount)
    {
        var claim = GovernedClaimCorpus.Build(index, lineCount);
        var isa = X12TestReader.Single(Serializer.Serialize(claim), "ISA");

        Assert.Equal(claim.BHT04_TransactionSetCreationDate.ToString("yyMMdd"), isa[9]);
        Assert.Equal(claim.BHT04_TransactionSetCreationDate.ToString("HHmm"), isa[10]);
    }

    /// <summary>GS is the functional group header. GS08 names the implementation guide.</summary>
    [Theory]
    [MemberData(nameof(Claims))]
    public void GS_declares_the_professional_claim_implementation_guide(int index, int lineCount)
    {
        var claim = GovernedClaimCorpus.Build(index, lineCount);
        var gs = X12TestReader.Single(Serializer.Serialize(claim), "GS");

        Assert.Equal("HC", gs[1]);                      // health care claim
        Assert.Equal(claim.BHT04_TransactionSetCreationDate.ToString("yyyyMMdd"), gs[4]);
        Assert.Equal(claim.BHT04_TransactionSetCreationDate.ToString("HHmm"), gs[5]);
        Assert.Equal("X", gs[7]);                       // accredited standards committee X12
        Assert.Equal("005010X222A2", gs[8]);
    }

    /// <summary>ST opens the transaction set. ST01 is the transaction set identifier: 837.</summary>
    [Theory]
    [MemberData(nameof(Claims))]
    public void ST_opens_an_837_transaction_naming_the_same_guide_as_GS(int index, int lineCount)
    {
        var edi = Serialize(index, lineCount);
        var st = X12TestReader.Single(edi, "ST");

        Assert.Equal("837", st[1]);
        Assert.Equal("005010X222A2", st[3]);
        Assert.Equal(X12TestReader.Single(edi, "GS")[8], st[3]);
    }

    /// <summary>
    /// BHT is the beginning of the hierarchical transaction, and the one governed segment whose
    /// contents governance Section 2 stores directly: BHT03 and BHT04 are columns.
    /// </summary>
    [Theory]
    [MemberData(nameof(Claims))]
    public void BHT_carries_the_governed_submitter_transaction_id_and_creation_date(int index, int lineCount)
    {
        var claim = GovernedClaimCorpus.Build(index, lineCount);
        var bht = X12TestReader.Single(Serializer.Serialize(claim), "BHT");

        Assert.Equal("0019", bht[1]);                   // information source, subscriber, dependent
        Assert.Equal("00", bht[2]);                     // original transaction
        Assert.Equal(claim.BHT03_ClaimSubmitterTransactionId, bht[3]);
        Assert.Equal(claim.BHT04_TransactionSetCreationDate.ToString("yyyyMMdd"), bht[4]);
        Assert.Equal(claim.BHT04_TransactionSetCreationDate.ToString("HHmm"), bht[5]);
        Assert.Equal("CH", bht[6]);                     // chargeable
    }

    /// <summary>
    /// CLM is the claim segment. CLM01, CLM02 and the two governed components of the CLM05
    /// composite are all columns in governance Section 2, named for these positions.
    /// </summary>
    [Theory]
    [MemberData(nameof(Claims))]
    public void CLM_carries_every_governed_claim_level_column(int index, int lineCount)
    {
        var claim = GovernedClaimCorpus.Build(index, lineCount);
        var clm = X12TestReader.Single(Serializer.Serialize(claim), "CLM");

        Assert.Equal(claim.CLM01_ClaimControlNumber, clm[1]);
        Assert.Equal(X12Number.Render(claim.CLM02_TotalClaimChargeAmount), clm[2]);
        Assert.Equal(claim.CLM05_1_PlaceOfServiceCode, clm.Component(5, 1));
        Assert.Equal("B", clm.Component(5, 2));         // facility code qualifier
        Assert.Equal(claim.CLM05_3_ClaimFrequencyCode, clm.Component(5, 3));
    }

    /// <summary>
    /// SE closes the transaction set. SE01 is the count of segments it closes, inclusive of ST and
    /// SE themselves, and SE02 repeats ST02. A clearinghouse rejects the file on either mismatch.
    /// </summary>
    [Theory]
    [MemberData(nameof(Claims))]
    public void SE_counts_the_segments_it_closes_and_repeats_the_ST_control_number(int index, int lineCount)
    {
        var edi = Serialize(index, lineCount);
        var segments = X12TestReader.Read(edi).ToList();

        var start = segments.FindIndex(segment => segment.Id == "ST");
        var end = segments.FindIndex(segment => segment.Id == "SE");

        Assert.InRange(start, 0, segments.Count - 1);
        Assert.InRange(end, start, segments.Count - 1);
        Assert.Equal(end - start + 1, int.Parse(segments[end][1]));
        Assert.Equal(X12TestReader.Single(edi, "ST")[2], segments[end][2]);
    }

    /// <summary>
    /// Every governed Section 2 column reaches the stream. This is the precondition for the
    /// Section 1 Reversibility Guarantee: a column the writer drops cannot be recovered by any
    /// reader, so the round trip would fail however good the parser is.
    /// </summary>
    [Theory]
    [MemberData(nameof(Claims))]
    public void Every_governed_column_appears_in_the_emitted_interchange(int index, int lineCount)
    {
        var claim = GovernedClaimCorpus.Build(index, lineCount);
        var edi = Serializer.Serialize(claim);

        foreach (var expected in GovernedValues(claim))
        {
            Assert.Contains(expected, edi, StringComparison.Ordinal);
        }
    }

    /// <summary>Every governed value, in the textual form the 837 is expected to carry it in.</summary>
    private static IEnumerable<string> GovernedValues(ClaimHeader claim)
    {
        yield return claim.BHT03_ClaimSubmitterTransactionId;
        yield return claim.BHT04_TransactionSetCreationDate.ToString("yyyyMMdd");
        yield return claim.Loop2010AA_NM103_BillingProviderLastNameOrOrg;
        if (claim.Loop2010AA_NM104_BillingProviderFirstName is { } firstName) yield return firstName;
        yield return claim.Loop2010AA_NM109_BillingProviderNpi;
        yield return claim.Loop2010AA_N301_BillingProviderAddressLine;
        yield return claim.Loop2010AA_N401_BillingProviderCity;
        yield return claim.Loop2010AA_N402_BillingProviderState;
        yield return claim.Loop2010AA_N403_BillingProviderZipCode;
        yield return claim.Loop2010BA_NM103_SubscriberLastName;
        yield return claim.Loop2010BA_NM104_SubscriberFirstName;
        yield return claim.Loop2010BA_DMG02_SubscriberDob;
        yield return claim.Loop2010BA_DMG03_SubscriberGender;
        yield return claim.Loop2010BB_NM103_PayerName;
        yield return claim.Loop2010BB_NM109_PayerId;
        yield return claim.CLM01_ClaimControlNumber;
        yield return X12Number.Render(claim.CLM02_TotalClaimChargeAmount);
        yield return claim.CLM05_1_PlaceOfServiceCode;
        yield return claim.CLM05_3_ClaimFrequencyCode;
        yield return Icd10Code.ToX12(claim.HI01_2_PrincipalDiagnosisCode);

        foreach (var line in claim.LineItems)
        {
            yield return line.SV101_2_ProcedureCode;
            yield return X12Number.Render(line.SV102_LineItemChargeAmount);
            yield return line.SV103_UnitOfMeasure;
            yield return X12Number.Render(line.SV104_ServiceUnitCount);
            yield return line.DTP03_ServiceDate;
        }
    }
}
