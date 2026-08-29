namespace Governance.Generation;

/// <summary>A procedure code, its category, and its description.</summary>
public record MedicalCode(string Code, string Category, string Description);

/// <summary>
/// Governance User Story 1.2: valid medical codes drawn from selected categories.
/// </summary>
public interface IMedicalCodeCatalog
{
    IReadOnlyList<string> Categories { get; }
    IReadOnlyList<MedicalCode> CodesIn(string category);
}

/// <summary>NOT YET IMPLEMENTED - see Governance.Generation.Tests.</summary>
public sealed class SeedMedicalCodeCatalog : IMedicalCodeCatalog
{
    public IReadOnlyList<string> Categories => throw new NotImplementedException(nameof(Categories));

    public IReadOnlyList<MedicalCode> CodesIn(string category)
        => throw new NotImplementedException(nameof(CodesIn));
}
