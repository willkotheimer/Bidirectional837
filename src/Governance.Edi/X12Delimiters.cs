namespace Governance.Edi;

/// <summary>
/// The four ASC X12 delimiters, which an interchange declares in its own ISA segment rather than
/// assuming: the element separator is the character at ISA position 4, the repetition separator is
/// ISA11, the component separator is ISA16, and the segment terminator is the character following
/// it.
/// </summary>
/// <remarks>
/// PROVENANCE: GOVERNANCE-1 - the Reversibility Guarantee is why these are a value rather than
/// constants scattered through the writer. An interchange is read with the delimiters it declares
/// and written with the delimiters it will declare, so the two directions cannot drift apart.
/// </remarks>
public sealed record X12Delimiters(char Element, char Component, char Repetition, char Segment)
{
    /// <summary>The delimiter set this application emits, and the one HIPAA 5010 examples use.</summary>
    public static readonly X12Delimiters Default = new('*', ':', '^', '~');

    /// <summary>True if <paramref name="value"/> carries a character that would corrupt the stream.</summary>
    public bool CollidesWith(string? value) =>
        value is not null &&
        value.Any(character => character == Element || character == Component ||
                               character == Repetition || character == Segment);
}
