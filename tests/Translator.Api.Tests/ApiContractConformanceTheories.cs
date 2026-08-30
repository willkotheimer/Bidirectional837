using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Translator.Api.Tests;

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
public class ApiContractConformanceTheories : IClassFixture<GovernedApiFactory>
{
    private readonly GovernedApiFactory _factory;

    public ApiContractConformanceTheories(GovernedApiFactory factory) => _factory = factory;

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

    /// <summary>
    /// PROVENANCE: FIND-009 - routing is read from the application's endpoint table rather than
    /// inferred from a status code. Probing over HTTP conflated "this route does not exist" with
    /// "this resource does not exist", so implementing a genuine 404 broke a test that was only
    /// ever asking whether the route was published.
    /// </summary>
    [Theory]
    [MemberData(nameof(Operations))]
    public void Published_operation_is_routed_by_the_application(string method, string path)
    {
        var routed = RoutedOperations(_factory.Services);

        Assert.Contains((method, path), routed);
    }

    /// <summary>Every (method, path) the application actually routes, normalised to contract form.</summary>
    private static HashSet<(string Method, string Path)> RoutedOperations(IServiceProvider services)
    {
        var endpoints = services.GetRequiredService<EndpointDataSource>().Endpoints;
        var routed = new HashSet<(string, string)>();

        foreach (var endpoint in endpoints.OfType<RouteEndpoint>())
        {
            var pattern = endpoint.RoutePattern.RawText;
            if (pattern is null) continue;

            // "api/v1/claims/{id:guid}" and "/api/v1/claims/{id}" describe the same published path.
            var normalised = "/" + Regex.Replace(pattern, @"\{(\w+)(:[^}]+)?\}", "{$1}").TrimStart('/');

            foreach (var verb in endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [])
            {
                routed.Add((verb.ToUpperInvariant(), normalised));
            }
        }

        return routed;
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
    /// PROVENANCE: FIND-016 - the published contract declares application/problem+json for every
    /// error response, and nothing asserted that the application served it. A class-level
    /// [Produces("application/json")] had been overriding it since the contract was published: the
    /// status codes were right, the bodies were right, and the media type told a client the
    /// document was something other than what it was.
    /// </summary>
    [Theory]
    [MemberData(nameof(ErrorResponses))]
    public async Task Error_response_is_served_as_the_problem_document_the_contract_declares(
        string method, string path, int expectedStatus)
    {
        var client = _factory.CreateClient();

        var response = method == "GET"
            ? await client.GetAsync(path)
            : await client.PostAsync(path, BodyFor(path));

        Assert.Equal(expectedStatus, (int)response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    /// <summary>One error response for each way the published contract says a request can fail.</summary>
    public static IEnumerable<object[]> ErrorResponses() =>
    [
        ["GET", $"/api/v1/claims/{Guid.NewGuid()}", 404],
        ["POST", $"/api/v1/claims/{Guid.NewGuid()}/verify-reversibility", 404],
        ["POST", "/api/v1/bills/batch-generate", 400],
    ];

    private static HttpContent? BodyFor(string path) =>
        path.EndsWith("batch-generate", StringComparison.Ordinal)
            ? JsonContent.Create(new { BillCount = 5000, JurisdictionState = "OH", MedicalCodeCategories = new[] { "Cardiac" } })
            : null;

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
    /// PROVENANCE: ADR-005, ADR-010, FIND-003, FIND-004 - end-to-end proof that the control actually
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

}
