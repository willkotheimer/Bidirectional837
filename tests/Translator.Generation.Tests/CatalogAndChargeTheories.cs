using System.Text.RegularExpressions;
using Translator.Generation;

namespace Translator.Generation.Tests;

/// <summary>
/// PROVENANCE: GOVERNANCE-5, ADR-013 - governance User Story 1.2: valid medical codes drawn from
/// selected categories, carrying published standard charges or deterministic fallback charges.
/// </summary>
public class CatalogAndChargeTheories
{
    private static readonly Regex HcpcsPattern = new(@"^[A-Z]\d{4}$", RegexOptions.Compiled);

    public static IEnumerable<object[]> GovernedCategories() =>
    [
        ["Anesthesia"],
        ["PhysicalTherapy"],
        ["Cardiac"],
    ];

    [Theory]
    [MemberData(nameof(GovernedCategories))]
    public void Catalog_publishes_every_governed_category(string category)
        => Assert.Contains(category, new SeedMedicalCodeCatalog().Categories);

    [Theory]
    [MemberData(nameof(GovernedCategories))]
    public void Category_holds_codes(string category)
        => Assert.NotEmpty(new SeedMedicalCodeCatalog().CodesIn(category));

    /// <summary>
    /// Codes must match the HCPCS Level II shape and fit the governed SV101_2 length of five.
    /// CPT is proprietary and deliberately excluded (ADR-013).
    /// </summary>
    [Theory]
    [MemberData(nameof(GovernedCategories))]
    public void Every_code_in_a_category_is_a_well_formed_hcpcs_code(string category)
    {
        var codes = new SeedMedicalCodeCatalog().CodesIn(category);

        Assert.All(codes, code =>
        {
            Assert.Matches(HcpcsPattern, code.Code);
            Assert.True(code.Code.Length <= 5, $"{code.Code} exceeds the governed SV101_2 length of 5.");
            Assert.Equal(category, code.Category);
            Assert.NotEmpty(code.Description);
        });
    }

    [Theory]
    [MemberData(nameof(GovernedCategories))]
    public void Codes_are_unique_within_a_category(string category)
    {
        var codes = new SeedMedicalCodeCatalog().CodesIn(category).Select(c => c.Code).ToList();

        Assert.Equal(codes.Count, codes.Distinct().Count());
    }

    [Theory]
    [InlineData("Oncology")]
    [InlineData("")]
    [InlineData("anesthesia ")]
    public void Unknown_category_yields_no_codes(string category)
        => Assert.Empty(new SeedMedicalCodeCatalog().CodesIn(category));

    /// <summary>Every catalogued code must be priced, or a generated claim would carry no charge.</summary>
    [Theory]
    [MemberData(nameof(GovernedCategories))]
    public void Every_catalogued_code_carries_a_positive_charge(string category)
    {
        var schedule = new SeedChargeSchedule();

        Assert.All(new SeedMedicalCodeCatalog().CodesIn(category), code =>
        {
            var charge = schedule.ChargeFor(code.Code);
            Assert.True(charge > 0m, $"{code.Code} carries a non-positive charge of {charge}.");
            Assert.Equal(charge, decimal.Round(charge, 2));
        });
    }

    /// <summary>
    /// Governance permits a deterministic fallback charge. An uncatalogued code must therefore
    /// still price, and price the same way every time, rather than throwing or returning zero.
    /// </summary>
    [Theory]
    [InlineData("A0425")]
    [InlineData("J0120")]
    [InlineData("Z9999")]
    [InlineData("Q0001")]
    public void Uncatalogued_code_receives_a_deterministic_fallback_charge(string procedureCode)
    {
        var schedule = new SeedChargeSchedule();

        var first = schedule.ChargeFor(procedureCode);
        var second = schedule.ChargeFor(procedureCode);

        Assert.Equal(first, second);
        Assert.True(first > 0m, $"{procedureCode} received a non-positive fallback charge of {first}.");
        Assert.Equal(first, decimal.Round(first, 2));
    }

    [Theory]
    [InlineData("G0403", "G0404")]
    [InlineData("J0670", "E0616")]
    public void Different_codes_are_priced_differently(string first, string second)
    {
        var schedule = new SeedChargeSchedule();

        Assert.NotEqual(schedule.ChargeFor(first), schedule.ChargeFor(second));
    }
}
