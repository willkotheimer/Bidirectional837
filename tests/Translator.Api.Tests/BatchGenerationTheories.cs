using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Translator.Api.Tests;

/// <summary>
/// PROVENANCE: GOVERNANCE-5 - governance Feature 1, User Story 1.3: "As an API Client, I want to
/// request up to 500 synthetic claims via POST /api/v1/bills/batch-generate."
/// </summary>
public class BatchGenerationTheories : IClassFixture<GovernedApiFactory>
{
    private readonly GovernedApiFactory _factory;

    public BatchGenerationTheories(GovernedApiFactory factory) => _factory = factory;

    public static IEnumerable<object[]> PermittedBatchSizes() =>
    [
        [1, "OH", new[] { "Anesthesia" }],
        [5, "CA", new[] { "Cardiac" }],
        [50, "NY", new[] { "PhysicalTherapy" }],
        [200, "TX", new[] { "Anesthesia", "Cardiac" }],
        [500, "FL", new[] { "Anesthesia", "PhysicalTherapy", "Cardiac" }],
    ];

    [Theory]
    [MemberData(nameof(PermittedBatchSizes))]
    public async Task Permitted_batch_is_created_and_returns_the_requested_number_of_claims(
        int billCount, string state, string[] categories)
    {
        var response = await PostBatchAsync(billCount, state, categories);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var claims = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(billCount, claims.GetArrayLength());
    }

    /// <summary>
    /// Governance User Story 1.2 restated at the API boundary: the header total a client receives
    /// must equal the sum of the line amounts it received alongside it.
    /// </summary>
    [Theory]
    [MemberData(nameof(PermittedBatchSizes))]
    public async Task Returned_claim_totals_equal_the_sum_of_their_returned_line_amounts(
        int billCount, string state, string[] categories)
    {
        var response = await PostBatchAsync(billCount, state, categories);
        var claims = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

        foreach (var claim in claims.EnumerateArray())
        {
            var lineSum = claim.GetProperty("lineItems").EnumerateArray()
                .Sum(line => line.GetProperty("sV102_LineItemChargeAmount").GetDecimal());

            Assert.Equal(lineSum, claim.GetProperty("clM02_TotalClaimChargeAmount").GetDecimal());
        }
    }

    /// <summary>
    /// PROVENANCE: FIND-010 - governance User Story 1.3 acceptance criterion: "Generation of 500
    /// bills finishes in under 3.0 seconds." Measured end to end over HTTP, including persistence
    /// and serialisation, which is the stricter reading: generation alone is around 0.011s, so the
    /// governed requirement passes by roughly 280x while this assertion passes by about a third. A warm-up request precedes the measurement so that first-request JIT and DI
    /// container construction are not charged against the governed budget.
    /// </summary>
    [Theory]
    [InlineData(500, 3.0)]
    public async Task Governed_batch_size_completes_within_its_time_budget(int billCount, double budgetSeconds)
    {
        await PostBatchAsync(1, "OH", ["Cardiac"]);

        var stopwatch = Stopwatch.StartNew();
        var response = await PostBatchAsync(billCount, "OH", ["Anesthesia", "PhysicalTherapy", "Cardiac"]);
        stopwatch.Stop();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.True(stopwatch.Elapsed.TotalSeconds < budgetSeconds,
            $"Generating {billCount} bills took {stopwatch.Elapsed.TotalSeconds:F2}s, " +
            $"over the governed budget of {budgetSeconds:F1}s.");
    }

    /// <summary>Generated claims are held in the store, so the dashboard and export can read them.</summary>
    [Theory]
    [InlineData(3, "OH", "Cardiac")]
    [InlineData(7, "CA", "Anesthesia")]
    public async Task Generated_claims_are_retrievable_afterwards(int billCount, string state, string category)
    {
        var created = await PostBatchAsync(billCount, state, [category]);
        var claims = JsonDocument.Parse(await created.Content.ReadAsStringAsync()).RootElement;
        var firstId = claims[0].GetProperty("id").GetString();

        var fetched = await _factory.CreateClient().GetAsync($"/api/v1/claims/{firstId}");

        Assert.Equal(HttpStatusCode.OK, fetched.StatusCode);
        var claim = JsonDocument.Parse(await fetched.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(firstId, claim.GetProperty("id").GetString());
    }

    [Theory]
    [InlineData("ZZ")]
    [InlineData("OH")]
    public async Task Unknown_category_yields_a_bad_request_rather_than_an_empty_claim(string state)
    {
        var response = await PostBatchAsync(5, state, ["Oncology"]);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private Task<HttpResponseMessage> PostBatchAsync(int billCount, string state, string[] categories) =>
        _factory.CreateClient().PostAsJsonAsync("/api/v1/bills/batch-generate", new
        {
            BillCount = billCount,
            JurisdictionState = state,
            MedicalCodeCategories = categories,
        });
}
