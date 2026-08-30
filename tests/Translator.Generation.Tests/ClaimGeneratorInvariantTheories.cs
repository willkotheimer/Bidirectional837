using System.Text.RegularExpressions;
using Translator.Domain.Validation;
using Translator.Generation;

namespace Translator.Generation.Tests;

/// <summary>
/// PROVENANCE: GOVERNANCE-5 - the acceptance criteria of Feature 1, User Stories 1.1 and 1.2,
/// expressed as invariants over every generated claim rather than as spot checks on one.
/// </summary>
public class ClaimGeneratorInvariantTheories
{
    /// <summary>HCPCS Level II: one letter followed by four digits. Governance caps SV101_2 at five characters.</summary>
    private static readonly Regex HcpcsPattern = new(@"^[A-Z]\d{4}$", RegexOptions.Compiled);

    /// <summary>Batch shapes spanning single claims, ordinary batches, and the governed ceiling.</summary>
    public static IEnumerable<object[]> BatchShapes() =>
    [
        [1, "OH", new[] { "Anesthesia" }],
        [3, "CA", new[] { "Cardiac" }],
        [10, "NY", new[] { "PhysicalTherapy" }],
        [25, "TX", new[] { "Anesthesia", "Cardiac" }],
        [50, "FL", new[] { "Anesthesia", "PhysicalTherapy", "Cardiac" }],
        [500, "WY", new[] { "Cardiac" }],
    ];

    public static IEnumerable<object[]> Jurisdictions() =>
        new[] { "OH", "CA", "NY", "TX", "FL", "WY", "AK", "HI", "ME", "DC" }
            .Select(state => new object[] { state });

    /// <summary>
    /// Governance User Story 1.2: "CLM02 total claim charge equals the sum of SV102 line item
    /// amounts." The single most consequential invariant in the generator: a claim whose header
    /// total disagrees with its lines is a claim no payer will adjudicate.
    /// </summary>
    [Theory]
    [MemberData(nameof(BatchShapes))]
    public async Task Claim_total_equals_the_sum_of_its_line_amounts(int billCount, string state, string[] categories)
    {
        var claims = await GenerateAsync(billCount, state, categories);

        Assert.All(claims, claim =>
            Assert.Equal(claim.LineItems.Sum(l => l.SV102_LineItemChargeAmount), claim.CLM02_TotalClaimChargeAmount));
    }

    [Theory]
    [MemberData(nameof(BatchShapes))]
    public async Task Generated_batch_holds_exactly_the_requested_number_of_claims(int billCount, string state, string[] categories)
    {
        var claims = await GenerateAsync(billCount, state, categories);

        Assert.Equal(billCount, claims.Count);
    }

    /// <summary>Governance User Story 1.2: procedure codes match valid HCPCS patterns.</summary>
    [Theory]
    [MemberData(nameof(BatchShapes))]
    public async Task Every_procedure_code_matches_the_hcpcs_pattern(int billCount, string state, string[] categories)
    {
        var claims = await GenerateAsync(billCount, state, categories);

        Assert.All(claims, claim =>
            Assert.All(claim.LineItems, line => Assert.Matches(HcpcsPattern, line.SV101_2_ProcedureCode)));
    }

    /// <summary>A code from outside the requested categories is a code the caller did not ask for.</summary>
    [Theory]
    [MemberData(nameof(BatchShapes))]
    public async Task Every_procedure_code_comes_from_a_requested_category(int billCount, string state, string[] categories)
    {
        var catalog = new SeedMedicalCodeCatalog();
        var permitted = categories.SelectMany(catalog.CodesIn).Select(code => code.Code).ToHashSet();

        var claims = await GenerateAsync(billCount, state, categories);

        Assert.All(claims, claim =>
            Assert.All(claim.LineItems, line => Assert.Contains(line.SV101_2_ProcedureCode, permitted)));
    }

    /// <summary>Governance User Story 1.1: the provider populated must carry a valid NPI.</summary>
    [Theory]
    [MemberData(nameof(BatchShapes))]
    public async Task Every_billing_provider_carries_a_valid_npi(int billCount, string state, string[] categories)
    {
        var claims = await GenerateAsync(billCount, state, categories);

        Assert.All(claims, claim =>
            Assert.True(NationalProviderIdentifier.IsValid(claim.Loop2010AA_NM109_BillingProviderNpi),
                $"Generated NPI {claim.Loop2010AA_NM109_BillingProviderNpi} fails the check digit."));
    }

    /// <summary>Governance User Story 1.1: the provider is drawn for the requested jurisdiction.</summary>
    [Theory]
    [MemberData(nameof(Jurisdictions))]
    public async Task Every_billing_provider_sits_in_the_requested_jurisdiction(string state)
    {
        var claims = await GenerateAsync(20, state, ["Cardiac"]);

        Assert.All(claims, claim => Assert.Equal(state, claim.Loop2010AA_N402_BillingProviderState));
    }

