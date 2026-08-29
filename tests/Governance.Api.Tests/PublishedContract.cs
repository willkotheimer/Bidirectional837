using System.Text.Json;

namespace Governance.Api.Tests;

/// <summary>
/// Reads the authored OpenAPI contract at docs/api/swagger.json. Governance Section 4 requires the
/// specification to be fully defined before the logic beneath it, so the committed document - not
/// the running application - is the source of truth these tests measure against.
/// </summary>
public static class PublishedContract
{
    public static readonly string RepoRoot = FindRepoRoot();

    private static readonly Lazy<JsonDocument> Document = new(() =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoRoot, "docs", "api", "swagger.json"))));

    /// <summary>Every (method, path) operation the contract publishes.</summary>
    public static IEnumerable<object[]> Operations()
    {
        foreach (var path in Document.Value.RootElement.GetProperty("paths").EnumerateObject())
        {
            foreach (var operation in path.Value.EnumerateObject())
            {
                yield return [operation.Name.ToUpperInvariant(), path.Name];
            }
        }
    }

    /// <summary>Every path the contract publishes.</summary>
    public static IEnumerable<object[]> Paths() =>
        Document.Value.RootElement.GetProperty("paths").EnumerateObject()
            .Select(path => new object[] { path.Name });

    public static HashSet<string> PathSet() =>
        Document.Value.RootElement.GetProperty("paths").EnumerateObject()
            .Select(path => path.Name)
            .ToHashSet();

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
