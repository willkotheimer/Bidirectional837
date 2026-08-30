using System.Text.RegularExpressions;
using Governance.Domain.Entities;
using Governance.TestSupport;

namespace Governance.Edi.Tests;

/// <summary>
/// PROVENANCE: GOVERNANCE-5 - governance Feature 2, User Story 2.1 acceptance criterion: "Output
/// validates against HIPAA 5010 syntax rules."
///
/// The rules asserted here are the ones a clearinghouse enforces before it looks at a single claim
/// field: the envelope must balance, its control numbers must agree, its counts must be true, its
/// segments must arrive in the order the guide mandates, and no element may carry a character that
/// the stream uses as punctuation. A file that fails any of these is rejected whole.
/// </summary>
public class X12SyntaxTheories
{
    private static readonly Edi837Serializer Serializer = new();
    private static readonly Regex SegmentId = new("^[A-Z][A-Z0-9]{1,2}$", RegexOptions.Compiled);

    public static IEnumerable<object[]> Claims() => GovernedClaimCorpus.ClaimIndices();

    private static string Serialize(int index, int lineCount) =>
        Serializer.Serialize(GovernedClaimCorpus.Build(index, lineCount));

    /// <summary>
    /// The three X12 envelopes nest strictly: ISA closes with IEA, GS with GE, ST with SE, and
    /// each pair carries the same control number at both ends.
    /// </summary>
    [Theory]
    [MemberData(nameof(Claims))]
    public void Envelopes_nest_and_their_control_numbers_agree_at_both_ends(int index, int lineCount)
    {
        var edi = Serialize(index, lineCount);
        var ids = X12TestReader.Read(edi).Select(segment => segment.Id).ToList();

        Assert.Equal("ISA", ids[0]);
        Assert.Equal("GS", ids[1]);
        Assert.Equal("IEA", ids[^1]);
        Assert.Equal("GE", ids[^2]);
        Assert.True(ids.IndexOf("ST") < ids.IndexOf("SE"));

        Assert.Equal(X12TestReader.Single(edi, "ISA")[13], X12TestReader.Single(edi, "IEA")[2]);
        Assert.Equal(X12TestReader.Single(edi, "GS")[6], X12TestReader.Single(edi, "GE")[2]);
        Assert.Equal(X12TestReader.Single(edi, "ST")[2], X12TestReader.Single(edi, "SE")[2]);
    }

    /// <summary>GE01 counts the transaction sets in the group; IEA01 counts the groups.</summary>
    [Theory]
    [MemberData(nameof(Claims))]
    public void Envelope_counts_state_what_the_interchange_actually_contains(int index, int lineCount)
    {
        var edi = Serialize(index, lineCount);

        Assert.Equal(X12TestReader.All(edi, "ST").Count, int.Parse(X12TestReader.Single(edi, "GE")[1]));
        Assert.Equal(X12TestReader.All(edi, "GS").Count, int.Parse(X12TestReader.Single(edi, "IEA")[1]));
    }

    /// <summary>
    /// The 005010X222A2 guide fixes the segment order, and a reader that walks the hierarchy in
    /// one pass depends on it. The expected skeleton below is read off the guide, not off the
    /// writer.
    /// </summary>
    [Theory]
    [MemberData(nameof(Claims))]
    public void Segments_arrive_in_the_order_the_implementation_guide_mandates(int index, int lineCount)
    {
        var expected = new List<string>
        {
            "ISA", "GS", "ST", "BHT",
            "NM1", "PER",                   // 1000A submitter and its contact
            "NM1",                          // 1000B receiver
            "HL", "NM1", "N3", "N4",        // 2000A / 2010AA billing provider
            "HL", "SBR", "NM1", "DMG",      // 2000B / 2010BA subscriber
            "NM1",                          // 2010BB payer
            "CLM", "HI",                    // 2300 claim
        };

        for (var line = 0; line < lineCount; line++)
        {
            expected.AddRange(["LX", "SV1", "DTP"]);   // 2400 service line
        }

        expected.AddRange(["SE", "GE", "IEA"]);

        Assert.Equal(expected, X12TestReader.Read(Serialize(index, lineCount)).Select(s => s.Id).ToList());
    }

