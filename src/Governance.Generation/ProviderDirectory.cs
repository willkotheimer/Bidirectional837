using System.Text.Json;
using Governance.Domain.Validation;

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

/// <summary>
/// PROVENANCE: ADR-012 - the mock state-compliant provider set governance User Story 1.1 requires
/// as a fallback. Providers are synthesised rather than drawn from a fixed list, so every state is
/// served: a fallback that cannot answer for Wyoming is not a graceful one.
/// </summary>
public sealed class SyntheticProviderDirectory : IProviderDirectory
{
    private static readonly string[] OrganisationForms =
        ["MEDICAL GROUP", "HEALTH PARTNERS", "CLINIC", "PHYSICIANS NETWORK", "CARE ASSOCIATES"];

    private static readonly string[] Localities =
        ["RIVERSIDE", "FAIRVIEW", "LAKESIDE", "HILLCREST", "BRIDGEPORT", "OAKMONT", "WESTFIELD", "STONEBROOK"];

    private static readonly string[] Thoroughfares =
        ["MAIN STREET", "PARK AVENUE", "CEDAR ROAD", "MERIDIAN PARKWAY", "SUMMIT BOULEVARD"];

    /// <summary>
    /// The leading digits of a real ZIP for each state, so a fallback address is plausible for its
    /// jurisdiction rather than merely well-formed. Any state absent here still receives a
    /// well-formed ZIP; correctness of the prefix is a nicety, availability is not.
    /// </summary>
    private static readonly Dictionary<string, int> ZipPrefixByState = new(StringComparer.OrdinalIgnoreCase)
    {
        ["AL"] = 350, ["AK"] = 995, ["AZ"] = 850, ["AR"] = 720, ["CA"] = 900, ["CO"] = 800,
        ["CT"] = 60, ["DE"] = 199, ["DC"] = 200, ["FL"] = 331, ["GA"] = 303, ["HI"] = 968,
        ["ID"] = 837, ["IL"] = 606, ["IN"] = 462, ["IA"] = 503, ["KS"] = 662, ["KY"] = 402,
        ["LA"] = 701, ["ME"] = 43, ["MD"] = 212, ["MA"] = 21, ["MI"] = 482, ["MN"] = 554,
        ["MS"] = 392, ["MO"] = 631, ["MT"] = 591, ["NE"] = 681, ["NV"] = 891, ["NH"] = 33,
        ["NJ"] = 71, ["NM"] = 871, ["NY"] = 100, ["NC"] = 282, ["ND"] = 581, ["OH"] = 432,
        ["OK"] = 731, ["OR"] = 972, ["PA"] = 191, ["RI"] = 29, ["SC"] = 292, ["SD"] = 571,
        ["TN"] = 372, ["TX"] = 750, ["UT"] = 841, ["VT"] = 54, ["VA"] = 232, ["WA"] = 981,
        ["WV"] = 251, ["WI"] = 532, ["WY"] = 820, ["PR"] = 6,
    };

