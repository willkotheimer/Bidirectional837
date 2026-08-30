using System.Net;
using System.Text;
using Translator.Domain.Validation;
using Translator.Generation;

namespace Translator.Generation.Tests;

/// <summary>
/// PROVENANCE: GOVERNANCE-5, ADR-012 - governance User Story 1.1: query the open-source NPI
/// registry using the requested jurisdiction state, and "if API is unreachable, fall back
/// gracefully to a mock state-compliant provider set."
///
/// The registry is exercised against a stubbed transport rather than the live service, so the suite
/// is deterministic and runs without a network. The live path is covered by an opt-in test.
/// </summary>
public class ProviderDirectoryTheories
{
    private const string RegistryResponse = """
    {
      "result_count": 1,
      "results": [
        {
          "number": "1245319599",
          "basic": { "organization_name": "RIVERSIDE CARDIOLOGY GROUP" },
          "addresses": [
            {
              "address_purpose": "LOCATION",
              "address_1": "820 RIVERSIDE PARKWAY",
              "city": "COLUMBUS",
              "state": "OH",
              "postal_code": "432154407"
            }
          ]
        }
      ]
    }
    """;

    public static IEnumerable<object[]> Jurisdictions() =>
        new[] { "OH", "CA", "NY", "TX", "FL", "WY", "AK", "HI", "ME", "DC" }
            .Select(state => new object[] { state });

    /// <summary>Every way the registry can let us down. Each must produce a usable provider, not an exception.</summary>
    public static IEnumerable<object[]> RegistryFailures() =>
    [
        [new StubTransport(HttpStatusCode.ServiceUnavailable)],
        [new StubTransport(HttpStatusCode.InternalServerError)],
        [new StubTransport(HttpStatusCode.NotFound)],
        [new StubTransport(HttpStatusCode.OK, """{ "result_count": 0, "results": [] }""")],
        [new StubTransport(HttpStatusCode.OK, "this is not json")],
        [new StubTransport(new HttpRequestException("No such host is known."))],
        [new StubTransport(new TaskCanceledException("The request timed out."))],
    ];

    [Theory]
    [MemberData(nameof(Jurisdictions))]
    public async Task Fallback_directory_yields_a_provider_for_every_jurisdiction(string state)
    {
        var provider = await new SyntheticProviderDirectory().ProviderForAsync(state, selector: 0);

        Assert.Equal(state, provider.State);
        Assert.True(NationalProviderIdentifier.IsValid(provider.Npi),
            $"Fallback NPI {provider.Npi} fails the check digit.");
        Assert.NotEmpty(provider.OrganisationOrLastName);
        Assert.NotEmpty(provider.AddressLine);
        Assert.NotEmpty(provider.City);
        Assert.NotEmpty(provider.ZipCode);
    }

