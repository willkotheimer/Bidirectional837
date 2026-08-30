using System.Collections.Frozen;
using System.Reflection;

namespace Governance.Generation;

/// <summary>
/// Governance User Story 1.1, served from a distilled snapshot of the NPPES registry rather than
/// from the registry's query API.
/// </summary>
/// <remarks>
/// PROVENANCE: ADR-023 - the bulk NPPES file and the registry API are the same data from the same
/// authority; the API is a query interface over the file. Reading a distilled snapshot gives the
/// same real providers governance asks for, and turns a batch of 500 claims from 500 network calls
/// into none.
///
/// The snapshot is embedded in the assembly rather than copied beside it, for the reason ADR-013
/// gives about the code catalog: a deployed instance cannot start without its reference data.
/// </remarks>
public sealed class SeedProviderDirectory : IProviderDirectory
{
    private const string ResourceName = "Governance.Generation.Seed.providers_by_state.csv";

    /// <summary>Loaded once. The snapshot is immutable and shared by every request.</summary>
    private static readonly FrozenDictionary<string, BillingProvider[]> ByJurisdiction = Load();

    public IReadOnlyCollection<string> Jurisdictions => ByJurisdiction.Keys;

    public IReadOnlyList<BillingProvider> ProvidersIn(string jurisdictionState)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jurisdictionState);

        return ByJurisdiction.TryGetValue(jurisdictionState.ToUpperInvariant(), out var providers)
            ? providers
            : [];
    }

    public Task<BillingProvider> ProviderForAsync(
        string jurisdictionState, int selector, CancellationToken cancellationToken = default)
    {
        var providers = ProvidersIn(jurisdictionState);

        if (providers.Count == 0)
        {
            // Not an error the caller should swallow silently: the jurisdiction is outside the
            // snapshot, and the resilient wrapper decides what to do about it. Throwing keeps the
            // fallback chain governance User Story 1.1 requires in one place.
            throw new InvalidOperationException(
                $"The provider snapshot carries no providers for '{jurisdictionState}'.");
        }

        // Non-negative regardless of the selector, so a caller counting down or passing int.MinValue
        // still lands inside the list rather than throwing on a negative index.
        var index = (int)((uint)selector % (uint)providers.Count);

        return Task.FromResult(providers[index]);
    }

    private static FrozenDictionary<string, BillingProvider[]> Load()
    {
        using var stream = typeof(SeedProviderDirectory).GetTypeInfo().Assembly
            .GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"The embedded provider snapshot '{ResourceName}' is missing.");

        using var reader = new StreamReader(stream);
        var grouped = new Dictionary<string, List<BillingProvider>>(StringComparer.OrdinalIgnoreCase);

        reader.ReadLine();   // header

        while (reader.ReadLine() is { } line)
        {
            if (line.Length == 0) continue;

            // PROVENANCE: FIND-019 - one quote-aware reader, in one place. Provider names
            // legitimately contain commas: "ALABAMA CARDIOVASCULAR GROUP, P.C." is in the snapshot.
            var cells = SeedResource.SplitRow(line);
            if (cells.Length < 8) continue;

            var state = cells[6].ToUpperInvariant();

            // The distillation writes an empty first name for an organisational provider. Loop2010AA
            // NM104 is nullable precisely to carry that distinction, and NM102 is derived from it.
            var firstName = cells[3].Length == 0 ? null : cells[3];

            if (!grouped.TryGetValue(state, out var providers))
            {
                providers = grouped[state] = [];
            }

            providers.Add(new BillingProvider(
                OrganisationOrLastName: cells[2],
                FirstName: firstName,
                Npi: cells[0],
                AddressLine: cells[4],
                City: cells[5],
                State: state,
                ZipCode: cells[7]));
        }

        return grouped.ToFrozenDictionary(entry => entry.Key, entry => entry.Value.ToArray(),
            StringComparer.OrdinalIgnoreCase);
    }

}
