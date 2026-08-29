namespace Governance.Generation;

/// <summary>The Loop2010AA billing provider details a generated claim needs.</summary>
public record BillingProvider(
    string OrganisationOrLastName,
    string? FirstName,
    string Npi,
    string AddressLine,
    string City,
    string State,
    string ZipCode);

/// <summary>
/// Governance User Story 1.1: query the open-source NPI registry using the requested jurisdiction
/// state, falling back gracefully to a mock state-compliant provider set if it is unreachable.
/// </summary>
public interface IProviderDirectory
{
    Task<BillingProvider> ProviderForAsync(string jurisdictionState, int selector, CancellationToken cancellationToken = default);
}

/// <summary>NOT YET IMPLEMENTED - see Governance.Generation.Tests.</summary>
public sealed class SyntheticProviderDirectory : IProviderDirectory
{
    public Task<BillingProvider> ProviderForAsync(string jurisdictionState, int selector, CancellationToken cancellationToken = default)
        => throw new NotImplementedException(nameof(ProviderForAsync));
}

/// <summary>NOT YET IMPLEMENTED - see Governance.Generation.Tests.</summary>
public sealed class NpiRegistryProviderDirectory : IProviderDirectory
{
    public NpiRegistryProviderDirectory(HttpClient httpClient) => HttpClient = httpClient;

    public HttpClient HttpClient { get; }

    public Task<BillingProvider> ProviderForAsync(string jurisdictionState, int selector, CancellationToken cancellationToken = default)
        => throw new NotImplementedException(nameof(ProviderForAsync));
}

/// <summary>NOT YET IMPLEMENTED - see Governance.Generation.Tests.</summary>
public sealed class ResilientProviderDirectory : IProviderDirectory
{
    public ResilientProviderDirectory(IProviderDirectory primary, IProviderDirectory fallback)
    {
        Primary = primary;
        Fallback = fallback;
    }

    public IProviderDirectory Primary { get; }
    public IProviderDirectory Fallback { get; }

    public Task<BillingProvider> ProviderForAsync(string jurisdictionState, int selector, CancellationToken cancellationToken = default)
        => throw new NotImplementedException(nameof(ProviderForAsync));
}
