using Translator.TestSupport;

namespace Translator.Edi.Tests;

/// <summary>
/// PROVENANCE: GOVERNANCE-4 - the Roundtrip Reversibility Test Standard: "Every pipeline build must
/// run an automated test verifying that Import(837_original) -> DB Record -> Export() ->
/// 837_regenerated == 837_original."
///
/// PROVENANCE: GOVERNANCE-1 - the Zero-Mutation Rule those tests exist to prove: "An 837 file
/// imported into the application must yield a stored database record that, when re-exported without
/// user edits, produces a byte-equivalent or functionally identical 837 payload. Re-importing that
/// generated 837 must result in a duplicate bill record identical to the original import."
///
/// Both directions of the loop are asserted here, over every claim in the corpus, and the assertion
/// is byte equality rather than the functional equivalence governance would also accept. The
/// stricter reading is available to us because the writer is a pure function of the record
/// (ADR-016), and a guarantee proven at its strict reading cannot quietly decay into its loose one.
/// </summary>
public class ReversibilityTheories
{
    private static readonly Edi837Serializer Serializer = new();
    private static readonly Edi837Parser Parser = new();
    private static readonly ReversibilityVerifier Verifier = new(Serializer, Parser);

    public static IEnumerable<object[]> Claims() => GovernedClaimCorpus.ClaimIndices();

    /// <summary>
    /// The governed standard, stated as governance states it: a file read in and written back out
    /// is the file that came in.
    /// </summary>
    [Theory]
    [MemberData(nameof(Claims))]
    public void Interchange_read_in_and_written_back_out_is_byte_identical(int index, int lineCount)
    {
        var original = Serializer.Serialize(GovernedClaimCorpus.Build(index, lineCount));

        var regenerated = Serializer.Serialize(Parser.Parse(original));

        Assert.Equal(original, regenerated);
    }

    /// <summary>
    /// The second sentence of the Zero-Mutation Rule: re-importing the generated file must yield a
    /// record identical to the first import. This is the half that catches a reader and a writer
    /// that agree with each other on something neither should be doing - a value dropped by both
    /// would survive the text comparison above and fail here.
    /// </summary>
    [Theory]
    [MemberData(nameof(Claims))]
    public void Re_importing_a_generated_interchange_reproduces_the_same_record(int index, int lineCount)
    {
        var interchange = Serializer.Serialize(GovernedClaimCorpus.Build(index, lineCount));

        var first = Parser.Parse(interchange);
        var second = Parser.Parse(Serializer.Serialize(first));

        Assert.Empty(ReversibilityVerifier.Differences(first, second));
    }

    /// <summary>
    /// The loop run twice more. A round trip that is stable on its first pass but drifts on a later
    /// one is a round trip that is not closed, and a claim in a real system is exported more than
    /// once.
    /// </summary>
    [Theory]
    [MemberData(nameof(Claims))]
    public void Round_trip_is_stable_under_repetition(int index, int lineCount)
    {
        var text = Serializer.Serialize(GovernedClaimCorpus.Build(index, lineCount));

        for (var pass = 0; pass < 3; pass++)
        {
            var next = Serializer.Serialize(Parser.Parse(text));

            Assert.Equal(text, next);
            text = next;
        }
    }

    [Theory]
    [MemberData(nameof(Claims))]
    public void Verifier_reports_a_stored_claim_as_unmutated(int index, int lineCount)
    {
        var verdict = Verifier.Verify(GovernedClaimCorpus.Build(index, lineCount));

        Assert.True(verdict.EdiTextIsIdentical, "The re-exported text differs from the exported text.");
        Assert.True(verdict.RecordIsIdentical, string.Join("; ", verdict.Differences));
        Assert.Empty(verdict.Differences);
    }

    /// <summary>
    /// The verifier must be able to fail. A reversibility check that cannot report a mutation is
    /// worth nothing, and one built by comparing a record against itself would pass forever. Each
    /// case below moves exactly one governed column and expects that column to be named.
    /// </summary>
    public static IEnumerable<object[]> SingleColumnMutations() =>
    [
        [nameof(Translator.Domain.Entities.ClaimHeader.Loop2010BB_NM103_PayerName)],
        [nameof(Translator.Domain.Entities.ClaimHeader.Loop2010BA_NM103_SubscriberLastName)],
        [nameof(Translator.Domain.Entities.ClaimHeader.Loop2010AA_NM109_BillingProviderNpi)],
        [nameof(Translator.Domain.Entities.ClaimHeader.CLM01_ClaimControlNumber)],
        [nameof(Translator.Domain.Entities.ClaimHeader.HI01_2_PrincipalDiagnosisCode)],
    ];

    [Theory]
    [MemberData(nameof(SingleColumnMutations))]
    public void Differences_names_the_governed_column_that_moved(string columnName)
    {
        var left = GovernedClaimCorpus.Build(1, 3);
        var right = GovernedClaimCorpus.Build(1, 3);

        var column = typeof(Translator.Domain.Entities.ClaimHeader).GetProperty(columnName)!;
        column.SetValue(right, columnName == nameof(Translator.Domain.Entities.ClaimHeader.HI01_2_PrincipalDiagnosisCode)
            ? "Z99.9"
            : "CHANGED");

        var differences = ReversibilityVerifier.Differences(left, right);

        Assert.NotEmpty(differences);
        Assert.Contains(differences, difference => difference.Contains(columnName, StringComparison.Ordinal));
    }

    /// <summary>
    /// A dropped or added service line is a mutation, and a comparison that only walked the header
    /// would miss it entirely. The line amounts are where the money is.
    /// </summary>
    [Fact]
    public void Differences_notices_a_service_line_that_was_added_or_removed()
    {
        var left = GovernedClaimCorpus.Build(1, 3);
        var shortened = GovernedClaimCorpus.Build(1, 3);
        shortened.LineItems.RemoveAt(0);

        Assert.NotEmpty(ReversibilityVerifier.Differences(left, shortened));
        Assert.NotEmpty(ReversibilityVerifier.Differences(shortened, left));
    }

    [Fact]
    public void Differences_notices_a_changed_line_amount()
    {
        var left = GovernedClaimCorpus.Build(1, 3);
        var right = GovernedClaimCorpus.Build(1, 3);
        right.LineItems[1].SV102_LineItemChargeAmount += 0.01m;

        var differences = ReversibilityVerifier.Differences(left, right);

        Assert.Contains(differences, difference =>
            difference.Contains(nameof(Translator.Domain.Entities.ClaimLineItem.SV102_LineItemChargeAmount),
                StringComparison.Ordinal));
    }

    /// <summary>
    /// PROVENANCE: ADR-016 - storage identity is deliberately not compared. It has no 837
    /// counterpart, so a reader cannot recover it, and a comparison that included it would report
    /// every correct round trip as a mutation.
    /// </summary>
    [Fact]
    public void Differences_ignores_storage_identity()
    {
        var left = GovernedClaimCorpus.Build(1, 3);
        var right = GovernedClaimCorpus.Build(1, 3);

        right.Id = Guid.NewGuid();
        foreach (var line in right.LineItems) line.Id = Guid.NewGuid();

        Assert.Empty(ReversibilityVerifier.Differences(left, right));
    }
}
