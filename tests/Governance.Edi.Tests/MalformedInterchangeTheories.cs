using Governance.TestSupport;

namespace Governance.Edi.Tests;

/// <summary>
/// PROVENANCE: GOVERNANCE-5 - governance Feature 3, User Story 3.1 acceptance criterion:
/// "Integration tests with malformed and valid 837 files written first."
///
/// Every case below is a well-formed-looking file that is wrong in one specific way. Each must be
/// refused with an <see cref="EdiFormatException"/> naming what is wrong. The requirement is not
/// merely that the reader survive: a malformed file that parses into a partial claim is worse than
/// one that is rejected, because the partial claim reaches the store and re-exports as a valid 837
/// that says something the sender never said.
/// </summary>
public class MalformedInterchangeTheories
{
    private static readonly Edi837Serializer Serializer = new();
    private static readonly Edi837Parser Parser = new();

    /// <summary>A valid interchange, which each Theory below then damages in one place.</summary>
    private static string Valid() => Serializer.Serialize(GovernedClaimCorpus.Build(1, 3));

    public static IEnumerable<object[]> NotInterchangesAtAll() =>
    [
        [""],
        ["   "],
        ["\n\n"],
        ["this is not an EDI file at all"],
        ["<?xml version=\"1.0\"?><claim/>"],
        ["{\"CLM01_ClaimControlNumber\":\"CLM1\"}"],
        ["ST*837*0001*005010X222A2~SE*2*0001~"],          // a transaction with no interchange
    ];

    [Theory]
    [MemberData(nameof(NotInterchangesAtAll))]
    public void Payload_that_is_not_an_interchange_is_refused(string payload)
    {
        Assert.Throws<EdiFormatException>(() => Parser.Parse(payload));
    }

    /// <summary>
    /// ISA is fixed width, and a reader locates the delimiters by offset before it knows what they
    /// are. A truncated ISA therefore cannot be read at all, and guessing at it would mean reading
    /// the rest of the file with delimiters that were never declared.
    /// </summary>
    [Theory]
    [InlineData(10)]
    [InlineData(50)]
    [InlineData(104)]
    public void Truncated_interchange_header_is_refused(int keptCharacters)
    {
        Assert.Throws<EdiFormatException>(() => Parser.Parse(Valid()[..keptCharacters]));
    }

    /// <summary>Each required segment, removed one at a time.</summary>
    public static IEnumerable<object[]> RequiredSegments() =>
        new[] { "GS", "ST", "BHT", "HL", "N3", "N4", "DMG", "CLM", "HI", "SE", "GE", "IEA" }
            .Select(id => new object[] { id });

    [Theory]
    [MemberData(nameof(RequiredSegments))]
    public void Interchange_missing_a_required_segment_is_refused(string segmentId)
    {
        var damaged = WithoutFirst(Valid(), segmentId);

        Assert.Throws<EdiFormatException>(() => Parser.Parse(damaged));
    }

    /// <summary>
    /// A claim with no service lines would satisfy the CLM02 sum invariant vacuously, at zero, and
    /// re-export as a valid 837 for a claim that bills nothing.
    /// </summary>
    [Fact]
    public void Claim_with_no_service_lines_is_refused()
    {
        var damaged = Valid();

        foreach (var segmentId in new[] { "LX", "SV1", "DTP" })
        {
            while (damaged.Contains(segmentId + "*", StringComparison.Ordinal))
            {
                damaged = WithoutFirst(damaged, segmentId);
            }
        }

        Assert.Throws<EdiFormatException>(() => Parser.Parse(damaged));
    }

    /// <summary>
    /// The control numbers at each end of an envelope must agree, and the counts must be true.
    /// These are the checks a clearinghouse makes first, and a file that fails them has been
    /// truncated, concatenated or edited in transit.
    /// </summary>
    [Theory]
    [InlineData("IEA*1*000000001~", "IEA*1*000000002~")]        // interchange control mismatch
    [InlineData("GE*1*1~", "GE*1*2~")]                          // group control mismatch
    [InlineData("SE*32*0001~", "SE*32*0002~")]                  // transaction control mismatch
    [InlineData("SE*32*0001~", "SE*99*0001~")]                  // segment count is untrue
    [InlineData("IEA*1*000000001~", "IEA*4*000000001~")]        // group count is untrue
    public void Interchange_whose_envelope_does_not_balance_is_refused(string original, string replacement)
    {
        var valid = Valid();
        Assert.Contains(original, valid, StringComparison.Ordinal);

        Assert.Throws<EdiFormatException>(() => Parser.Parse(valid.Replace(original, replacement)));
    }

