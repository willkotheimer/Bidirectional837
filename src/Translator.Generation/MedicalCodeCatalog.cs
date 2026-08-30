using System.Collections.Immutable;
using System.Reflection;
using System.Text;

namespace Translator.Generation;

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

/// <summary>
/// PROVENANCE: ADR-013 - the catalog is a curated public HCPCS Level II set, embedded in the
/// assembly. CPT is proprietary to the AMA and is deliberately absent, as the inherited provenance
/// notes require.
/// </summary>
public sealed class SeedMedicalCodeCatalog : IMedicalCodeCatalog
{
    private static readonly ImmutableDictionary<string, ImmutableArray<MedicalCode>> ByCategory = Load();

    public IReadOnlyList<string> Categories { get; } = ByCategory.Keys.OrderBy(name => name).ToImmutableArray();

    public IReadOnlyList<MedicalCode> CodesIn(string category) =>
        ByCategory.TryGetValue(category, out var codes) ? codes : ImmutableArray<MedicalCode>.Empty;

    private static ImmutableDictionary<string, ImmutableArray<MedicalCode>> Load()
    {
        var codes = SeedResource
            .ReadRows("Translator.Generation.Seed.hcpcs_categories.csv")
            .Select(cells => new MedicalCode(cells[0], cells[1], cells[2]));

        return codes
            .GroupBy(code => code.Category, StringComparer.Ordinal)
            .ToImmutableDictionary(
                group => group.Key,
                group => group.ToImmutableArray(),
                StringComparer.Ordinal);
    }
}

/// <summary>Reads an embedded seed CSV, honouring quoted fields.</summary>
/// <remarks>
/// PROVENANCE: FIND-019 - this reader used to split on every comma, and documented the assumption
/// that "the seed files carry no quoted fields". That was true of fifteen hand-written rows and
/// stopped being true the moment the catalogue was distilled from CMS, whose descriptions are
/// prose and contain commas. The assumption was recorded and still went stale silently.
/// </remarks>
internal static class SeedResource
{
    public static IEnumerable<string[]> ReadRows(string logicalName)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(logicalName)
            ?? throw new InvalidOperationException(
                $"Embedded seed resource '{logicalName}' is missing from the assembly.");

        using var reader = new StreamReader(stream);

        var isHeader = true;
        while (reader.ReadLine() is { } line)
        {
            if (isHeader) { isHeader = false; continue; }
            if (line.Trim().Length == 0) continue;

            yield return SplitRow(line);
        }
    }

    /// <summary>
    /// Splits one CSV record. A quoted field may contain the delimiter, and a doubled quote inside
    /// one is a single literal quote.
    /// </summary>
    public static string[] SplitRow(string line)
    {
        var cells = new List<string>();
        var current = new StringBuilder();
        var quoted = false;

        for (var index = 0; index < line.Length; index++)
        {
            var character = line[index];

            if (quoted)
            {
                if (character != '"') { current.Append(character); continue; }

                if (index + 1 < line.Length && line[index + 1] == '"') { current.Append('"'); index++; }
                else quoted = false;
            }
            else if (character == '"') quoted = true;
            else if (character == ',') { cells.Add(current.ToString()); current.Clear(); }
            else current.Append(character);
        }

        cells.Add(current.ToString());
        return [.. cells];
    }
}
