using Governance.Domain.Entities;

namespace Governance.Generation;

/// <summary>A request to generate a batch of synthetic claims.</summary>
public record BatchGenerationRequest(
    int BillCount,
    string JurisdictionState,
    IReadOnlyList<string> MedicalCodeCategories,
    int Seed);

/// <summary>
/// Governance Feature 1: the synthetic bill batch generator.
/// NOT YET IMPLEMENTED - see Governance.Generation.Tests.
/// </summary>
public sealed class SyntheticClaimGenerator
{
    public SyntheticClaimGenerator(
        IProviderDirectory providerDirectory,
        IMedicalCodeCatalog codeCatalog,
        IChargeSchedule chargeSchedule)
    {
        ProviderDirectory = providerDirectory;
        CodeCatalog = codeCatalog;
        ChargeSchedule = chargeSchedule;
    }

    public IProviderDirectory ProviderDirectory { get; }
    public IMedicalCodeCatalog CodeCatalog { get; }
    public IChargeSchedule ChargeSchedule { get; }

    public Task<IReadOnlyList<ClaimHeader>> GenerateAsync(BatchGenerationRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException(nameof(GenerateAsync));
}
