using Governance.Domain.Entities;

namespace Governance.Edi;

/// <summary>
/// Raised when an interchange cannot be read as a governed 837 Professional claim.
/// </summary>
/// <remarks>
/// It carries a message naming what was wrong rather than where the reader gave up, because
/// governance User Story 3.1 requires malformed files to be handled and the person handling one
/// needs to know which segment to look at.
/// </remarks>
public sealed class EdiFormatException : Exception
{
    public EdiFormatException(string message) : base(message) { }

    public EdiFormatException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// Governance User Story 3.1: reads an ASC X12 837 Professional interchange back into the governed
/// <see cref="ClaimHeader"/> entity, mapping each element to the Section 2 column named for it.
/// </summary>
/// <remarks>NOT YET IMPLEMENTED - see Governance.Edi.Tests.</remarks>
public sealed class Edi837Parser
{
    /// <summary>The claim carried by one interchange.</summary>
    public ClaimHeader Parse(string interchange) => throw new NotImplementedException(nameof(Parse));
}
