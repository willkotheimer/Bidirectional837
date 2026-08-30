using System.Collections.Immutable;
using System.Globalization;

namespace Translator.Generation;

/// <summary>
/// Governance User Story 1.2: published standard charges, or deterministic fallback charges.
/// </summary>
public interface IChargeSchedule
{
    decimal ChargeFor(string procedureCode);
}

/// <summary>
/// PROVENANCE: ADR-013 - published-style charges for the curated catalog, and a deterministic
/// fallback for any code outside it. Governance User Story 1.2 permits either.
/// </summary>
public sealed class SeedChargeSchedule : IChargeSchedule
{
    private static readonly ImmutableDictionary<string, decimal> Published = Load();

    /// <summary>Bounds of the fallback band, in whole cents.</summary>
    private const int FallbackFloorCents = 1_000;
    private const int FallbackCeilingCents = 250_000;

    public decimal ChargeFor(string procedureCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(procedureCode);

        return Published.TryGetValue(procedureCode, out var published)
            ? published
            : FallbackFor(procedureCode);
    }

    /// <summary>
    /// A charge derived from the code itself, so an uncatalogued code prices identically on every
    /// run and on every machine. A hash-code-based derivation would not: string hashing is
    /// randomised per process in .NET, which would make generated batches irreproducible and defeat
    /// the seeded determinism the generator guarantees.
    /// </summary>
    private static decimal FallbackFor(string procedureCode)
    {
        var accumulator = 17;
        foreach (var character in procedureCode)
        {
            accumulator = unchecked((accumulator * 31) + character);
        }

        var span = FallbackCeilingCents - FallbackFloorCents;
        var cents = FallbackFloorCents + (Math.Abs(accumulator) % span);

        return cents * 0.01m;
    }

    private static ImmutableDictionary<string, decimal> Load() =>
        SeedResource
            .ReadRows("Translator.Generation.Seed.charges_sample.csv")
            .ToImmutableDictionary(
                cells => cells[0],
                cells => decimal.Parse(cells[^1], NumberStyles.Number, CultureInfo.InvariantCulture),
                StringComparer.Ordinal);
}
