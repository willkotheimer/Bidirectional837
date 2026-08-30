using System.Text.Json;
using System.Text.RegularExpressions;
using Translator.Domain.Validation;

namespace Translator.Generation.Tests;

/// <summary>
/// PROVENANCE: FIND-005, FIND-008 - the inherited seed tooling names data files it does not have,
/// and hides their absence behind an existence check, so a seeding run reports success having
/// loaded nothing. These Theories hold the seed corpus to what the tooling claims of it.
/// </summary>
public class SeedIntegrityTheories
{
    private static readonly string RepoRoot = FindRepoRoot();

    /// <summary>
    /// Every seed file the Python loader references. Read from the loader itself rather than
    /// hard-coded, so adding a reference there without adding the file breaks this Theory.
    /// </summary>
    public static IEnumerable<object[]> SeedFilesReferencedByTheLoader()
    {
        var loader = File.ReadAllText(Path.Combine(RepoRoot, "scripts", "load_to_sqlite.py"));

        foreach (Match match in Regex.Matches(loader, @"SEED\s*/\s*'([^']+)'"))
        {
            yield return [match.Groups[1].Value];
        }
    }

    /// <summary>Seed files holding provider records, whose NPIs must satisfy the check digit.</summary>
    public static IEnumerable<object[]> ProviderSeedFiles() =>
    [
        ["providers_sample.json"],
    ];

    /// <summary>
    /// FIND-005: the loader guards every load with an existence check, so a file named there and
    /// missing from disk is skipped in silence and its table is created empty.
    /// </summary>
    [Theory]
    [MemberData(nameof(SeedFilesReferencedByTheLoader))]
    public void Seed_file_referenced_by_the_loader_exists(string fileName)
    {
        var path = Path.Combine(RepoRoot, "seed", fileName);

        Assert.True(File.Exists(path),
            $"scripts/load_to_sqlite.py loads seed/{fileName}, which is not in the repository. " +
            "The loader skips a missing file silently, so the table it feeds is created empty and " +
            "the run still reports success.");
    }

    [Theory]
    [MemberData(nameof(SeedFilesReferencedByTheLoader))]
    public void Seed_file_referenced_by_the_loader_holds_more_than_a_header(string fileName)
    {
        var path = Path.Combine(RepoRoot, "seed", fileName);
        Assert.True(File.Exists(path), $"seed/{fileName} is missing.");

        var lines = File.ReadAllLines(path).Where(line => line.Trim().Length > 0).ToList();

        Assert.True(lines.Count > 1, $"seed/{fileName} carries no rows beneath its header.");
    }

    /// <summary>
    /// FIND-008: an NPI is not merely ten digits. The inherited provider sample carried one that
    /// fails the check digit, which would have been copied into generated claims as a valid-looking
    /// identifier that no clearinghouse would accept.
    /// </summary>
    [Theory]
    [MemberData(nameof(ProviderSeedFiles))]
    public void Every_npi_in_a_provider_seed_file_satisfies_the_check_digit(string fileName)
    {
        var path = Path.Combine(RepoRoot, "seed", fileName);
        using var document = JsonDocument.Parse(File.ReadAllText(path));

        var invalid = document.RootElement.EnumerateArray()
            .Select(provider => provider.GetProperty("number").GetString())
            .Where(npi => !NationalProviderIdentifier.IsValid(npi))
            .ToList();

        Assert.True(invalid.Count == 0,
            $"seed/{fileName} carries NPI(s) {string.Join(", ", invalid)} that fail the NPI check digit.");
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "governance.txt")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
