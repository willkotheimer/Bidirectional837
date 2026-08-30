using System.Text.RegularExpressions;

namespace Governance.Generation.Tests;

/// <summary>
/// PROVENANCE: ADR-024 - the catalog is built backwards from the published CMS fee schedules, so
/// every catalogued code is real, current, billable and priced by construction.
///
/// These Theories hold the distilled result to the two properties that make that claim true: every
/// code is HCPCS Level II and nothing else, and every code carries a charge a claim can actually
/// bill. The first is the copyright boundary, checked by the build rather than remembered.
/// </summary>
public class CatalogIntegrityTheories
{
    private static readonly IMedicalCodeCatalog Catalog = new SeedMedicalCodeCatalog();
    private static readonly IChargeSchedule Charges = new SeedChargeSchedule();

    /// <summary>One letter then four digits. Nothing else is HCPCS Level II.</summary>
    private static readonly Regex LevelIi = new("^[A-CE-V][0-9]{4}$", RegexOptions.Compiled);

    public static IEnumerable<object[]> Categories() =>
        Catalog.Categories.Select(category => new object[] { category });

    /// <summary>Governance User Story 1.2 names these three as its examples, so they must exist.</summary>
    public static IEnumerable<object[]> GovernedCategories() =>
        new[] { "Anesthesia", "PhysicalTherapy", "Cardiac" }.Select(name => new object[] { name });

    [Theory]
    [MemberData(nameof(GovernedCategories))]
    public void Category_governance_names_carries_codes(string category)
    {
        Assert.Contains(category, Catalog.Categories);
        Assert.NotEmpty(Catalog.CodesIn(category));
    }

    /// <summary>
    /// The copyright boundary, asserted rather than trusted. A five-digit code is CPT and belongs to
    /// the AMA; a D code is CDT and belongs to the ADA. Neither may enter the catalog, and a build
    /// that lets one in should fail rather than a reviewer having to notice.
    /// </summary>
    [Theory]
    [MemberData(nameof(Categories))]
    public void Every_catalogued_code_is_hcpcs_level_two(string category)
    {
        foreach (var code in Catalog.CodesIn(category))
        {
            Assert.Matches(LevelIi, code.Code);
            Assert.DoesNotContain(code.Code, candidate => char.IsAsciiDigit(candidate) && code.Code[0] == 'D');
            Assert.False(code.Code.All(char.IsAsciiDigit),
                $"{code.Code} is all digits, which makes it a CPT code rather than HCPCS Level II.");
            Assert.False(code.Code.StartsWith('D'),
                $"{code.Code} is a D-series dental code, which is ADA copyright.");
        }
    }

    /// <summary>
    /// Every catalogued code is priced, and priced at something a claim can bill. A charge of zero
    /// would let a whole claim total zero, which satisfies the governed CLM02 sum invariant only
    /// vacuously - and the ingestion parser refuses exactly that file.
    /// </summary>
    [Theory]
    [MemberData(nameof(Categories))]
    public void Every_catalogued_code_carries_a_billable_charge(string category)
    {
        foreach (var code in Catalog.CodesIn(category))
        {
            var charge = Charges.ChargeFor(code.Code);

            Assert.True(charge > 0m, $"{code.Code} ({category}) is catalogued with a charge of {charge}.");
            Assert.Equal(decimal.Round(charge, 2), charge);
        }
    }

    /// <summary>
    /// PROVENANCE: FIND-019 - descriptions must arrive whole, not truncated at their first comma.
    ///
    /// The seed reader split on every comma and documented the assumption that the files carried no
    /// quoted fields. That was true of fifteen hand-written rows and false the moment the catalogue
    /// was distilled from CMS prose. Asserting a description is non-empty did not catch it, because
    /// a truncated description is not empty - so this asserts completeness instead.
    /// </summary>
    [Fact]
    public void Descriptions_survive_the_seed_reader_whole()
    {
        var all = Catalog.Categories.SelectMany(Catalog.CodesIn).ToList();

        foreach (var code in all)
        {
            Assert.False(code.Description.StartsWith('"'),
                $"{code.Code} begins with a stray quote, so its row was split inside a quoted field.");
            Assert.False(code.Description.EndsWith('"'), $"{code.Code} ends with a stray quote.");
        }

        // The guard is only meaningful if quoted fields are actually present to be mishandled.
        Assert.Contains(all, code => code.Description.Contains(',', StringComparison.Ordinal));
    }

    /// <summary>A code with no description is no use to the selector it feeds.</summary>
    [Theory]
    [MemberData(nameof(Categories))]
    public void Every_catalogued_code_carries_a_description(string category)
    {
        foreach (var code in Catalog.CodesIn(category))
        {
            Assert.False(string.IsNullOrWhiteSpace(code.Description), $"{code.Code} has no description.");
            Assert.Equal(category, code.Category);
        }
    }

    /// <summary>
    /// The catalog is meaningfully larger than the fifteen hand-curated codes it replaced. Stated as
    /// a floor rather than an exact count so a refreshed fee schedule does not fail the build, but
    /// a distillation that silently collapsed would.
    /// </summary>
    [Fact]
    public void Catalog_is_broad_enough_to_be_searchable()
    {
        var all = Catalog.Categories.SelectMany(Catalog.CodesIn).ToList();

        Assert.True(all.Count >= 500, $"The catalog holds {all.Count} codes; it held 980 when distilled.");
        Assert.True(Catalog.Categories.Count >= 5, $"The catalog holds only {Catalog.Categories.Count} categories.");
        Assert.Equal(all.Count, all.Select(code => code.Code).Distinct().Count());
    }

    /// <summary>
    /// A generated claim must be serialisable, and the writer refuses any value carrying an X12
    /// delimiter. A description reaches no 837 element, but a procedure code does.
    /// </summary>
    [Theory]
    [MemberData(nameof(Categories))]
    public void No_catalogued_code_carries_an_x12_delimiter(string category)
    {
        foreach (var code in Catalog.CodesIn(category))
        {
            Assert.DoesNotContain(code.Code, character => character is '*' or '~' or ':' or '^');
        }
    }
}
