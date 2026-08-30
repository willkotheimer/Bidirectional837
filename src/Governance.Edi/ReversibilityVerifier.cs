using Governance.Domain.Entities;

namespace Governance.Edi;

/// <summary>
/// The verdict of a zero-mutation round trip, as governance User Story 3.2 states it: the emitted
/// text compared against its re-emission, and the stored record compared against its reconstruction.
/// </summary>
/// <remarks>
/// Both halves are reported separately and both are reported honestly. A verdict that collapsed
/// them into one boolean could be made true by comparing less, which is the failure mode a
/// reversibility check exists to prevent.
/// </remarks>
public sealed record ReversibilityVerdict(
    bool EdiTextIsIdentical,
    bool RecordIsIdentical,
    IReadOnlyList<string> Differences);

/// <summary>
/// Governance User Story 3.2, and the governance Section 4 Roundtrip Reversibility Test Standard:
/// export a stored claim, read the result back, and prove that neither the text nor the record
/// moved.
/// </summary>
/// <remarks>NOT YET IMPLEMENTED - see Governance.Edi.Tests.</remarks>
public sealed class ReversibilityVerifier
{
    public ReversibilityVerifier(Edi837Serializer serializer, Edi837Parser parser)
    {
        Serializer = serializer;
        Parser = parser;
    }

    public Edi837Serializer Serializer { get; }
    public Edi837Parser Parser { get; }

    /// <summary>Exports the claim, re-imports it, and reports whether anything changed.</summary>
    public ReversibilityVerdict Verify(ClaimHeader stored) => throw new NotImplementedException(nameof(Verify));

    /// <summary>
    /// Every governed column on which two claims differ, named by its Section 2 column name.
    /// Storage identity is not compared: it has no 837 counterpart, so a reader cannot recover it
    /// and its difference would be noise rather than mutation (ADR-016).
    /// </summary>
    public static IReadOnlyList<string> Differences(ClaimHeader left, ClaimHeader right) =>
        throw new NotImplementedException(nameof(Differences));
}