    /// <summary>
    /// The hierarchical level segments carry the 837 structure: a billing provider level with a
    /// subordinate subscriber level beneath it, and no level below that.
    /// </summary>
    [Theory]
    [MemberData(nameof(Claims))]
    public void Hierarchical_levels_declare_a_subscriber_beneath_the_billing_provider(int index, int lineCount)
    {
        var levels = X12TestReader.All(Serialize(index, lineCount), "HL");

        Assert.Equal(2, levels.Count);

        Assert.Equal("1", levels[0][1]);            // HL01 identifier
        Assert.Equal("", levels[0][2]);             // HL02 no parent
        Assert.Equal("20", levels[0][3]);           // HL03 information source
        Assert.Equal("1", levels[0][4]);            // HL04 child levels follow

        Assert.Equal("2", levels[1][1]);
        Assert.Equal("1", levels[1][2]);            // HL02 parent is the billing provider
        Assert.Equal("22", levels[1][3]);           // HL03 subscriber
        Assert.Equal("0", levels[1][4]);            // HL04 no dependent level
    }

    /// <summary>
    /// Each NM1 in the transaction is identified by its entity identifier code, and each governed
    /// party sits under the code the guide assigns it.
    /// </summary>
    [Theory]
    [MemberData(nameof(Claims))]
    public void Governed_parties_are_named_under_their_guide_assigned_entity_codes(int index, int lineCount)
    {
        var claim = GovernedClaimCorpus.Build(index, lineCount);
        var names = X12TestReader.All(Serializer.Serialize(claim), "NM1")
            .ToDictionary(segment => segment[1], segment => segment);

        Assert.Equal(claim.Loop2010AA_NM103_BillingProviderLastNameOrOrg, names["85"][3]);
        Assert.Equal(claim.Loop2010AA_NM104_BillingProviderFirstName ?? "", names["85"][4]);
        Assert.Equal("XX", names["85"][8]);         // NM108 national provider identifier
        Assert.Equal(claim.Loop2010AA_NM109_BillingProviderNpi, names["85"][9]);

        Assert.Equal(claim.Loop2010BA_NM103_SubscriberLastName, names["IL"][3]);
        Assert.Equal(claim.Loop2010BA_NM104_SubscriberFirstName, names["IL"][4]);

        Assert.Equal(claim.Loop2010BB_NM103_PayerName, names["PR"][3]);
        Assert.Equal("PI", names["PR"][8]);         // NM108 payer identification
        Assert.Equal(claim.Loop2010BB_NM109_PayerId, names["PR"][9]);
    }

    /// <summary>
    /// NM102 distinguishes a person from an organisation, and governance carries the distinction
    /// in whether the billing provider has a first name: Loop2010AA_NM104 is the one nullable
    /// column in the provider block.
    /// </summary>
    [Theory]
    [MemberData(nameof(Claims))]
    public void Billing_provider_entity_type_follows_the_presence_of_a_first_name(int index, int lineCount)
    {
        var claim = GovernedClaimCorpus.Build(index, lineCount);
        var billing = X12TestReader.All(Serializer.Serialize(claim), "NM1").Single(nm1 => nm1[1] == "85");

        Assert.Equal(claim.Loop2010AA_NM104_BillingProviderFirstName is null ? "2" : "1", billing[2]);
    }

    /// <summary>The subscriber demographic segment carries the governed date format and gender.</summary>
    [Theory]
    [MemberData(nameof(Claims))]
    public void Subscriber_demographics_declare_the_governed_date_format(int index, int lineCount)
    {
        var claim = GovernedClaimCorpus.Build(index, lineCount);
        var dmg = X12TestReader.Single(Serializer.Serialize(claim), "DMG");

        Assert.Equal("D8", dmg[1]);                 // CCYYMMDD, which is what the column stores
        Assert.Equal(claim.Loop2010BA_DMG02_SubscriberDob, dmg[2]);
        Assert.Equal(claim.Loop2010BA_DMG03_SubscriberGender, dmg[3]);
    }

    /// <summary>
    /// The principal diagnosis is carried in HI01 as a composite: the ABK qualifier for an ICD-10
    /// principal diagnosis, then the code itself.
    /// </summary>
    [Theory]
    [MemberData(nameof(Claims))]
    public void Principal_diagnosis_is_qualified_as_ICD_10(int index, int lineCount)
    {
        var claim = GovernedClaimCorpus.Build(index, lineCount);
        var hi = X12TestReader.Single(Serializer.Serialize(claim), "HI");

        Assert.Equal("ABK", hi.Component(1, 1));
        Assert.Equal(Icd10Code.ToX12(claim.HI01_2_PrincipalDiagnosisCode), hi.Component(1, 2));
    }

