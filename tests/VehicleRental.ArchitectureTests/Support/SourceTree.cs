namespace VehicleRental.ArchitectureTests.Support;

/// <summary>The production C# sources of this repository, found from the test's own location.</summary>
public static class SourceTree
{
    private const string SolutionFile = "VehicleRentalManagementSystem.slnx";

    public static string Root { get; } = FindRoot();

    /// <summary>
    /// Every <c>.cs</c> file under <c>src/</c>, as repository-relative paths with forward slashes. Build output and EF Core
    /// migrations are excluded: migrations run as the table owner, never as the application, and are reviewed as migrations.
    /// </summary>
    public static IReadOnlyList<string> ProductionFiles()
    {
        string src = Path.Combine(Root, "src");

        return Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Select(Relative)
            .Where(path => !path.Contains("/bin/", StringComparison.Ordinal)
                && !path.Contains("/obj/", StringComparison.Ordinal)
                && !path.Contains("/Migrations/", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    public static string Read(string relativePath) => File.ReadAllText(Path.Combine(Root, relativePath));

    private static string Relative(string fullPath) => Path.GetRelativePath(Root, fullPath).Replace('\\', '/');

    private static string FindRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, SolutionFile)))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException($"Could not find {SolutionFile} above {AppContext.BaseDirectory}.");
    }
}
