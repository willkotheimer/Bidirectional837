using System.Globalization;
using Translator.TestSupport;

namespace Translator.Edi.Tests;

/// <summary>
/// PROVENANCE: GOVERNANCE-1 - the two element renderings that stand between a stored column and
/// its 837 text, and the only two places in the writer where the stored form and the emitted form
/// are not the same characters.
///
/// Both are therefore held to the same standard: the conversion must be invertible, and the
/// inverse must be exact. A rendering that loses information is a Zero-Mutation violation that
/// nothing downstream can detect, because the file it produces is perfectly well formed.
/// </summary>
public class CanonicalElementTheories
{
    /// <summary>Governed monetary values, at the decimal(18,2) scale Section 2 declares.</summary>
    public static IEnumerable<object[]> MonetaryValues() =>
    [
        [0.00m], [0.01m], [1.00m], [1.50m], [9.99m], [125.00m], [125.25m],
        [1000.10m], [99999.99m], [-3.10m], [9999999999999999.99m],
    ];

    /// <summary>Governed quantity values, at the decimal(18,4) scale Section 2 declares.</summary>
    public static IEnumerable<object[]> QuantityValues() =>
    [
        [0.0000m], [0.0001m], [1.0000m], [1.2500m], [2.5000m], [10.0000m], [99.9999m],
    ];

    /// <summary>
    /// The 5010 guide asks that a numeric element carry no insignificant trailing zeros and no
    /// trailing decimal point. Scale is a property of the governed column, not of the text.
    /// </summary>
    public static IEnumerable<object[]> RenderedForms() =>
    [
        [0.00m, "0"],
        [1.0000m, "1"],
        [1.500m, "1.5"],
        [125.25m, "125.25"],
        [-3.100m, "-3.1"],
        [0.01m, "0.01"],
        [9999999999999999.99m, "9999999999999999.99"],
    ];

    [Theory]
    [MemberData(nameof(RenderedForms))]
    public void Rendering_suppresses_trailing_zeros_and_never_leaves_a_bare_decimal_point(
        decimal value, string expected)
    {
        Assert.Equal(expected, X12Number.Render(value));
    }

    /// <summary>
    /// The inverse restores the governed scale rather than whatever scale the text happened to
    /// carry. Trailing zeros are suppressed on the way out, so they must be reinstated on the way
    /// back in, or a charge of 1.00 would return as 1 and change the next file written from it.
    /// This is the same class of defect as FIND-002, at the EDI boundary rather than the store.
    /// </summary>
    [Theory]
    [MemberData(nameof(MonetaryValues))]
    public void Monetary_value_survives_rendering_and_reading_at_its_governed_scale(decimal value)
    {
        var recovered = X12Number.Parse(X12Number.Render(value), 2);

        Assert.Equal(value, recovered);
        Assert.Equal(Text(value), Text(recovered));
    }

    [Theory]
    [MemberData(nameof(QuantityValues))]
    public void Quantity_value_survives_rendering_and_reading_at_its_governed_scale(decimal value)
    {
        var recovered = X12Number.Parse(X12Number.Render(value), 4);

        Assert.Equal(value, recovered);
        Assert.Equal(Text(value), Text(recovered));
    }

    [Theory]
    [InlineData("1", 2, "1.00")]
    [InlineData("1.5", 4, "1.5000")]
    [InlineData("0", 2, "0.00")]
    [InlineData("125.25", 2, "125.25")]
    [InlineData("-3.1", 2, "-3.10")]
    public void Reading_an_element_restores_the_declared_scale(string element, int scale, string expected)
    {
        Assert.Equal(expected, Text(X12Number.Parse(element, scale)));
    }

    /// <summary>
    /// An element carrying more precision than the governed column can hold is refused rather
    /// than rounded. Silently rounding it would store a different amount than the file states,
    /// which is precisely the corruption FIND-001 recorded, arriving through a different door.
    /// </summary>
    [Theory]
    [InlineData("1.005", 2)]
    [InlineData("0.00001", 4)]
    public void Reading_an_element_finer_than_the_governed_scale_is_refused(string element, int scale)
    {
        Assert.Throws<FormatException>(() => X12Number.Parse(element, scale));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("abc")]
    [InlineData("1.2.3")]
    [InlineData("1,000.00")]
    public void Reading_an_element_that_is_not_a_number_is_refused(string element)
    {
        Assert.Throws<FormatException>(() => X12Number.Parse(element, 2));
    }

    /// <summary>ICD-10-CM codes as governance Section 2 stores them, with the decimal point.</summary>
    public static IEnumerable<object[]> StoredDiagnosisCodes() =>
    [
        ["A00"], ["I10"], ["E11.9"], ["M54.5"], ["J44.9"], ["S72.001A"], ["Z00.00"],
    ];

    [Theory]
    [InlineData("E11.9", "E119")]
    [InlineData("M54.5", "M545")]
    [InlineData("S72.001A", "S72001A")]
    [InlineData("I10", "I10")]
    [InlineData("A00", "A00")]
    public void Diagnosis_code_loses_its_decimal_point_on_the_way_into_the_HI_segment(
        string stored, string emitted)
    {
        Assert.Equal(emitted, Icd10Code.ToX12(stored));
    }

    /// <summary>
    /// The decimal point sits after the third character in every ICD-10-CM code, so restoring it
    /// is deterministic and the conversion is invertible.
    /// </summary>
    [Theory]
    [MemberData(nameof(StoredDiagnosisCodes))]
    public void Diagnosis_code_survives_the_round_trip_through_the_HI_segment(string stored)
    {
        Assert.Equal(stored, Icd10Code.FromX12(Icd10Code.ToX12(stored)));
    }

    /// <summary>
    /// PROVENANCE: FIND-011 - the guard that holds the mutation shut.
    ///
    /// A code stored without the decimal point its format requires cannot be converted safely:
    /// "E119" would be emitted unchanged and read back as "E11.9", so re-exporting it would
    /// produce a different file from the one it came from. The writer refuses it rather than
    /// silently mutating a diagnosis, which is a clinical field.
    /// </summary>
    [Theory]
    [InlineData("E119")]
    [InlineData("M545")]
    [InlineData("e11.9")]
    [InlineData("E11.")]
    [InlineData("11.9")]
    [InlineData("")]
    public void Diagnosis_code_that_is_not_in_the_governed_canonical_form_is_refused(string stored)
    {
        Assert.Throws<FormatException>(() => Icd10Code.ToX12(stored));
    }

    /// <summary>Every diagnosis the corpus carries is in canonical form and round-trips.</summary>
    [Theory]
    [MemberData(nameof(CorpusClaims))]
    public void Corpus_diagnoses_are_canonical_and_survive_the_round_trip(int index, int lineCount)
    {
        var stored = GovernedClaimCorpus.Build(index, lineCount).HI01_2_PrincipalDiagnosisCode;

        Assert.Equal(stored, Icd10Code.FromX12(Icd10Code.ToX12(stored)));
    }

    public static IEnumerable<object[]> CorpusClaims() => GovernedClaimCorpus.ClaimIndices();

    /// <summary>
    /// Decimal comparison alone would not catch a lost scale: 1.00m and 1m compare equal. The
    /// text is what the 837 carries, so the text is what these Theories compare.
    /// </summary>
    private static string Text(decimal value) => value.ToString(CultureInfo.InvariantCulture);
}
