using Governance.Domain.Entities;

namespace Governance.Edi;

/// <summary>
/// Governance User Story 2.2: packages serialised 837 transactions into a ZIP archive for
/// transport.
/// </summary>
/// <remarks>NOT YET IMPLEMENTED - see Governance.Edi.Tests.</remarks>
public sealed class ClaimArchive
{
    public ClaimArchive(Edi837Serializer serializer) => Serializer = serializer;

    public Edi837Serializer Serializer { get; }

    /// <summary>The archive entry name for a claim.</summary>
    public static string EntryNameFor(ClaimHeader claim) => throw new NotImplementedException(nameof(EntryNameFor));

    /// <summary>The ZIP archive holding one 837 file per claim.</summary>
    public byte[] Package(IReadOnlyList<ClaimHeader> claims) => throw new NotImplementedException(nameof(Package));
}