    /// <summary>
    /// A transaction set that is not an 837, or an implementation guide that is not the
    /// professional one, must be refused rather than read as though it were. An 835 remittance
    /// carries CLM-like segments that would otherwise map onto governed columns.
    /// </summary>
    [Theory]
    [InlineData("ST*837*0001*005010X222A2~", "ST*835*0001*005010X221A1~")]
    [InlineData("ST*837*0001*005010X222A2~", "ST*837*0001*005010X223A3~")]
    [InlineData("GS*HC*", "GS*HP*")]
    public void Interchange_that_is_not_a_professional_claim_is_refused(string original, string replacement)
    {
        var valid = Valid();
        Assert.Contains(original, valid, StringComparison.Ordinal);

        Assert.Throws<EdiFormatException>(() => Parser.Parse(valid.Replace(original, replacement)));
    }

    /// <summary>
    /// An element whose value cannot be a governed column value is refused rather than coerced.
    /// Coercing it is how a file that says one thing becomes a record that says another.
    /// </summary>
    public static IEnumerable<object[]> UnreadableElements() =>
    [
        ["HI*ABK:I10~", "HI*ABK:~"],                     // no diagnosis code
        ["DMG*D8*", "DMG*D6*"],                          // a date format the column cannot hold
        ["*UN*", "*UNITS*"],                             // unit of measure beyond its governed width
    ];

    [Theory]
    [MemberData(nameof(UnreadableElements))]
    public void Element_that_cannot_be_a_governed_value_is_refused(string original, string replacement)
    {
        var valid = Valid();
        Assert.Contains(original, valid, StringComparison.Ordinal);

        Assert.Throws<EdiFormatException>(() => Parser.Parse(valid.Replace(original, replacement)));
    }

    /// <summary>
    /// A charge that is not a number, or carries more precision than the governed column holds,
    /// must be refused. Rounding it silently is the FIND-001 corruption: a valid file, a successful
    /// import, and the wrong money.
    /// </summary>
    [Theory]
    [InlineData("nine hundred")]
    [InlineData("1.005")]
    [InlineData("1,000.00")]
    [InlineData("")]
    public void Claim_charge_that_is_not_a_governed_amount_is_refused(string amount)
    {
        var claim = GovernedClaimCorpus.Build(1, 3);
        var valid = Serializer.Serialize(claim);
        var rendered = X12Number.Render(claim.CLM02_TotalClaimChargeAmount);

        var damaged = valid.Replace(
            $"CLM*{claim.CLM01_ClaimControlNumber}*{rendered}*",
            $"CLM*{claim.CLM01_ClaimControlNumber}*{amount}*");

        Assert.NotEqual(valid, damaged);
        Assert.Throws<EdiFormatException>(() => Parser.Parse(damaged));
    }

    /// <summary>
    /// PROVENANCE: GOVERNANCE-1 - the claim total and the sum of its line amounts are the same
    /// fact stated twice, and governance User Story 1.2 requires them to agree. A file in which
    /// they disagree is not a claim this system can hold: whichever value it stored, the other
    /// would be wrong, and the mutation would be introduced by the import rather than found by it.
    /// </summary>
    [Fact]
    public void Interchange_whose_claim_total_contradicts_its_line_amounts_is_refused()
    {
        var claim = GovernedClaimCorpus.Build(1, 3);
        var valid = Serializer.Serialize(claim);
        var rendered = X12Number.Render(claim.CLM02_TotalClaimChargeAmount);
        var inflated = X12Number.Render(claim.CLM02_TotalClaimChargeAmount + 1.00m);

        var damaged = valid.Replace(
            $"CLM*{claim.CLM01_ClaimControlNumber}*{rendered}*",
            $"CLM*{claim.CLM01_ClaimControlNumber}*{inflated}*");

        Assert.NotEqual(valid, damaged);
        Assert.Throws<EdiFormatException>(() => Parser.Parse(damaged));
    }

    /// <summary>
    /// The message must name the problem. A reader handed "input string was not in a correct
    /// format" cannot act on it, and governance requires malformed files to be handled rather than
    /// merely survived.
    /// </summary>
    [Fact]
    public void Refusal_names_what_was_wrong_with_the_file()
    {
        var damaged = WithoutFirst(Valid(), "CLM");

        var refusal = Assert.Throws<EdiFormatException>(() => Parser.Parse(damaged));

        Assert.Contains("CLM", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>The interchange with its first segment of the given identifier removed.</summary>
    private static string WithoutFirst(string interchange, string segmentId)
    {
        var start = interchange.IndexOf(segmentId + "*", StringComparison.Ordinal);
        Assert.True(start >= 0, $"The valid interchange carries no {segmentId} segment to remove.");

        var end = interchange.IndexOf('~', start);
        Assert.True(end > start, $"The {segmentId} segment is not terminated.");

        return interchange.Remove(start, end - start + 1);
    }
}
