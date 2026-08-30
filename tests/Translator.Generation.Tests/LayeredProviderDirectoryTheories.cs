namespace Translator.Generation.Tests;

/// <summary>
/// PROVENANCE: FIND-018 - a local source and a remote source need different fallback behaviour, and
/// using one policy for both silently disables the good source.
///
/// ResilientProviderDirectory sets its primary aside permanently after a single failure. That is
/// right for the registry, and ADR-012 gives the reason: a batch of 500 claims must not become 500
/// failed network calls each waiting out a timeout. It is wrong for the snapshot, which fails only
/// for a jurisdiction it does not carry and can still serve every other one at no cost.
/// </summary>
public class LayeredProviderDirectoryTheories
{
    /// <summary>A directory that serves exactly one jurisdiction and refuses everything else.</summary>
    private sealed class NarrowDirectory : IProviderDirectory
    {
        private readonly string _served;

        public NarrowDirectory(string served) => _served = served;

        public int Calls { get; private set; }

        public Task<BillingProvider> ProviderForAsync(
            string jurisdictionState, int selector, CancellationToken cancellationToken = default)
        {
            Calls++;

            if (!string.Equals(jurisdictionState, _served, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"No providers for {jurisdictionState}.");
            }

            return Task.FromResult(new BillingProvider(
                "NARROW HEALTH", null, "1245319599", "1 NARROW WAY", "COLUMBUS", _served, "43215"));
        }
    }

    /// <summary>
    /// The defect this type exists to avoid. One unserviceable jurisdiction must not cost the
    /// application every other jurisdiction.
    /// </summary>
    [Fact]
    public async Task A_jurisdiction_the_primary_cannot_serve_does_not_disable_it_for_the_rest()
    {
        var primary = new NarrowDirectory("OH");
        var layered = new LayeredProviderDirectory(primary, new SyntheticProviderDirectory());

        var missing = await layered.ProviderForAsync("ZZ", 0);
        Assert.Equal("ZZ", missing.State);

        var served = await layered.ProviderForAsync("OH", 0);

        Assert.Equal("NARROW HEALTH", served.OrganisationOrLastName);
        Assert.Equal(2, primary.Calls);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(25)]
    [InlineData(500)]
    public async Task Primary_is_consulted_on_every_request(int requests)
    {
        var primary = new NarrowDirectory("OH");
        var layered = new LayeredProviderDirectory(primary, new SyntheticProviderDirectory());

        for (var index = 0; index < requests; index++)
        {
            await layered.ProviderForAsync(index % 2 == 0 ? "OH" : "ZZ", index);
        }

        Assert.Equal(requests, primary.Calls);
    }

    /// <summary>
    /// The contrast, asserted so the two policies cannot quietly converge. ADR-012's latch is still
    /// the right behaviour for the registry, and must survive.
    /// </summary>
    [Fact]
    public async Task Resilient_directory_still_sets_a_failed_remote_primary_aside()
    {
        var primary = new NarrowDirectory("OH");
        var resilient = new ResilientProviderDirectory(primary, new SyntheticProviderDirectory());

        await resilient.ProviderForAsync("ZZ", 0);
        await resilient.ProviderForAsync("OH", 0);
        await resilient.ProviderForAsync("OH", 1);

        Assert.True(resilient.PrimaryUnavailable);
        Assert.Equal(1, primary.Calls);
    }

    /// <summary>
    /// The full chain governance User Story 1.1 describes: real data first, the registry for what
    /// the snapshot does not carry, and the mock set behind both. Every jurisdiction resolves.
    /// </summary>
    [Theory]
    [InlineData("OH")]
    [InlineData("WY")]
    [InlineData("PR")]
    [InlineData("ZZ")]
    public async Task Chain_resolves_every_jurisdiction_including_ones_outside_the_snapshot(string state)
    {
        var chain = new LayeredProviderDirectory(new SeedProviderDirectory(), new SyntheticProviderDirectory());

        var provider = await chain.ProviderForAsync(state, 3);

        Assert.Equal(state, provider.State);
        Assert.NotEmpty(provider.OrganisationOrLastName);
        Assert.NotEmpty(provider.AddressLine);
    }
}
