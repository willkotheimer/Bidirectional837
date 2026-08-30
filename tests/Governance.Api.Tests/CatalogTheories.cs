using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Governance.Api.Tests;

/// <summary>
/// PROVENANCE: ADR-025 - the two read-only routes the generation form needs, so the client can be
/// built against the same catalogue the generator draws from rather than a copy that would drift.
///
/// The drift is the point. A hard-coded code list in the client would silently disagree with the
/// server's seed corpus the first time either changed, and the disagreement would show up as a
/// batch-generate 400 for a category the dropdown offered.
/// </summary>
public class CatalogTheories : IClassFixture<GovernedApiFactory>
{
    private readonly GovernedApiFactory _factory;

    public CatalogTheories(GovernedApiFactory factory) => _factory = factory;

    public static IEnumerable<object[]> GovernedCategories() =>
        new[] { "Anesthesia", "PhysicalTherapy", "Cardiac" }.Select(name => new object[] { name });

    [Fact]
    public async Task Codes_route_lists_the_whole_catalogue()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/codes");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var codes = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

        Assert.True(codes.GetArrayLength() >= 500, $"Only {codes.GetArrayLength()} codes were served.");

        foreach (var code in codes.EnumerateArray())
        {
            Assert.False(string.IsNullOrWhiteSpace(code.GetProperty("code").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(code.GetProperty("category").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(code.GetProperty("description").GetString()));
            Assert.True(code.GetProperty("standardCharge").GetDecimal() > 0m);
        }
    }

    [Theory]
    [MemberData(nameof(GovernedCategories))]
    public async Task Codes_route_filters_to_a_requested_category(string category)
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/v1/codes?category={category}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var codes = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

        Assert.True(codes.GetArrayLength() > 0, $"No codes were served for {category}.");
        Assert.All(codes.EnumerateArray().ToList(),
            code => Assert.Equal(category, code.GetProperty("category").GetString()));
    }

    /// <summary>
    /// An unknown category is a 404 rather than an empty list, because an empty list would let the
    /// client show a working-looking dropdown for a category the generator would then reject.
    /// </summary>
    [Theory]
    [InlineData("Dentistry")]
    [InlineData("NoSuchCategory")]
    public async Task Codes_route_refuses_a_category_the_catalogue_does_not_hold(string category)
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/v1/codes?category={category}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Jurisdictions_route_lists_every_state_a_provider_can_be_sourced_for()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/jurisdictions");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var jurisdictions = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

        Assert.True(jurisdictions.GetArrayLength() >= 50,
            $"Only {jurisdictions.GetArrayLength()} jurisdictions were served.");

        foreach (var jurisdiction in jurisdictions.EnumerateArray())
        {
            Assert.Equal(2, jurisdiction.GetProperty("code").GetString()!.Length);
            Assert.False(string.IsNullOrWhiteSpace(jurisdiction.GetProperty("name").GetString()));
            Assert.True(jurisdiction.GetProperty("providerCount").GetInt32() > 0);
        }
    }

    /// <summary>
    /// The whole reason both routes exist: every value they offer must be one the generation route
    /// accepts. A dropdown that can produce a 400 is a dropdown built against the wrong list.
    /// </summary>
    [Theory]
    [MemberData(nameof(GovernedCategories))]
    public async Task Every_offered_category_is_accepted_by_batch_generation(string category)
    {
        var client = _factory.CreateClient();

        var jurisdictions = JsonDocument.Parse(
            await client.GetStringAsync("/api/v1/jurisdictions")).RootElement;
        var state = jurisdictions[0].GetProperty("code").GetString();

        var response = await client.PostAsJsonAsync("/api/v1/bills/batch-generate", new
        {
            BillCount = 2,
            JurisdictionState = state,
            MedicalCodeCategories = new[] { category },
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    /// <summary>
    /// The charge a generated claim bills is the charge the catalogue advertised for that code.
    /// A client showing one number while the generator uses another would be worse than showing
    /// nothing.
    /// </summary>
    [Fact]
    public async Task Generated_line_amounts_match_the_advertised_standard_charge()
    {
        var client = _factory.CreateClient();

        var advertised = JsonDocument.Parse(await client.GetStringAsync("/api/v1/codes")).RootElement
            .EnumerateArray()
            .ToDictionary(
                code => code.GetProperty("code").GetString()!,
                code => code.GetProperty("standardCharge").GetDecimal());

        var response = await client.PostAsJsonAsync("/api/v1/bills/batch-generate", new
        {
            BillCount = 20,
            JurisdictionState = "OH",
            MedicalCodeCategories = new[] { "Cardiac", "Anesthesia" },
        });

        var claims = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

        foreach (var claim in claims.EnumerateArray())
        {
            foreach (var line in claim.GetProperty("lineItems").EnumerateArray())
            {
                var code = line.GetProperty("sV101_2_ProcedureCode").GetString()!;
                var units = line.GetProperty("sV104_ServiceUnitCount").GetDecimal();
                var amount = line.GetProperty("sV102_LineItemChargeAmount").GetDecimal();

                Assert.True(advertised.ContainsKey(code), $"{code} was billed but is not catalogued.");
                Assert.Equal(advertised[code] * units, amount);
            }
        }
    }
}