    /// <summary>
    /// Every governed service line reaches the stream as its own LX / SV1 / DTP trio, with the
    /// governed line number, procedure code, charge, unit of measure, quantity and service date.
    /// </summary>
    [Theory]
    [MemberData(nameof(Claims))]
    public void Every_service_line_is_written_with_its_governed_columns(int index, int lineCount)
    {
        var claim = GovernedClaimCorpus.Build(index, lineCount);
        var edi = Serializer.Serialize(claim);

        var lx = X12TestReader.All(edi, "LX");
        var sv1 = X12TestReader.All(edi, "SV1");
        var dtp = X12TestReader.All(edi, "DTP");
        var expected = claim.LineItems.OrderBy(line => line.LX01_AssignedLineNumber).ToList();

        Assert.Equal(expected.Count, lx.Count);
        Assert.Equal(expected.Count, sv1.Count);
        Assert.Equal(expected.Count, dtp.Count);

        for (var position = 0; position < expected.Count; position++)
        {
            var line = expected[position];

            Assert.Equal(line.LX01_AssignedLineNumber.ToString(), lx[position][1]);
            Assert.Equal("HC", sv1[position].Component(1, 1));       // HCPCS / CPT qualifier
            Assert.Equal(line.SV101_2_ProcedureCode, sv1[position].Component(1, 2));
            Assert.Equal(X12Number.Render(line.SV102_LineItemChargeAmount), sv1[position][2]);
            Assert.Equal(line.SV103_UnitOfMeasure, sv1[position][3]);
            Assert.Equal(X12Number.Render(line.SV104_ServiceUnitCount), sv1[position][4]);
            Assert.Equal("472", dtp[position][1]);                   // date of service
            Assert.Equal("D8", dtp[position][2]);
            Assert.Equal(line.DTP03_ServiceDate, dtp[position][3]);
        }
    }

    /// <summary>
    /// Governance User Story 1.2 restated at the EDI boundary: the CLM02 a clearinghouse reads
    /// must equal the sum of the SV102 amounts it reads in the same file. The generator holds
    /// this invariant over its own objects; this holds it over the emitted text, which is what a
    /// payer actually adjudicates.
    /// </summary>
    [Theory]
    [MemberData(nameof(Claims))]
    public void Claim_total_in_the_stream_equals_the_sum_of_the_line_amounts_in_the_stream(int index, int lineCount)
    {
        var edi = Serialize(index, lineCount);

        var total = X12Number.Parse(X12TestReader.Single(edi, "CLM")[2], 2);
        var lineSum = X12TestReader.All(edi, "SV1").Sum(sv1 => X12Number.Parse(sv1[2], 2));

        Assert.Equal(total, lineSum);
    }

    /// <summary>Every segment is terminated, non-empty, and identified by a well-formed id.</summary>
    [Theory]
    [MemberData(nameof(Claims))]
    public void Every_segment_is_terminated_and_carries_a_well_formed_identifier(int index, int lineCount)
    {
        var edi = Serialize(index, lineCount);

        Assert.EndsWith("~", edi.TrimEnd('\r', '\n'), StringComparison.Ordinal);

        foreach (var segment in X12TestReader.Read(edi))
        {
            Assert.Matches(SegmentId, segment.Id);
            Assert.NotEmpty(segment.Elements);
        }
    }

    /// <summary>
    /// No element may contain a delimiter. A single unescaped separator inside a name or an
    /// address splits one element into two and shifts every element after it, which is the
    /// classic way an EDI file becomes silently wrong rather than loudly invalid.
    /// </summary>
    [Theory]
    [MemberData(nameof(Claims))]
    public void No_emitted_element_carries_a_delimiter_character(int index, int lineCount)
    {
        var delimiters = X12Delimiters.Default;

        foreach (var segment in X12TestReader.Read(Serialize(index, lineCount)))
        {
            // The composite separator is legitimate punctuation inside a composite element, and
            // ISA16 declares it, so it is checked at component level rather than element level.
            foreach (var component in segment.Elements.SelectMany(element => element.Split(':')))
            {
                Assert.False(delimiters.CollidesWith(component),
                    $"Segment {segment.Id} carries a delimiter inside '{component}'.");
            }
        }
    }

    /// <summary>
    /// A governed value that carries a delimiter cannot be written safely, and the writer must
    /// say so rather than emit a stream that parses into different data than it was given.
    /// </summary>
    [Theory]
    [InlineData("PAYER*NAME")]
    [InlineData("PAYER~NAME")]
    [InlineData("PAYER:NAME")]
    [InlineData("PAYER^NAME")]
    public void Serialising_a_value_carrying_a_delimiter_is_refused(string payerName)
    {
        var claim = GovernedClaimCorpus.Build(1);
        claim.Loop2010BB_NM103_PayerName = payerName;

        Assert.Throws<InvalidOperationException>(() => Serializer.Serialize(claim));
    }

