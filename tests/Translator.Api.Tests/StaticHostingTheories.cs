using System.Net;

namespace Translator.Api.Tests;

/// <summary>
/// PROVENANCE: ADR-032 - the API serves the client from its own origin, which is what makes
/// ADR-028's reasoning true as written: a deployed instance needs no cross-origin grant because
/// there is no cross-origin call.
///
/// Two properties matter and they pull against each other. Any path the client owns must return the
/// application shell, because a single-page client routes in the browser and a deep link must not
/// 404. And no path the API owns may do that, because a published route answering with HTML instead
/// of a problem document is a client that cannot tell a failure from a page.
/// </summary>
public class StaticHostingTheories : IClassFixture<GovernedApiFactory>
{
    private readonly GovernedApiFactory _factory;

    public StaticHostingTheories(GovernedApiFactory factory) => _factory = factory;

    /// <summary>Paths the browser owns: the root, and anything the client might route to.</summary>
    public static IEnumerable<object[]> ClientPaths() =>
        new[] { "/", "/index.html" }.Select(path => new object[] { path });

    [Theory]
    [MemberData(nameof(ClientPaths))]
    public async Task Client_path_is_served_the_application_shell(string path)
    {
        var response = await _factory.CreateClient().GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
    }

    /// <summary>
    /// The fallback must not reach the API. An unknown route under /api is a mistake worth
    /// reporting, and answering it with the client's HTML would turn every typo into a page that
    /// looks like it worked.
    /// </summary>
    [Theory]
    [InlineData("/api/v1/nonexistent")]
    [InlineData("/api/v1/claims/not-a-guid")]
    [InlineData("/api/v2/claims")]
    public async Task Unknown_api_path_is_not_answered_with_the_client(string path)
    {
        var response = await _factory.CreateClient().GetAsync(path);

        Assert.NotEqual("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>Serving the client must not have displaced anything the contract publishes.</summary>
    [Theory]
    [InlineData("/api/v1/codes")]
    [InlineData("/api/v1/jurisdictions")]
    [InlineData("/api/v1/claims")]
    public async Task Published_route_still_answers_with_its_own_data(string path)
    {
        var response = await _factory.CreateClient().GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    }

    /// <summary>The OpenAPI document is part of the governed deliverable and is still served.</summary>
    [Fact]
    public async Task Openapi_document_is_still_served()
    {
        var response = await _factory.CreateClient().GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
