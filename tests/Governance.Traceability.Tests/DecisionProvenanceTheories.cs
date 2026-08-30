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
    // PROVENANCE: FIND-007 - the first scanner read only the token following the keyword, so a
    // marker citing a governed section before a decision registered the section and missed the
    // decision entirely, reporting coverage it had not verified.
    // A marker line may cite more than one decision, and may cite a governed section alongside them
    // ("PROVENANCE: GOVERNANCE-2, ADR-003"), so citations are read from the whole marker line rather
    // than from the single token following the keyword.
    private const string MarkerKeyword = "PROVENANCE:";
    private static readonly Regex AdrCitation = new(@"ADR-\d{3}", RegexOptions.Compiled);
    private static readonly Regex FindingCitation = new(@"FIND-\d{3}", RegexOptions.Compiled);
    private static readonly Regex GovernanceCitation = new(@"GOVERNANCE-(\d+)", RegexOptions.Compiled);
    private static readonly Regex DecisionRow = new(@"^\|\s*\[(ADR-\d{3})\]", RegexOptions.Compiled);
    private static readonly Regex FindingRow = new(@"^\|\s*\[(FIND-\d{3})\]", RegexOptions.Compiled);

    /// <summary>Governance.txt is numbered 1 to 5; a marker citing anything else cites nothing.</summary>
    private const int GovernedSectionCount = 5;

    private static readonly string RepoRoot = RepositoryRoot.Path;

    /// <summary>Every decision the register marks as code-bearing.</summary>
    public static IEnumerable<object[]> DecisionsRequiringAMarker() =>
        ReadRegister()
            .Where(entry => entry.Value.Equals("required", StringComparison.OrdinalIgnoreCase))
            .Select(entry => new object[] { entry.Key });

    /// <summary>Every finding the register says is held shut by a test.</summary>
    public static IEnumerable<object[]> FindingsRequiringAGuard() =>
        ReadFindings()
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
            .Where(path => CitedDecisions(path).Contains(decisionId))
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
        var cited = CitedDecisions(Path.Combine(RepoRoot, relativePath));

        var dangling = cited.Where(id => !registered.Contains(id)).ToList();

        Assert.True(dangling.Count == 0,
            $"{relativePath} cites {string.Join(", ", dangling)}, which docs/DECISIONS.md does not define. " +
            "A marker must not outlive the decision it cites.");
    }

    [Theory]
    [MemberData(nameof(SourceFiles))]
    public void Governance_markers_in_a_source_file_name_a_real_governed_section(string relativePath)
    {
        var cited = MarkerLines(Path.Combine(RepoRoot, relativePath))
            .SelectMany(line => GovernanceCitation.Matches(line).Select(m => int.Parse(m.Groups[1].Value)))
            .Distinct()
            .ToList();

        var outOfRange = cited.Where(section => section < 1 || section > GovernedSectionCount).ToList();

        Assert.True(outOfRange.Count == 0,
            $"{relativePath} cites governance section(s) {string.Join(", ", outOfRange)}; " +
            $"governance.txt has sections 1 to {GovernedSectionCount}.");
    }


    /// <summary>
    /// PROVENANCE: ADR-011 - a finding the register says is held shut by a test must name that
    /// test in the source. This is the same contract ADR-008 applies to decisions, extended to
    /// findings: a fix that is not guarded is a fix that can silently regress.
    /// </summary>
    [Theory]
    [MemberData(nameof(FindingsRequiringAGuard))]
    public void Finding_marked_as_guarded_is_cited_by_a_test(string findingId)
    {
        var guarding = EnumerateSourceFiles()
            .Where(path => CitedFindings(path).Contains(findingId))
            .Select(path => Path.GetRelativePath(RepoRoot, path))
            .ToList();

        Assert.True(guarding.Count > 0,
            $"{findingId} is marked as guarded in docs/FINDINGS.md but no source file carries a " +
            $"'PROVENANCE: {findingId}' comment. Either mark the test that holds the finding shut, " +
            "or change the register's Guard column to 'not applicable' and say why in the entry.");
    }

    [Theory]
    [MemberData(nameof(SourceFiles))]
    public void Finding_markers_in_a_source_file_resolve_to_the_register(string relativePath)
    {
        var registered = ReadFindings().Keys.ToHashSet();
        var cited = CitedFindings(Path.Combine(RepoRoot, relativePath));

        var dangling = cited.Where(id => !registered.Contains(id)).ToList();

        Assert.True(dangling.Count == 0,
            $"{relativePath} cites {string.Join(", ", dangling)}, which docs/FINDINGS.md does not " +
            "define. A marker must not outlive the finding it cites.");
    }

    /// <summary>Lines that carry a provenance marker.</summary>
    private static IEnumerable<string> MarkerLines(string absolutePath) =>
        File.ReadLines(absolutePath).Where(line => line.Contains(MarkerKeyword, StringComparison.Ordinal));

    /// <summary>Every decision cited by a marker in the file.</summary>
    private static List<string> CitedDecisions(string absolutePath) =>
        MarkerLines(absolutePath)
            .SelectMany(line => AdrCitation.Matches(line).Select(match => match.Value))
            .Distinct()
            .ToList();

    /// <summary>Every finding cited by a marker in the file.</summary>
    private static List<string> CitedFindings(string absolutePath) =>
        MarkerLines(absolutePath)
            .SelectMany(line => FindingCitation.Matches(line).Select(match => match.Value))
            .Distinct()
            .ToList();

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

    /// <summary>Decision id to the value of its Code marker column.</summary>
    private static Dictionary<string, string> ReadRegister() =>
        ReadRegister(Path.Combine("docs", "DECISIONS.md"), DecisionRow);

    /// <summary>Finding id to the value of its Guard column.</summary>
    private static Dictionary<string, string> ReadFindings() =>
        ReadRegister(Path.Combine("docs", "FINDINGS.md"), FindingRow);

    /// <summary>
    /// Reads a register table keyed by its identifier column. Both registers put the enforced
    /// marker column last, so the value read is the final cell of the row.
    /// </summary>
    private static Dictionary<string, string> ReadRegister(string relativePath, Regex rowPattern)
    {
        var register = new Dictionary<string, string>();

        foreach (var line in File.ReadAllLines(Path.Combine(RepoRoot, relativePath)))
        {
            var match = rowPattern.Match(line);
            if (!match.Success) continue;

            var cells = line.Split('|', StringSplitOptions.TrimEntries);
            register[match.Groups[1].Value] = cells[^2];
        }

        Assert.True(register.Count > 0, $"{relativePath} contains no parseable register rows.");
        return register;
    }
}