    /// <summary>
    /// PROVENANCE: GOVERNANCE-1 - the Zero-Mutation Rule requires that re-exporting an unedited
    /// record reproduce its payload. That is only possible if serialisation is a pure function of
    /// the record: any reading of the clock, of a counter or of a random source would make the
    /// second export differ from the first.
    /// </summary>
    [Theory]
    [MemberData(nameof(Claims))]
    public void Serialising_the_same_claim_twice_yields_the_same_bytes(int index, int lineCount)
    {
        var claim = GovernedClaimCorpus.Build(index, lineCount);

        Assert.Equal(Serializer.Serialize(claim), Serializer.Serialize(claim));
    }

    /// <summary>
    /// PROVENANCE: GOVERNANCE-1 - the stronger half of the same rule. The database identity of a
    /// claim is a storage artefact with no 837 counterpart, so an importer cannot recover it. If
    /// the writer let it reach the stream, a re-import would produce a record whose re-export
    /// differed from the file it came from, and the round trip could never close.
    /// </summary>
    [Theory]
    [MemberData(nameof(Claims))]
    public void Storage_identity_does_not_reach_the_stream(int index, int lineCount)
    {
        var claim = GovernedClaimCorpus.Build(index, lineCount);
        var edi = Serializer.Serialize(claim);

        Assert.DoesNotContain(claim.Id.ToString("N"), edi, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(claim.Id.ToString("D"), edi, StringComparison.OrdinalIgnoreCase);

        foreach (var line in claim.LineItems)
        {
            Assert.DoesNotContain(line.Id.ToString("N"), edi, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(line.Id.ToString("D"), edi, StringComparison.OrdinalIgnoreCase);
        }

        var reissued = Reidentify(claim);
        Assert.Equal(edi, Serializer.Serialize(reissued));
    }

    /// <summary>The same governed content under fresh database identities.</summary>
    private static ClaimHeader Reidentify(ClaimHeader claim)
    {
        var copy = new ClaimHeader
        {
            Id = Guid.NewGuid(),
            BHT03_ClaimSubmitterTransactionId = claim.BHT03_ClaimSubmitterTransactionId,
            BHT04_TransactionSetCreationDate = claim.BHT04_TransactionSetCreationDate,
            Loop2010AA_NM103_BillingProviderLastNameOrOrg = claim.Loop2010AA_NM103_BillingProviderLastNameOrOrg,
            Loop2010AA_NM104_BillingProviderFirstName = claim.Loop2010AA_NM104_BillingProviderFirstName,
            Loop2010AA_NM109_BillingProviderNpi = claim.Loop2010AA_NM109_BillingProviderNpi,
            Loop2010AA_N301_BillingProviderAddressLine = claim.Loop2010AA_N301_BillingProviderAddressLine,
            Loop2010AA_N401_BillingProviderCity = claim.Loop2010AA_N401_BillingProviderCity,
            Loop2010AA_N402_BillingProviderState = claim.Loop2010AA_N402_BillingProviderState,
            Loop2010AA_N403_BillingProviderZipCode = claim.Loop2010AA_N403_BillingProviderZipCode,
            Loop2010BA_NM103_SubscriberLastName = claim.Loop2010BA_NM103_SubscriberLastName,
            Loop2010BA_NM104_SubscriberFirstName = claim.Loop2010BA_NM104_SubscriberFirstName,
            Loop2010BA_DMG02_SubscriberDob = claim.Loop2010BA_DMG02_SubscriberDob,
            Loop2010BA_DMG03_SubscriberGender = claim.Loop2010BA_DMG03_SubscriberGender,
            Loop2010BB_NM103_PayerName = claim.Loop2010BB_NM103_PayerName,
            Loop2010BB_NM109_PayerId = claim.Loop2010BB_NM109_PayerId,
            CLM01_ClaimControlNumber = claim.CLM01_ClaimControlNumber,
            CLM02_TotalClaimChargeAmount = claim.CLM02_TotalClaimChargeAmount,
            CLM05_1_PlaceOfServiceCode = claim.CLM05_1_PlaceOfServiceCode,
            CLM05_3_ClaimFrequencyCode = claim.CLM05_3_ClaimFrequencyCode,
            HI01_2_PrincipalDiagnosisCode = claim.HI01_2_PrincipalDiagnosisCode,
        };

        foreach (var line in claim.LineItems)
        {
            copy.LineItems.Add(new ClaimLineItem
            {
                Id = Guid.NewGuid(),
                LX01_AssignedLineNumber = line.LX01_AssignedLineNumber,
                SV101_2_ProcedureCode = line.SV101_2_ProcedureCode,
                SV102_LineItemChargeAmount = line.SV102_LineItemChargeAmount,
                SV103_UnitOfMeasure = line.SV103_UnitOfMeasure,
                SV104_ServiceUnitCount = line.SV104_ServiceUnitCount,
                DTP03_ServiceDate = line.DTP03_ServiceDate,
            });
        }

        return copy;
    }
}
