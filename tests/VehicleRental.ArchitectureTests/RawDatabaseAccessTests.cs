using System.Text;
using VehicleRental.ArchitectureTests.Support;

namespace VehicleRental.ArchitectureTests;

/// <summary>
/// Production code may reach the database outside EF Core LINQ only where the reviewed allowlist says so, and SQL text is
/// always a compile-time constant. EF Core's own analyzers (EF1002, EF1003) catch interpolation and concatenation passed
/// straight to a raw method; these tests also catch SQL that arrives through a variable, string.Format, a helper's
/// parameter or a raw command, which the analyzers do not see.
/// </summary>
public class RawDatabaseAccessTests
{
    private static readonly Lazy<IReadOnlyList<ScanResult>> Production = new(() =>
        SourceTree.ProductionFiles().Select(file => RawDatabaseAccessScanner.Scan(file, SourceTree.Read(file))).ToList());

    [Fact]
    public void The_scan_covers_the_production_sources()
    {
        IReadOnlyList<string> files = SourceTree.ProductionFiles();

        Assert.Contains("src/VehicleRental.Infrastructure/Persistence/UnitOfWork.cs", files);
        Assert.Contains("src/VehicleRental.Api/Program.cs", files);
        Assert.Contains("src/VehicleRental.Console/Program.cs", files);
        Assert.DoesNotContain(files, f => f.Contains("/Migrations/", StringComparison.Ordinal) || f.Contains("/obj/", StringComparison.Ordinal));
    }

    [Fact]
    public void Raw_database_access_happens_only_where_the_reviewed_allowlist_allows_it()
    {
        List<RawAccessSite> sites = Production.Value.SelectMany(r => r.Sites).ToList();
        var problems = new StringBuilder();

        foreach (IGrouping<(RawAccessKind Kind, string File, string Type), RawAccessSite> group in sites.GroupBy(s => (s.Kind, s.File, s.Type)))
        {
            AllowedRawAccess? allowed = RawDatabaseAccessAllowlist.Entries.SingleOrDefault(e => (e.Kind, e.File, e.Type) == group.Key);

            if (allowed is null)
            {
                problems.AppendLine($"Not allowed: {group.Key.Kind} in {group.Key.Type} ({group.Key.File}):");
                Append(problems, group);
            }
            else if (allowed.Count != group.Count())
            {
                problems.AppendLine($"Allowed {allowed.Count}, found {group.Count()}: {group.Key.Kind} in {group.Key.Type} ({group.Key.File}). Review the change, then update the allowlist:");
                Append(problems, group);
            }
        }

        foreach (AllowedRawAccess entry in RawDatabaseAccessAllowlist.Entries)
        {
            if (!sites.Any(s => (s.Kind, s.File, s.Type) == (entry.Kind, entry.File, entry.Type)))
            {
                problems.AppendLine($"Stale allowlist entry (nothing matches it any more, remove it): {entry.Kind} in {entry.Type} ({entry.File})");
            }
        }

        Assert.True(problems.Length == 0, "Raw database access outside the reviewed allowlist (tests/VehicleRental.ArchitectureTests/RawDatabaseAccessAllowlist.cs):" + Environment.NewLine + problems);
    }

    [Fact]
    public void Production_SQL_text_is_always_a_compile_time_constant()
    {
        List<SqlTextViolation> violations = Production.Value.SelectMany(r => r.Violations).ToList();

        Assert.True(
            violations.Count == 0,
            "SQL text that is not a compile-time constant (use ExecuteSql/FromSql/SqlQuery with an interpolated string, which parameterises every value):"
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations.Select(v => $"  {v.File}:{v.Line}  {v.What}")));
    }

    [Fact]
    public void Every_allowlist_entry_is_unique_explained_and_points_at_a_real_file()
    {
        Assert.Equal(
            RawDatabaseAccessAllowlist.Entries.Count,
            RawDatabaseAccessAllowlist.Entries.Select(e => (e.Kind, e.File, e.Type)).Distinct().Count());

        Assert.All(RawDatabaseAccessAllowlist.Entries, entry =>
        {
            Assert.True(entry.Count > 0, $"{entry.Type}: the count must be positive");
            Assert.True(entry.Reason.Length >= 40, $"{entry.Type}: give a real reason");
            Assert.True(File.Exists(Path.Combine(SourceTree.Root, entry.File)), $"{entry.File} does not exist");
        });
    }

    private static void Append(StringBuilder problems, IEnumerable<RawAccessSite> sites)
    {
        foreach (RawAccessSite site in sites)
        {
            problems.AppendLine($"  {site.File}:{site.Line}  {site.What}");
        }
    }
}