    /// <summary>Line numbers are the 837 LX loop's ordinal: sequential from 1, no gaps, no repeats.</summary>
    [Theory]
    [MemberData(nameof(BatchShapes))]
    public async Task Line_numbers_run_sequentially_from_one(int billCount, string state, string[] categories)
    {
        var claims = await GenerateAsync(billCount, state, categories);

        Assert.All(claims, claim =>
            Assert.Equal(
                Enumerable.Range(1, claim.LineItems.Count),
                claim.LineItems.Select(l => l.LX01_AssignedLineNumber).OrderBy(n => n)));
    }

    [Theory]
    [MemberData(nameof(BatchShapes))]
    public async Task Every_claim_carries_at_least_one_service_line(int billCount, string state, string[] categories)
    {
        var claims = await GenerateAsync(billCount, state, categories);

        Assert.All(claims, claim => Assert.NotEmpty(claim.LineItems));
    }

    /// <summary>
    /// Claim control numbers identify the claim to the payer. Two claims in one batch sharing one
    /// would be indistinguishable downstream.
    /// </summary>
    [Theory]
    [MemberData(nameof(BatchShapes))]
    public async Task Claim_control_numbers_are_unique_within_a_batch(int billCount, string state, string[] categories)
    {
        var claims = await GenerateAsync(billCount, state, categories);

        Assert.Equal(billCount, claims.Select(c => c.CLM01_ClaimControlNumber).Distinct().Count());
    }

    /// <summary>
    /// Charges are money: never negative, never zero, and never carrying more precision than the
    /// governed decimal(18,2) can hold.
    /// </summary>
    [Theory]
    [MemberData(nameof(BatchShapes))]
    public async Task Every_line_charge_is_positive_and_within_the_governed_scale(int billCount, string state, string[] categories)
    {
        var claims = await GenerateAsync(billCount, state, categories);

        Assert.All(claims, claim => Assert.All(claim.LineItems, line =>
        {
            Assert.True(line.SV102_LineItemChargeAmount > 0m, "A service line carries a non-positive charge.");
            Assert.Equal(line.SV102_LineItemChargeAmount, decimal.Round(line.SV102_LineItemChargeAmount, 2));
        }));
    }

    /// <summary>
    /// Governance section 4 requires reproducible runs. The same seed must yield the same batch,
    /// or a failing generated claim could never be investigated.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(42)]
    [InlineData(20260829)]
    public async Task Same_seed_yields_an_identical_batch(int seed)
    {
        var first = await GenerateAsync(15, "OH", ["Anesthesia", "Cardiac"], seed);
        var second = await GenerateAsync(15, "OH", ["Anesthesia", "Cardiac"], seed);

        Assert.Equal(
            first.Select(Fingerprint),
            second.Select(Fingerprint));
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(42, 43)]
    public async Task Different_seeds_yield_different_batches(int firstSeed, int secondSeed)
    {
        var first = await GenerateAsync(15, "OH", ["Anesthesia", "Cardiac"], firstSeed);
        var second = await GenerateAsync(15, "OH", ["Anesthesia", "Cardiac"], secondSeed);

        Assert.NotEqual(
            first.Select(Fingerprint).ToList(),
            second.Select(Fingerprint).ToList());
    }

    /// <summary>Governance section 2 dates are CCYYMMDD strings; a malformed one breaks the 837 DTP segment.</summary>
    [Theory]
    [MemberData(nameof(BatchShapes))]
    public async Task Every_governed_date_is_a_parseable_ccyymmdd_string(int billCount, string state, string[] categories)
    {
        var claims = await GenerateAsync(billCount, state, categories);

        Assert.All(claims, claim =>
        {
            AssertCcyymmdd(claim.Loop2010BA_DMG02_SubscriberDob);
            Assert.All(claim.LineItems, line => AssertCcyymmdd(line.DTP03_ServiceDate));
        });
    }

    private static void AssertCcyymmdd(string value)
    {
        Assert.Equal(8, value.Length);
        Assert.True(
            DateTime.TryParseExact(value, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out _),
            $"{value} is not a parseable CCYYMMDD date.");
    }

    private static string Fingerprint(Domain.Entities.ClaimHeader claim) =>
        string.Join('|',
            claim.CLM01_ClaimControlNumber,
            claim.Loop2010AA_NM109_BillingProviderNpi,
            claim.Loop2010BA_NM103_SubscriberLastName,
            claim.CLM02_TotalClaimChargeAmount,
            string.Join(',', claim.LineItems.Select(l => $"{l.SV101_2_ProcedureCode}:{l.SV102_LineItemChargeAmount}")));

    private static Task<IReadOnlyList<Domain.Entities.ClaimHeader>> GenerateAsync(
        int billCount, string state, string[] categories, int seed = 20260829)
    {
        var generator = new SyntheticClaimGenerator(
            new SyntheticProviderDirectory(),
            new SeedMedicalCodeCatalog(),
            new SeedChargeSchedule());

        return generator.GenerateAsync(new BatchGenerationRequest(billCount, state, categories, seed));
    }
}
