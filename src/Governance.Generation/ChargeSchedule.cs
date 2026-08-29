namespace Governance.Generation;

/// <summary>
/// Governance User Story 1.2: published standard charges, or deterministic fallback charges.
/// </summary>
public interface IChargeSchedule
{
    decimal ChargeFor(string procedureCode);
}

/// <summary>NOT YET IMPLEMENTED - see Governance.Generation.Tests.</summary>
public sealed class SeedChargeSchedule : IChargeSchedule
{
    public decimal ChargeFor(string procedureCode)
        => throw new NotImplementedException(nameof(ChargeFor));
}
