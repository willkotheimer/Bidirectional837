using Governance.Domain.Entities;

namespace Governance.Edi;

/// <summary>
/// Governance User Story 2.1: serialises a governed <see cref="ClaimHeader"/> into an ASC X12 837
/// Professional (005010X222A2) transaction, wrapped in its own interchange.
/// </summary>
/// <remarks>NOT YET IMPLEMENTED - see Governance.Edi.Tests.</remarks>
public sealed class Edi837Serializer
{
    public Edi837Serializer(X12Delimiters? delimiters = null)
        => Delimiters = delimiters ?? X12Delimiters.Default;

    public X12Delimiters Delimiters { get; }

    /// <summary>The complete interchange for one claim, ISA through IEA.</summary>
    public string Serialize(ClaimHeader claim) => throw new NotImplementedException(nameof(Serialize));
}
