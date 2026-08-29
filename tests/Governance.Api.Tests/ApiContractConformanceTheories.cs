using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Governance.Api.Tests;

/// <summary>
/// PROVENANCE: GOVERNANCE-4 - "API Contracts First: OpenAPI specifications (swagger.json) and C#
/// API controllers must be fully defined before building underlying service logic."
///
/// The authored contract in docs/api/swagger.json is committed ahead of the controllers. These
/// Theories assert that the running application exposes exactly that surface: every published
/// operation is routed, and the document the application serves declares every published path.
/// Whether the logic behind an operation exists yet is a separate question, settled in later
/// sections; an operation whose service is still outstanding answers 501, never 404.
/// </summary>
public class ApiContractConformanceTheories : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ApiContractConformanceTheories(WebApplicationFactory<Program> factory) => _factory = factory;

    public static IEnumerable<object[]> Operations() => PublishedContract.Operations();
    public static IEnumerable<object[]> Paths() => PublishedContract.Paths();

    /// <summary>Routes deliberately outside the published surface, including the project template's own.</summary>
    public static IEnumerable<object[]> UnpublishedRoutes() =>
    [
        ["/WeatherForecast"],
        ["/api/v1/bills"],
        ["/api/v1/claims/export"],
        ["/api/v2/claims"],
    ];

    [Theory]
    [MemberData(nameof(Operations))]
    public async Task Published_operation_is_routed_by_the_application(string method, string path)
    {
        var client = _factory.CreateClient();

        var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), Concretise(path)));

        Assert.NotEqual(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(Paths))]
    public async Task Served_openapi_document_declares_every_published_path(string path)
    {
        var client = _factory.CreateClient();

        var document = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json"));

        Assert.True(document.RootElement.GetProperty("paths").TryGetProperty(path, out _),
            $"The served OpenAPI document does not declare {path}, which docs/api/swagger.json publishes.");
    }

    [Theory]
    [MemberData(nameof(UnpublishedRoutes))]
    public async Task Route_outside_the_published_contract_is_not_served(string path)
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// The one assertion here that is global rather than per-case: the served document must not
    /// grow a path the authored contract does not publish. Expressed as a Fact because it is a
    /// statement about the surface as a whole, not an invariant instantiated per input.
    /// </summary>
    [Fact]
    public async Task Served_openapi_document_declares_no_path_the_contract_omits()
    {
        var client = _factory.CreateClient();
        var published = PublishedContract.PathSet();

        var document = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json"));
        var served = document.RootElement.GetProperty("paths").EnumerateObject().Select(p => p.Name).ToList();

        Assert.Empty(served.Where(path => !published.Contains(path)));
    }

    /// <summary>
    /// Governance User Story 1.3: "Requesting > 500 bills returns a 400 Bad Request."
    /// A permitted count must not be rejected; whether it is yet fulfilled is Feature 1's concern.
    /// </summary>
    [Theory]
    [InlineData(1, false)]
    [InlineData(250, false)]
    [InlineData(500, false)]
    [InlineData(501, true)]
    [InlineData(5000, true)]
    public async Task Batch_generation_rejects_a_request_above_the_governed_ceiling(int billCount, bool expectRejection)
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/bills/batch-generate", new
        {
            BillCount = billCount,
            JurisdictionState = "OH",
            MedicalCodeCategories = new[] { "Anesthesia" },
        });

        Assert.Equal(expectRejection, response.StatusCode == HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// PROVENANCE: ADR-005, ADR-010 - end-to-end proof that the compensating control actually
    /// fires. ContractValidationTheories proves the governed limits are declared where the
    /// framework reads them; this proves the deployed pipeline rejects a violation with the 400
    /// that governance requires, rather than passing it through to a store that cannot refuse it.
    /// </summary>
    [Theory]
    [InlineData(2, false)]
    [InlineData(3, true)]
    [InlineData(40, true)]
    public async Task Batch_generation_rejects_an_over_length_jurisdiction_state(int stateLength, bool expectRejection)
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/bills/batch-generate", new
        {
            BillCount = 10,
            JurisdictionState = new string('X', stateLength),
            MedicalCodeCategories = new[] { "Anesthesia" },
        });

        Assert.Equal(expectRejection, response.StatusCode == HttpStatusCode.BadRequest);
    }

    /// <summary>Substitutes a concrete value for any path template parameter.</summary>
    private static string Concretise(string path) =>
        path.Replace("{id}", Guid.Empty.ToString());
}
