using System.Text.RegularExpressions;

namespace Governance.Traceability.Tests;

/// <summary>
/// PROVENANCE: ADR-026 - "Governance" names the guardrails, not the application.
///
/// The project owner set the rule: if the application itself is called Governance that is wrong,
/// and if Governance means the development guardrails then that is what the name should mean. This
/// suite is the guardrails, so it keeps the name. Everything that is the 837 translator takes the
/// application's own.
///
/// Enforced rather than agreed, because a root namespace is exactly the kind of thing that drifts
/// back one file at a time. Measured over the source rather than over loaded assemblies, which is
/// how the rest of this suite works and needs no reference to the projects under test.
/// </summary>
public partial class NamespaceTheories
{
    private const string ApplicationRoot = "Translator";
    private const string GuardrailRoot = "Governance";

    /// <summary>The one project that is about governance rather than being governed by it.</summary>
    private const string GuardrailProject = "Governance.Traceability.Tests";

    [GeneratedRegex(@"^\s*namespace\s+([A-Za-z0-9_.]+)", RegexOptions.Multiline)]
    private static partial Regex NamespaceDeclaration();

    /// <summary>Every C# file in the tree, with the project directory it belongs to.</summary>
    public static IEnumerable<object[]> SourceFiles()
    {
        foreach (var (project, path) in EnumerateSource())
        {
            yield return [project, Path.GetRelativePath(RepositoryRoot.Path, path)];
        }
    }

    /// <summary>Every project directory beneath src/ and tests/.</summary>
    public static IEnumerable<object[]> Projects() =>
        EnumerateProjects().Select(project => new object[] { Path.GetFileName(project) });

    [Theory]
    [MemberData(nameof(Projects))]
    public void Project_is_named_for_what_it_is(string project)
    {
        var expected = project == GuardrailProject ? GuardrailRoot : ApplicationRoot;

        Assert.True(project.StartsWith(expected + ".", StringComparison.Ordinal),
            $"Project '{project}' should sit under '{expected}'. " +
            $"'{GuardrailRoot}' names the guardrails; the application is '{ApplicationRoot}'.");
    }

    [Theory]
    [MemberData(nameof(SourceFiles))]
    public void Declared_namespace_matches_the_project_that_holds_it(string project, string relativePath)
    {
        var expected = project == GuardrailProject ? GuardrailRoot : ApplicationRoot;
        var declared = NamespaceDeclaration()
            .Matches(File.ReadAllText(Path.Combine(RepositoryRoot.Path, relativePath)))
            .Select(match => match.Groups[1].Value)
            .ToList();

        foreach (var declaration in declared)
        {
            Assert.True(
                declaration == expected || declaration.StartsWith(expected + ".", StringComparison.Ordinal),
                $"{relativePath} declares namespace '{declaration}', which is outside '{expected}'.");
        }
    }

    /// <summary>
    /// The rename must not run halfway. A file under the application root that still mentions a
    /// Governance-rooted type is a reference the rename missed, and it would compile right up until
    /// the day the old assembly stopped being produced.
    /// </summary>
    [Theory]
    [MemberData(nameof(SourceFiles))]
    public void Application_source_does_not_reference_a_governance_rooted_type(string project, string relativePath)
    {
        if (project == GuardrailProject) return;

        var text = File.ReadAllText(Path.Combine(RepositoryRoot.Path, relativePath));

        // "GOVERNANCE-4" is a provenance marker citing a governed section, not a namespace.
        var references = Regex.Matches(text, @"\bGovernance\.[A-Za-z]")
            .Select(match => match.Value)
            .ToList();

        Assert.True(references.Count == 0,
            $"{relativePath} still references {references.Count} Governance-rooted name(s); " +
            "the rename did not reach it.");
    }

    /// <summary>The solution must list every project, under whichever root it belongs to.</summary>
    [Fact]
    public void Solution_lists_every_project_under_its_proper_root()
    {
        var solution = File.ReadAllText(Path.Combine(RepositoryRoot.Path, "Bidirectional837.slnx"));

        foreach (var project in EnumerateProjects().Select(Path.GetFileName))
        {
            Assert.Contains($"{project}/{project}.csproj", solution, StringComparison.Ordinal);
        }
    }

    private static IEnumerable<string> EnumerateProjects()
    {
        foreach (var area in new[] { "src", "tests" })
        {
            var directory = Path.Combine(RepositoryRoot.Path, area);
            if (!Directory.Exists(directory)) continue;

            foreach (var project in Directory.EnumerateDirectories(directory).Order(StringComparer.Ordinal))
            {
                if (Directory.EnumerateFiles(project, "*.csproj").Any()) yield return project;
            }
        }
    }

    private static IEnumerable<(string Project, string Path)> EnumerateSource()
    {
        foreach (var project in EnumerateProjects())
        {
            foreach (var file in Directory.EnumerateFiles(project, "*.cs", SearchOption.AllDirectories))
            {
                if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")) continue;
                if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) continue;

                yield return (Path.GetFileName(project), file);
            }
        }
    }
}
