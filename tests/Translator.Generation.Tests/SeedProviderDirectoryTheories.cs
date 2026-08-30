using System.Diagnostics;
using System.Net;
using Translator.Domain.Validation;

namespace Translator.Generation.Tests;

/// <summary>
/// PROVENANCE: ADR-023 - the distilled NPPES snapshot that serves governance User Story 1.1
/// without a network call.
///
/// The snapshot is real registry data, so these Theories hold it to the same standard the live path
/// is held to: a check-digit-valid NPI, an address in the requested jurisdiction, and every value
/// inside the governed Section 2 column it feeds. A snapshot that needed truncating to fit
/// Loop2010AA would be a provider whose name is not that provider's name.
/// </summary>
public class SeedProviderDirectoryTheories
{
    private static readonly SeedProviderDirectory Directory = new();

    /// <summary>The 50 states, DC and Puerto Rico, which is what the distillation targets.</summary>
    public static IEnumerable<object[]> Jurisdictions() =>
        new[]
        {
            "AL", "AK", "AZ", "AR", "CA", "CO", "CT", "DE", "DC", "FL", "GA", "HI", "ID", "IL", "IN",
            "IA", "KS", "KY", "LA", "ME", "MD", "MA", "MI", "MN", "MS", "MO", "MT", "NE", "NV", "NH",
            "NJ", "NM", "NY", "NC", "ND", "OH", "OK", "OR", "PA", "PR", "RI", "SC", "SD", "TN", "TX",
            "UT", "VT", "VA", "WA", "WV", "WI", "WY",
        }.Select(state => new object[] { state });

    [Theory]
    [MemberData(nameof(Jurisdictions))]
    public void Every_jurisdiction_carries_providers(string state)
    {
        Assert.Contains(state, Directory.Jurisdictions);
        Assert.NotEmpty(Directory.ProvidersIn(state));
    }

    /// <summary>
    /// PROVENANCE: FIND-008 - the inherited provider seed carried NPIs that were ten digits and not
    /// NPIs. This snapshot is distilled by a script that applies the check-digit rule in Python;
    /// this Theory applies it again from the domain implementation, so the two agree by independent
    /// arrival rather than by sharing code.
    /// </summary>
    [Theory]
    [MemberData(nameof(Jurisdictions))]
    public void Every_seeded_npi_satisfies_the_check_digit(string state)
    {
        foreach (var provider in Directory.ProvidersIn(state))
        {
            Assert.True(NationalProviderIdentifier.IsValid(provider.Npi),
                $"{state}: '{provider.Npi}' for {provider.OrganisationOrLastName} is not a valid NPI.");
        }
    }

    /// <summary>Every value fits the governed Section 2 column it is written into.</summary>
    [Theory]
    [MemberData(nameof(Jurisdictions))]
    public void Every_seeded_value_fits_its_governed_column(string state)
    {
        foreach (var provider in Directory.ProvidersIn(state))
        {
            Assert.InRange(provider.OrganisationOrLastName.Length, 1, 100);   // Loop2010AA_NM103
            Assert.InRange(provider.AddressLine.Length, 1, 55);               // Loop2010AA_N301
            Assert.InRange(provider.City.Length, 1, 30);                      // Loop2010AA_N401
            Assert.Equal(2, provider.State.Length);                           // Loop2010AA_N402
            Assert.InRange(provider.ZipCode.Length, 1, 15);                   // Loop2010AA_N403

            if (provider.FirstName is { } first)
            {
                Assert.InRange(first.Length, 1, 35);                          // Loop2010AA_NM104
            }
        }
    }

