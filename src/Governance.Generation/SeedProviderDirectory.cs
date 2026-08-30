using Governance.Domain.Entities;

namespace Governance.Generation;

/// <summary>
/// Governance User Story 1.1, served from a distilled snapshot of the NPPES registry rather than
/// from the registry's query API.
/// </summary>
/// <remarks>
/// NOT YET IMPLEMENTED - see Governance.Generation.Tests.
///
/// PROVENANCE: ADR-023 - the bulk NPPES file and the registry API are the same data from the same
/// authority; the API is a query interface over the file. Reading a distilled snapshot gives the
/// same real providers governance asks for, and turns a batch of 500 claims from 500 network calls
/// into none.
/// </remarks>
public sealed class SeedProviderDirectory : IProviderDirectory
{
    /// <summary>Every jurisdiction the snapshot carries providers for.</summary>
    public IReadOnlyCollection<string> Jurisdictions => throw new NotImplementedException(nameof(Jurisdictions));

    /// <summary>Every provider the snapshot carries for a jurisdiction, in file order.</summary>
    public IReadOnlyList<BillingProvider> ProvidersIn(string jurisdictionState) =>
        throw new NotImplementedException(nameof(ProvidersIn));

    public Task<BillingProvider> ProviderForAsync(
        string jurisdictionState, int selector, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException(nameof(ProviderForAsync));
}