    /// <summary>
    /// The fallback set must be state-compliant for any state, not only for the handful a fixed
    /// list happens to contain. A generator that cannot serve Wyoming is not a graceful fallback.
    /// </summary>
    [Theory]
    [MemberData(nameof(Jurisdictions))]
    public async Task Fallback_directory_is_deterministic_for_a_given_state_and_selector(string state)
    {
        var first = await new SyntheticProviderDirectory().ProviderForAsync(state, selector: 7);
        var second = await new SyntheticProviderDirectory().ProviderForAsync(state, selector: 7);

        Assert.Equal(first, second);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 2)]
    [InlineData(5, 11)]
    public async Task Fallback_directory_varies_provider_by_selector(int firstSelector, int secondSelector)
    {
        var directory = new SyntheticProviderDirectory();

        var first = await directory.ProviderForAsync("OH", firstSelector);
        var second = await directory.ProviderForAsync("OH", secondSelector);

        Assert.NotEqual(first.Npi, second.Npi);
    }

    [Theory]
    [MemberData(nameof(Jurisdictions))]
    public async Task Registry_directory_maps_a_successful_response_onto_the_governed_fields(string state)
    {
        var transport = new StubTransport(HttpStatusCode.OK, RegistryResponse);
        var directory = new NpiRegistryProviderDirectory(new HttpClient(transport) { BaseAddress = new Uri("https://npiregistry.cms.hhs.gov/") });

        var provider = await directory.ProviderForAsync(state, selector: 0);

        Assert.Equal("1245319599", provider.Npi);
        Assert.Equal("RIVERSIDE CARDIOLOGY GROUP", provider.OrganisationOrLastName);
        Assert.Equal("820 RIVERSIDE PARKWAY", provider.AddressLine);
        Assert.Equal("COLUMBUS", provider.City);
        Assert.Contains(state, transport.LastRequestUri!.Query);
    }

    /// <summary>
    /// Governance User Story 1.1 acceptance criterion: an unreachable API must degrade gracefully.
    /// Every failure mode is covered, because "unreachable" in practice means a timeout, a 503, a
    /// DNS failure, an empty result set, or a body that is not the JSON we expected.
    /// </summary>
    [Theory]
    [MemberData(nameof(RegistryFailures))]
    public async Task Unreachable_or_unusable_registry_falls_back_to_the_mock_provider_set(StubTransport transport)
    {
        var directory = new ResilientProviderDirectory(
            new NpiRegistryProviderDirectory(new HttpClient(transport) { BaseAddress = new Uri("https://npiregistry.cms.hhs.gov/") }),
            new SyntheticProviderDirectory());

        var provider = await directory.ProviderForAsync("OH", selector: 0);

        Assert.Equal("OH", provider.State);
        Assert.True(NationalProviderIdentifier.IsValid(provider.Npi));
    }

    [Fact]
    public async Task Reachable_registry_is_preferred_over_the_fallback()
    {
        var directory = new ResilientProviderDirectory(
            new NpiRegistryProviderDirectory(new HttpClient(new StubTransport(HttpStatusCode.OK, RegistryResponse))
            { BaseAddress = new Uri("https://npiregistry.cms.hhs.gov/") }),
            new SyntheticProviderDirectory());

        var provider = await directory.ProviderForAsync("OH", selector: 0);

        Assert.Equal("1245319599", provider.Npi);
    }

    /// <summary>
    /// A transport that answers with a canned response, a status code, or a thrown exception.
    /// Implements IXunitSerializable so it can carry Theory data.
    /// </summary>
    public sealed class StubTransport : HttpMessageHandler, Xunit.Abstractions.IXunitSerializable
    {
        private HttpStatusCode _status;
        private string _body;
        private Exception? _throws;

        public StubTransport() : this(HttpStatusCode.OK, "{}") { }

        public StubTransport(HttpStatusCode status, string body = "{}")
        {
            _status = status;
            _body = body;
        }

        public StubTransport(Exception throws)
        {
            _status = HttpStatusCode.OK;
            _body = "{}";
            _throws = throws;
        }

        public Uri? LastRequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri;

            if (_throws is not null) throw _throws;

            return Task.FromResult(new HttpResponseMessage(_status)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json"),
            });
        }

        public void Deserialize(Xunit.Abstractions.IXunitSerializationInfo info)
        {
            _status = (HttpStatusCode)info.GetValue<int>("status");
            _body = info.GetValue<string>("body");
            var throwsKind = info.GetValue<string>("throws");
            _throws = throwsKind switch
            {
                "http" => new HttpRequestException("No such host is known."),
                "timeout" => new TaskCanceledException("The request timed out."),
                _ => null,
            };
        }

        public void Serialize(Xunit.Abstractions.IXunitSerializationInfo info)
        {
            info.AddValue("status", (int)_status);
            info.AddValue("body", _body);
            info.AddValue("throws", _throws switch
            {
                HttpRequestException => "http",
                TaskCanceledException => "timeout",
                _ => "none",
            });
        }

        public override string ToString() =>
            _throws is not null ? _throws.GetType().Name : $"{(int)_status} {_body[..Math.Min(20, _body.Length)]}";
    }
}
