using System.Net;
using System.Web;

namespace Translator.Generation.Tests;

/// <summary>
/// PROVENANCE: FIND-017 - the registry query the application actually builds, measured against what
/// the registry actually requires.
///
/// Every existing Theory over this client answers a stubbed transport, and the stub was written to
/// return what we expected the registry to return. That proves the client can read a well-formed
/// answer; it proves nothing about whether the registry would ever give one to the question we ask.
/// It would not: the live API refuses a query carrying only `state` and `enumeration_type`, and
/// refuses it with HTTP 200 and an error body, so `EnsureSuccessStatusCode` is silent about it.
///
/// The Theories below therefore measure two things a stub cannot fake: the shape of the request we
/// send, and our handling of the exact rejection the live service returns.
/// </summary>
public class RegistryQueryTheories
{
    /// <summary>
    /// The rejection the live registry returns, verbatim, for
    /// `?version=2.1&amp;state=OH&amp;enumeration_type=NPI-2&amp;limit=20`. Captured 2026-08-30.
    /// Note the status is 200: the registry reports a bad query in the body, not in the status.
    /// </summary>
    private const string LiveRejection = """
    {
      "Errors": [
        { "description": "Field state requires additional search criteria", "field": "state", "number": "07" },
        { "description": "Enumeration_type requires additional search criteria", "field": "enumeration_type", "number": "09" }
      ]
    }
    """;

    /// <summary>
    /// The criteria the registry accepts as a companion to `state`. A query carrying `state` and
    /// nothing else from this list is refused, whatever else it carries.
    /// </summary>
    private static readonly string[] AcceptedCompanions =
        ["postal_code", "city", "taxonomy_description", "organization_name", "first_name", "last_name", "number"];

    public static IEnumerable<object[]> Jurisdictions() =>
        new[] { "OH", "CA", "NY", "TX", "FL", "WY" }.Select(state => new object[] { state });

    /// <summary>
    /// The defect itself. `state` alone is not a query the registry will answer, so a client that
    /// sends one can never retrieve the "valid NPI, Provider Name, and Physical Address" governance
    /// User Story 1.1 requires - it can only ever fall back.
    /// </summary>
    [Theory]
    [MemberData(nameof(Jurisdictions))]
    public async Task Query_carries_a_second_search_criterion_beside_state(string state)
    {
        var transport = new ProviderDirectoryTheories.StubTransport(HttpStatusCode.OK, LiveRejection);
        var directory = Directory(transport);

        try
        {
            await directory.ProviderForAsync(state, 0);
        }
        catch (Exception)
        {
            // The rejection is expected here; the request it produced is what is under test.
        }

        Assert.NotNull(transport.LastRequestUri);

        var query = HttpUtility.ParseQueryString(transport.LastRequestUri!.Query);
        var criteria = query.AllKeys.Where(key => key is not null).Select(key => key!).ToList();

        Assert.Contains("state", criteria);
        Assert.True(
            criteria.Any(AcceptedCompanions.Contains),
            $"The query for {state} carries only [{string.Join(", ", criteria)}]. The registry refuses " +
            $"a `state` query with no companion criterion; one of [{string.Join(", ", AcceptedCompanions)}] " +
            "is required, or the live path can never return a provider.");
    }

    /// <summary>The companion criterion must actually be filled in, not merely present and empty.</summary>
    [Theory]
    [MemberData(nameof(Jurisdictions))]
    public async Task Second_search_criterion_carries_a_value(string state)
    {
        var transport = new ProviderDirectoryTheories.StubTransport(HttpStatusCode.OK, LiveRejection);

        try { await Directory(transport).ProviderForAsync(state, 0); } catch (Exception) { }

        var query = HttpUtility.ParseQueryString(transport.LastRequestUri!.Query);

        var companion = query.AllKeys.FirstOrDefault(key => key is not null && AcceptedCompanions.Contains(key));

        Assert.NotNull(companion);
        Assert.False(string.IsNullOrWhiteSpace(query[companion]),
            $"The query for {state} sends `{companion}` with no value.");
    }

    /// <summary>
    /// The requested jurisdiction still governs the query. A companion criterion that replaced the
    /// state rather than narrowing within it would return providers from anywhere.
    /// </summary>
    [Theory]
    [MemberData(nameof(Jurisdictions))]
    public async Task Requested_jurisdiction_is_the_state_sent_to_the_registry(string state)
    {
        var transport = new ProviderDirectoryTheories.StubTransport(HttpStatusCode.OK, LiveRejection);

        try { await Directory(transport).ProviderForAsync(state, 0); } catch (Exception) { }

        var query = HttpUtility.ParseQueryString(transport.LastRequestUri!.Query);

        Assert.Equal(state, query["state"]);
    }

    /// <summary>
    /// A rejection body must not be read as an empty result set. Both end in a fallback today, so
    /// the outcome is the same; the message is not, and the message is the only thing that tells an
    /// operator the live path is misconfigured rather than merely unlucky.
    /// </summary>
    [Fact]
    public async Task Registry_rejection_is_reported_as_a_rejection_not_as_an_empty_result()
    {
        var directory = Directory(new ProviderDirectoryTheories.StubTransport(HttpStatusCode.OK, LiveRejection));

        var failure = await Assert.ThrowsAnyAsync<Exception>(() => directory.ProviderForAsync("OH", 0));

        Assert.Contains("requires additional search criteria", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Governance User Story 1.1 requires graceful fallback when the registry cannot supply a
    /// provider. A refused query is one of those states, and until now it was not covered: every
    /// fallback case in the existing suite is a transport failure or an empty result.
    /// </summary>
    [Theory]
    [MemberData(nameof(Jurisdictions))]
    public async Task Refused_query_falls_back_to_the_mock_provider_set(string state)
    {
        var resilient = new ResilientProviderDirectory(
            Directory(new ProviderDirectoryTheories.StubTransport(HttpStatusCode.OK, LiveRejection)),
            new SyntheticProviderDirectory());

        var provider = await resilient.ProviderForAsync(state, 0);

        Assert.Equal(state, provider.State);
        Assert.NotEmpty(provider.OrganisationOrLastName);
    }

    private static NpiRegistryProviderDirectory Directory(ProviderDirectoryTheories.StubTransport transport) =>
        new(new HttpClient(transport) { BaseAddress = new Uri(NpiRegistryProviderDirectory.DefaultBaseAddress) });
}
