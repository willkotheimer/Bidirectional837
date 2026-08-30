namespace Governance.Traceability.Tests;

/// <summary>
/// The repository root, located by walking up from the test binary until governance.txt is found.
/// </summary>
/// <remarks>
/// The traceability suite measures documents rather than objects, so it needs the working tree
/// rather than the build output. governance.txt is the marker because it is the one file the whole
/// build is downstream of.
/// </remarks>
public static class RepositoryRoot
{
    public static string Path { get; } = Locate();

    private static string Locate()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null &&
               !File.Exists(System.IO.Path.Combine(directory.FullName, "governance.txt")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException(
                $"No governance.txt above {AppContext.BaseDirectory}; the repository root cannot be located.");
    }
}