    /// <summary>
    /// A value carrying an X12 delimiter cannot be serialised - the writer refuses it rather than
    /// emit a stream that parses into different data than it was given. Such a provider must
    /// therefore never reach the seed, because every seeded provider ends up in an 837.
    /// </summary>
    [Theory]
    [MemberData(nameof(Jurisdictions))]
    public void No_seeded_value_carries_an_x12_delimiter(string state)
    {
        foreach (var provider in Directory.ProvidersIn(state))
        {
            foreach (var value in new[]
                     {
                         provider.OrganisationOrLastName, provider.FirstName, provider.AddressLine,
                         provider.City, provider.State, provider.ZipCode,
                     })
            {
                Assert.DoesNotContain(value ?? "", character => character is '*' or '~' or ':' or '^');
            }
        }
    }

    [Theory]
    [MemberData(nameof(Jurisdictions))]
    public async Task Provider_returned_for_a_jurisdiction_is_in_that_jurisdiction(string state)
    {
        for (var selector = 0; selector < 20; selector++)
        {
            var provider = await Directory.ProviderForAsync(state, selector);

            Assert.Equal(state, provider.State);
        }
    }

    /// <summary>
    /// PROVENANCE: ADR-014 - the same request yields the same provider, so a batch that fails a
    /// downstream assertion can be regenerated exactly.
    /// </summary>
    [Theory]
    [MemberData(nameof(Jurisdictions))]
    public async Task Same_jurisdiction_and_selector_yield_the_same_provider(string state)
    {
        for (var selector = 0; selector < 10; selector++)
        {
            var first = await Directory.ProviderForAsync(state, selector);
            var second = await Directory.ProviderForAsync(state, selector);

            Assert.Equal(first, second);
        }
    }

    /// <summary>A batch must not put the same provider on every claim.</summary>
    [Theory]
    [MemberData(nameof(Jurisdictions))]
    public async Task Different_selectors_reach_different_providers(string state)
    {
        var seen = new HashSet<string>();

        for (var selector = 0; selector < 30; selector++)
        {
            seen.Add((await Directory.ProviderForAsync(state, selector)).Npi);
        }

        Assert.True(seen.Count > 5, $"{state} produced only {seen.Count} distinct providers across 30 selectors.");
    }

    /// <summary>
    /// Both NM102 branches are represented. An organisational provider carries no first name and a
    /// person carries one, and governance makes Loop2010AA_NM104 the one nullable column in the
    /// block precisely so the distinction can be drawn.
    /// </summary>
    [Fact]
    public void Snapshot_carries_both_organisations_and_people()
    {
        var all = Directory.Jurisdictions.SelectMany(Directory.ProvidersIn).ToList();

        Assert.Contains(all, provider => provider.FirstName is null);
        Assert.Contains(all, provider => provider.FirstName is not null);
    }

    /// <summary>
    /// The reason this snapshot exists. Governance User Story 1.3 allows 3.0 seconds for 500 bills,
    /// and the live registry answers one provider per request, so a batch of 500 was 500 round trips
    /// against that budget. Reading a local snapshot makes it none.
    /// </summary>
    [Fact]
    public async Task Generating_a_governed_batch_makes_no_network_calls()
    {
        var exploding = new CountingTransport();

        var directory = new ResilientProviderDirectory(
            Directory,
            new NpiRegistryProviderDirectory(new HttpClient(exploding)
            {
                BaseAddress = new Uri(NpiRegistryProviderDirectory.DefaultBaseAddress),
            }));

        var generator = new SyntheticClaimGenerator(directory, new SeedMedicalCodeCatalog(), new SeedChargeSchedule());

        var stopwatch = Stopwatch.StartNew();
        var claims = await generator.GenerateAsync(
            new BatchGenerationRequest(500, "OH", ["Cardiac", "Anesthesia", "PhysicalTherapy"], Seed: 20260830));
        stopwatch.Stop();

        Assert.Equal(500, claims.Count);
        Assert.Equal(0, exploding.Calls);
        Assert.All(claims, claim => Assert.True(
            NationalProviderIdentifier.IsValid(claim.Loop2010AA_NM109_BillingProviderNpi)));
    }

    /// <summary>A transport that counts what it is asked to send, and refuses to send it.</summary>
    private sealed class CountingTransport : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        }
    }
}
