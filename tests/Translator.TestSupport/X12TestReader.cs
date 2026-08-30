namespace Translator.TestSupport;

/// <summary>One parsed segment: its identifier and its elements, in order.</summary>
public sealed record ParsedSegment(string Id, IReadOnlyList<string> Elements)
{
    /// <summary>Element by its one-based X12 position, or the empty string past the end.</summary>
    public string this[int position] =>
        position >= 1 && position <= Elements.Count ? Elements[position - 1] : string.Empty;

    /// <summary>Component <paramref name="component"/> of the composite at <paramref name="position"/>.</summary>
    public string Component(int position, int component)
    {
        var parts = this[position].Split(':');
        return component >= 1 && component <= parts.Length ? parts[component - 1] : string.Empty;
    }

    public override string ToString() => Id + "*" + string.Join('*', Elements);
}

/// <summary>
/// A deliberately independent X12 reader, used only by tests.
/// </summary>
/// <remarks>
/// It is not the ingestion parser: that is a governance Feature 3 deliverable. Keeping the two
/// apart matters beyond sequencing. A test that measured the writer with the reader written to
/// match it would agree with itself about a shared misreading of the standard, and the Section 1
/// Reversibility Guarantee would be proven against nothing. This reader knows only how X12
/// delimits segments and elements, and asserts nothing about meaning.
/// </remarks>
public static class X12TestReader
{
    public static IReadOnlyList<ParsedSegment> Read(string interchange)
    {
        var segments = new List<ParsedSegment>();

        foreach (var raw in interchange.Split('~'))
        {
            // A segment terminator may be followed by whitespace for readability; it is not content.
            var text = raw.Trim('\r', '\n', ' ');
            if (text.Length == 0) continue;

            var elements = text.Split('*');
            segments.Add(new ParsedSegment(elements[0], elements[1..]));
        }

        return segments;
    }

    /// <summary>The single segment with this identifier. Throws if it is absent or repeated.</summary>
    public static ParsedSegment Single(string interchange, string segmentId)
    {
        var matches = Read(interchange).Where(segment => segment.Id == segmentId).ToList();

        if (matches.Count != 1)
        {
            throw new InvalidOperationException(
                $"Expected exactly one {segmentId} segment, found {matches.Count}.");
        }

        return matches[0];
    }

    public static IReadOnlyList<ParsedSegment> All(string interchange, string segmentId) =>
        Read(interchange).Where(segment => segment.Id == segmentId).ToList();
}
