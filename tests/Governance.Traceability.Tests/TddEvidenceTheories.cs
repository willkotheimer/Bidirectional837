using System.Text.RegularExpressions;

namespace Governance.Traceability.Tests;

/// <summary>
/// PROVENANCE: ADR-020 - the raw console output of each test run is no longer versioned, so
/// docs/TDD-EVIDENCE.md is now the repository's record of the governance Section 4 protocol. A
/// summary nobody checks is a summary that drifts, and this one carries the evidence for the
/// strongest control in the build, so it is enforced rather than trusted.
///
/// Governance Section 4: "Service implementations that pass tests on their first build without a
/// recorded failing test run in CI are strictly flagged as governance violations." The Theories
/// below fail the build on exactly that: a section whose recorded RED run did not fail, a section
/// whose GREEN run did not pass, or a section that reached the registers without recording a run
/// at all.
/// </summary>
public class TddEvidenceTheories
{
    private static readonly Regex EvidenceRow = new(@"^\|\s*(\d+[a-z]?)\s*\|", RegexOptions.Compiled);
    private static readonly Regex RegisterSection = new(@"^\|\s*\[(?:ADR|FIND)-\d{3}\]", RegexOptions.Compiled);
    private static readonly Regex CommitId = new("^`[0-9a-f]{7,40}`$", RegexOptions.Compiled);

    private static readonly string SummaryPath = Path.Combine("docs", "TDD-EVIDENCE.md");

    /// <summary>One recorded run pair: what the RED run did, and what the GREEN run did.</summary>
    public sealed record EvidenceRecord(
        string Section,
        int RedFailed,
        int RedPassed,
        int GreenPassed,
        int GreenFailed,
        string RedCommit);

    public static IEnumerable<object[]> RecordedSections() =>
        ReadSummary().Select(record => new object[] { record.Section });

    [Theory]
    [MemberData(nameof(RecordedSections))]
    public void Recorded_red_run_was_observed_failing(string section)
    {
        var record = Find(section);

        Assert.True(record.RedFailed > 0,
            $"Section {section} records a RED run with no failures. Governance Section 4 requires " +
            "that tests be observed failing before the implementation exists; a RED run that passed " +
            "is not evidence of that, it is the violation the clause names.");
    }

    [Theory]
    [MemberData(nameof(RecordedSections))]
    public void Recorded_green_run_passed_completely(string section)
    {
        var record = Find(section);

        Assert.True(record.GreenFailed == 0,
            $"Section {section} records a GREEN run with {record.GreenFailed} failures. A section is " +
            "not delivered while its own suite is red.");
    }

    /// <summary>
    /// The suite may grow between the two runs - a register row or a new source file adds Theory
    /// cases - but it must never shrink. A GREEN run smaller than the RED run it answers is a run
    /// against fewer tests, which is the cheapest way to turn a failing suite green.
    /// </summary>
    [Theory]
    [MemberData(nameof(RecordedSections))]
    public void Suite_did_not_shrink_between_the_red_run_and_the_green_run(string section)
    {
        var record = Find(section);
        var redTotal = record.RedFailed + record.RedPassed;

        Assert.True(record.GreenPassed >= redTotal,
            $"Section {section} ran {redTotal} tests at RED and {record.GreenPassed} at GREEN. " +
            "Tests were removed rather than made to pass.");
    }

    /// <summary>
    /// PROVENANCE: FIND-013 - the RED commit is recorded and the GREEN commit is not, because only
    /// one of the two can be. A row is written into the commit that turns its own section green, so
    /// that commit cannot carry its own hash. The RED commit is in any case the one governance
    /// Section 4 asks for: it is the commit whose tree was observed failing.
    /// </summary>
    [Theory]
    [MemberData(nameof(RecordedSections))]
    public void Row_names_the_commit_whose_tree_was_observed_failing(string section)
    {
        Assert.Matches(CommitId, Find(section).RedCommit);
    }

    /// <summary>
    /// The check that makes the rest of them load-bearing. A section can only escape the Theories
    /// above by having no row at all, so every section the decision and findings registers know
    /// about must appear here. This is what the deleted run logs used to prove by existing.
    /// </summary>
    [Fact]
    public void Every_section_named_in_the_registers_has_a_recorded_run()
    {
        var recorded = ReadSummary().Select(record => record.Section).ToHashSet();

        var claimed = SectionsNamedIn(Path.Combine("docs", "DECISIONS.md"))
            .Concat(SectionsNamedIn(Path.Combine("docs", "FINDINGS.md")))
            .ToHashSet();

        var missing = claimed.Where(section => !recorded.Contains(section)).Order().ToList();

        Assert.True(missing.Count == 0,
            $"Section(s) {string.Join(", ", missing)} appear in the registers but record no test run " +
            $"in {SummaryPath}. A section delivered without a recorded RED run is the governance " +
            "Section 4 violation, whether or not the run happened.");
    }

    private static EvidenceRecord Find(string section) =>
        ReadSummary().Single(record => record.Section == section);

    /// <summary>
    /// Reads the summary table. The columns are read by their heading rather than by position, so
    /// a reordered or extended table is a documentation change rather than a broken build.
    /// </summary>
    private static List<EvidenceRecord> ReadSummary()
    {
        var lines = File.ReadAllLines(Path.Combine(RepositoryRoot.Path, SummaryPath));
        var headings = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var records = new List<EvidenceRecord>();

        foreach (var line in lines)
        {
            var cells = Cells(line);

            if (headings.Count == 0 && cells.Contains("Section", StringComparer.OrdinalIgnoreCase))
            {
                for (var index = 0; index < cells.Count; index++) headings[cells[index]] = index;
                continue;
            }

            if (headings.Count == 0 || !EvidenceRow.IsMatch(line)) continue;

            records.Add(new EvidenceRecord(
                cells[headings["Section"]],
                Count(cells[headings["RED failed"]]),
                Count(cells[headings["RED passed"]]),
                Count(cells[headings["GREEN passed"]]),
                Count(cells[headings["GREEN failed"]]),
                cells[headings["RED commit"]]));
        }

        Assert.True(records.Count > 0, $"{SummaryPath} contains no parseable evidence rows.");
        return records;
    }

    /// <summary>Every distinct value of the Section column of a register table.</summary>
    private static IEnumerable<string> SectionsNamedIn(string relativePath) =>
        File.ReadAllLines(Path.Combine(RepositoryRoot.Path, relativePath))
            .Where(line => RegisterSection.IsMatch(line))
            .Select(line => Cells(line)[^3])   // the Section column precedes the enforced marker column
            .Distinct();

    private static List<string> Cells(string line) =>
        line.Split('|', StringSplitOptions.TrimEntries).ToList();

    private static int Count(string cell) =>
        int.TryParse(cell, out var value)
            ? value
            : throw new FormatException($"'{cell}' is not a test count.");
}
