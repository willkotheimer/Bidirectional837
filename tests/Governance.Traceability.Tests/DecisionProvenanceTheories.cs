using System.Text.RegularExpressions;

namespace Governance.Traceability.Tests;

/// <summary>
/// PROVENANCE: ADR-008 - decision provenance is marked in code and the marking is enforced here.
///
/// A decision register nobody can trace back to the code it governs decays into a document nobody
/// reads. These Theories close both directions of that loop: a required decision cannot go unmarked
/// in the source, and a marker cannot outlive the decision it cites.
/// </summary>
public class DecisionProvenanceTheories
{
    private static readonly Regex AdrMarker = new(@"PROVENANCE:\s*(ADR-\d{3})", RegexOptions.Compiled);
    private static readonly Regex GovernanceMarker = new(@"PROVENANCE:\s*GOVERNANCE-(\d+)", RegexOptions.Compiled);
    private static readonly Regex RegisterRow = new(@"^\|\s*\[(ADR-\d{3})\]", RegexOptions.Compiled);

    /// <summary>Governance.txt is numbered 1 to 5; a marker citing anything else cites nothing.</summary>
    private const int GovernedSectionCount = 5;

    private static readonly string RepoRoot = FindRepoRoot();

    /// <summary>Every decision the register marks as code-bearing.</summary>
    public static IEnumerable<object[]> DecisionsRequiringAMarker() =>
        ReadRegister()
            .Where(entry => entry.Value.Equals("required", StringComparison.OrdinalIgnoreCase))
            .Select(entry => new object[] { entry.Key });

    /// <summary>Every source file that could carry a marker. Never empty, so the Theory never vacuates.</summary>
    public static IEnumerable<object[]> SourceFiles() =>
        EnumerateSourceFiles().Select(path => new object[] { Path.GetRelativePath(RepoRoot, path) });

    [Theory]
    [MemberData(nameof(DecisionsRequiringAMarker))]
    public void Decision_marked_as_code_bearing_appears_in_the_source(string decisionId)
    {
        var marked = EnumerateSourceFiles()
            .Where(path => AdrMarker.Matches(File.ReadAllText(path)).Any(m => m.Groups[1].Value == decisionId))
            .Select(path => Path.GetRelativePath(RepoRoot, path))
            .ToList();

        Assert.True(marked.Count > 0,
            $"{decisionId} is marked 'required' in docs/DECISIONS.md but no source file carries " +
            $"a 'PROVENANCE: {decisionId}' comment. Either mark the code that embodies the decision, " +
            "or change the register's Code marker column to 'not applicable'.");
    }

    [Theory]
    [MemberData(nameof(SourceFiles))]
    public void Decision_markers_in_a_source_file_resolve_to_the_register(string relativePath)
    {
        var registered = ReadRegister().Keys.ToHashSet();
        var cited = AdrMarker.Matches(File.ReadAllText(Path.Combine(RepoRoot, relativePath)))
            .Select(match => match.Groups[1].Value)
            .Distinct()
            .ToList();

        var dangling = cited.Where(id => !registered.Contains(id)).ToList();

        Assert.True(dangling.Count == 0,
            $"{relativePath} cites {string.Join(", ", dangling)}, which docs/DECISIONS.md does not define. " +
            "A marker must not outlive the decision it cites.");
    }

    [Theory]
    [MemberData(nameof(SourceFiles))]
    public void Governance_markers_in_a_source_file_name_a_real_governed_section(string relativePath)
    {
        var cited = GovernanceMarker.Matches(File.ReadAllText(Path.Combine(RepoRoot, relativePath)))
            .Select(match => int.Parse(match.Groups[1].Value))
            .Distinct()
            .ToList();

        var outOfRange = cited.Where(section => section < 1 || section > GovernedSectionCount).ToList();

        Assert.True(outOfRange.Count == 0,
            $"{relativePath} cites governance section(s) {string.Join(", ", outOfRange)}; " +
            $"governance.txt has sections 1 to {GovernedSectionCount}.");
    }

    private static IEnumerable<string> EnumerateSourceFiles()
    {
        foreach (var directory in new[] { "src", "tests", "scripts", "infra" })
        {
            var absolute = Path.Combine(RepoRoot, directory);
            if (!Directory.Exists(absolute)) continue;

            foreach (var path in Directory.EnumerateFiles(absolute, "*.*", SearchOption.AllDirectories))
            {
                var extension = Path.GetExtension(path);
                if (extension is not (".cs" or ".py" or ".bicep")) continue;
                if (path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")) continue;
                if (path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) continue;
                yield return path;
            }
        }
    }

    /// <summary>Reads the register table: decision id to the value of its Code marker column.</summary>
    private static Dictionary<string, string> ReadRegister()
    {
        var register = new Dictionary<string, string>();

        foreach (var line in File.ReadAllLines(Path.Combine(RepoRoot, "docs", "DECISIONS.md")))
        {
            var match = RegisterRow.Match(line);
            if (!match.Success) continue;

            var cells = line.Split('|', StringSplitOptions.TrimEntries);
            // Cells are: leading empty, id, decision, status, section, code marker, trailing empty.
            register[match.Groups[1].Value] = cells[^2];
        }

        Assert.True(register.Count > 0, "docs/DECISIONS.md contains no parseable register rows.");
        return register;
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "governance.txt")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory.FullName;
    }
}