    /// <summary>
    /// PROVENANCE: FIND-017 - the leading three digits of a real ZIP for a jurisdiction, as text.
    ///
    /// Exposed because the registry query needs it too: it is the companion criterion that makes a
    /// `state` query answerable, and it must narrow within the jurisdiction rather than across it.
    /// The map holds these as integers, so a prefix below 100 - Connecticut's 060, Massachusetts'
    /// 021 - has to be padded back to three digits or it would ask the registry for the wrong place.
    /// </summary>
    /// <exception cref="KeyNotFoundException">
    /// The jurisdiction has no known prefix, so no answerable registry query can be built for it.
    /// The caller's fallback chain handles that, which is where governance User Story 1.1 puts it.
    /// </exception>
    public static string ZipPrefixFor(string jurisdictionState)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jurisdictionState);

        var state = jurisdictionState.ToUpperInvariant();

        return ZipPrefixByState.TryGetValue(state, out var prefix)
            ? prefix.ToString("D3")
            : throw new KeyNotFoundException(
                $"No ZIP prefix is known for '{state}', so no registry query can be narrowed to it.");
    }

    public Task<BillingProvider> ProviderForAsync(string jurisdictionState, int selector, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jurisdictionState);

        var state = jurisdictionState.ToUpperInvariant();

        // Derived from the state and the selector alone, so the same request always yields the
        // same provider, on any machine and in any process.
        var stream = Stable(state, selector);

        var locality = Localities[stream % Localities.Length];
        var form = OrganisationForms[(stream / 7) % OrganisationForms.Length];
        var thoroughfare = Thoroughfares[(stream / 13) % Thoroughfares.Length];
        var streetNumber = 100 + (stream % 8900);
        var zipPrefix = ZipPrefixByState.TryGetValue(state, out var prefix) ? prefix : stream % 1000;
        var zipSuffix = stream % 100;

        var npiBase = (100_000_000 + (stream % 899_999_999)).ToString();

        return Task.FromResult(new BillingProvider(
            OrganisationOrLastName: $"{locality} {form}",
            FirstName: null,
            Npi: npiBase + NationalProviderIdentifier.CheckDigitFor(npiBase),
            AddressLine: $"{streetNumber} {thoroughfare}",
            City: locality,
            State: state,
            ZipCode: $"{zipPrefix:D3}{zipSuffix:D2}"));
    }

    /// <summary>
    /// A stable, process-independent hash. String.GetHashCode is randomised per process in .NET,
    /// which would make the fallback set differ between runs and break the generator's determinism.
    /// </summary>
    private static int Stable(string state, int selector)
    {
        var accumulator = 17;
        foreach (var character in state)
        {
            accumulator = unchecked((accumulator * 31) + character);
        }

        accumulator = unchecked((accumulator * 31) + selector);
        return Math.Abs(accumulator);
    }
}

/// <summary>
/// PROVENANCE: ADR-012 - the live path of governance User Story 1.1. Queries the CMS NPI registry
/// for organisational providers in the requested state.
/// </summary>
public sealed class NpiRegistryProviderDirectory : IProviderDirectory
{
    /// <summary>The public registry endpoint. Recorded in docs/PROVENANCE.md.</summary>
    public const string DefaultBaseAddress = "https://npiregistry.cms.hhs.gov/";

    private const int PageSize = 20;

    public NpiRegistryProviderDirectory(HttpClient httpClient) => HttpClient = httpClient;

    public HttpClient HttpClient { get; }

    public async Task<BillingProvider> ProviderForAsync(string jurisdictionState, int selector, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jurisdictionState);

        var state = jurisdictionState.ToUpperInvariant();

        // PROVENANCE: FIND-017 - `state` is not a query the registry will answer on its own. It
        // requires a companion criterion, and refuses a query without one using HTTP 200 and an
        // error body, so the status code says nothing about it. The ZIP prefix is used as that
        // companion because it narrows *within* the jurisdiction rather than across it: a criterion
        // that replaced the state would return providers from anywhere.
        var zipPrefix = SyntheticProviderDirectory.ZipPrefixFor(state);
        var requestUri =
            $"api/?version=2.1&state={state}&postal_code={zipPrefix}*&enumeration_type=NPI-2&limit={PageSize}";

        using var response = await HttpClient.GetAsync(requestUri, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken);

        // PROVENANCE: FIND-017 - a rejection arrives as HTTP 200 with an Errors array. Reading it as
        // an empty result set produces the same fallback but reports the wrong reason, and the
        // reason is the only thing that distinguishes a misconfigured query from an unlucky one.
        if (document.RootElement.TryGetProperty("Errors", out var errors) &&
            errors.ValueKind == JsonValueKind.Array &&
            errors.GetArrayLength() > 0)
        {
            var complaints = errors.EnumerateArray()
                .Select(error => Text(error, "description"))
                .Where(description => description is not null);

            throw new InvalidOperationException(
                $"The NPI registry refused the query for {state}: {string.Join("; ", complaints)}");
        }

        if (!document.RootElement.TryGetProperty("results", out var results) ||
            results.ValueKind != JsonValueKind.Array ||
            results.GetArrayLength() == 0)
        {
            throw new InvalidOperationException($"The NPI registry returned no providers for {state}.");
        }

        var result = results[selector % results.GetArrayLength()];
        var basic = result.GetProperty("basic");
        var address = LocationAddress(result);

        return new BillingProvider(
            OrganisationOrLastName: Text(basic, "organization_name") ?? Text(basic, "last_name") ?? "UNKNOWN PROVIDER",
            FirstName: Text(basic, "first_name"),
            Npi: result.GetProperty("number").GetString()!,
            AddressLine: Text(address, "address_1") ?? "UNKNOWN",
            City: Text(address, "city") ?? "UNKNOWN",
            State: Text(address, "state") ?? state,
            ZipCode: Text(address, "postal_code") ?? "00000");
    }

    /// <summary>Prefers the practice location over the mailing address, which is what Loop2010AA wants.</summary>
    private static JsonElement LocationAddress(JsonElement result)
    {
        var addresses = result.GetProperty("addresses");

        foreach (var address in addresses.EnumerateArray())
        {
            if (Text(address, "address_purpose") == "LOCATION") return address;
        }

        return addresses[0];
    }

    private static string? Text(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}

/// <summary>
/// PROVENANCE: ADR-012 - governance User Story 1.1: "If API is unreachable, fall back gracefully to
/// a mock state-compliant provider set."
/// </summary>
/// <remarks>
/// Unreachable is interpreted broadly and deliberately. A timeout, a 503, a DNS failure, an empty
/// result set and a body that will not parse are all states in which the registry cannot supply a
/// provider, and all of them must degrade to the fallback rather than fail a generation request.
/// Once the registry has failed, it is not retried for the lifetime of this directory: a batch of
/// 500 claims must not become 500 failed network calls.
/// </remarks>
public sealed class ResilientProviderDirectory : IProviderDirectory
{
    private volatile bool _primaryUnavailable;

    public ResilientProviderDirectory(IProviderDirectory primary, IProviderDirectory fallback)
    {
        Primary = primary;
        Fallback = fallback;
    }

    public IProviderDirectory Primary { get; }
    public IProviderDirectory Fallback { get; }

    /// <summary>Whether the primary directory has failed and been set aside for this instance.</summary>
    public bool PrimaryUnavailable => _primaryUnavailable;

    public async Task<BillingProvider> ProviderForAsync(string jurisdictionState, int selector, CancellationToken cancellationToken = default)
    {
        if (!_primaryUnavailable)
        {
            try
            {
                return await Primary.ProviderForAsync(jurisdictionState, selector, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                // The registry is an external dependency governance treats as optional. Any failure
                // to produce a provider is a fallback condition, not a generation failure.
                _primaryUnavailable = true;
            }
        }

        return await Fallback.ProviderForAsync(jurisdictionState, selector, cancellationToken);
    }
}

/// <summary>
/// Tries each source in order on every request, without setting any of them aside.
/// </summary>
/// <remarks>
/// PROVENANCE: FIND-018 - the counterpart to <see cref="ResilientProviderDirectory"/>, for a local
/// primary rather than a remote one.
///
/// The distinction is the cost of a retry. A remote source that failed once will probably fail
/// again and each attempt costs a timeout, so ADR-012 sets it aside for the lifetime of the
/// instance. A local source fails only for a jurisdiction it does not carry, costs nothing to ask
/// again, and can still serve every other jurisdiction - so setting it aside after one miss would
/// silently drop the whole application to the mock set on the strength of a single unusual request.
/// </remarks>
public sealed class LayeredProviderDirectory : IProviderDirectory
{
    public LayeredProviderDirectory(IProviderDirectory primary, IProviderDirectory fallback)
    {
        Primary = primary;
        Fallback = fallback;
    }

    public IProviderDirectory Primary { get; }
    public IProviderDirectory Fallback { get; }

    public async Task<BillingProvider> ProviderForAsync(string jurisdictionState, int selector, CancellationToken cancellationToken = default)
    {
        try
        {
            return await Primary.ProviderForAsync(jurisdictionState, selector, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return await Fallback.ProviderForAsync(jurisdictionState, selector, cancellationToken);
        }
    }
}
