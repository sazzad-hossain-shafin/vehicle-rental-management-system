using System.Text.RegularExpressions;
using System.Xml.Linq;
using VehicleRental.ArchitectureTests.Support;

namespace VehicleRental.ArchitectureTests;

/// <summary>
/// EF Core's SQL-injection analyzers (EF1002: interpolation, EF1003: concatenation, passed to a Raw SQL method) are errors
/// in every production build, and nothing under src/ switches them off. The behaviour of the analyzers themselves belongs
/// to EF Core; what these tests protect is this repository's configuration of them.
/// </summary>
public partial class SqlInjectionAnalyzerTests
{
    private static readonly string[] Rules = ["EF1002", "EF1003"];

    [Fact]
    public void The_SQL_injection_analyzers_are_errors_for_every_production_project()
    {
        XDocument props = XDocument.Load(Path.Combine(SourceTree.Root, "src", "Directory.Build.props"));

        List<string> warningsAsErrors = props.Descendants("WarningsAsErrors")
            .SelectMany(e => e.Value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToList();

        Assert.Contains("$(WarningsAsErrors)", warningsAsErrors); // extends, never replaces, other settings
        Assert.All(Rules, rule => Assert.Contains(rule, warningsAsErrors));
    }

    [Fact]
    public void Every_production_project_is_under_the_directory_that_configures_them()
    {
        // MSBuild stops at the nearest Directory.Build.props. A production project with its own (or one in between)
        // would silently skip src/Directory.Build.props unless it imported it explicitly.
        string src = Path.Combine(SourceTree.Root, "src");
        List<string> projects = Directory.EnumerateFiles(src, "*.csproj", SearchOption.AllDirectories).ToList();

        Assert.NotEmpty(projects);

        foreach (string project in projects)
        {
            for (DirectoryInfo? directory = new FileInfo(project).Directory; directory is not null && directory.FullName != src; directory = directory.Parent)
            {
                Assert.False(
                    File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")),
                    $"{directory.FullName} has its own Directory.Build.props, which would hide src/Directory.Build.props");
            }
        }
    }

    [Fact]
    public void Nothing_under_src_suppresses_them()
    {
        string src = Path.Combine(SourceTree.Root, "src");
        var problems = new List<string>();

        IEnumerable<string> files = Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(f => Path.GetExtension(f) is ".cs" or ".csproj" or ".props" or ".targets" or ".editorconfig" or ".globalconfig"
                || Path.GetFileName(f) is ".editorconfig" or ".globalconfig");

        foreach (string file in files)
        {
            string text = File.ReadAllText(file);

            foreach (string rule in Rules)
            {
                if (Suppression(rule).IsMatch(text))
                {
                    problems.Add($"{Path.GetRelativePath(SourceTree.Root, file)} suppresses {rule}");
                }
            }
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Theory]
    [InlineData("#pragma warning disable EF1002")]
    [InlineData("#pragma warning disable CS0168, EF1003")]
    [InlineData("[SuppressMessage(\"Security\", \"EF1002:Risk of vulnerability to SQL injection\")]")]
    [InlineData("<NoWarn>$(NoWarn);EF1003</NoWarn>")]
    [InlineData("dotnet_diagnostic.EF1002.severity = none")]
    [InlineData("dotnet_diagnostic.EF1003.severity = suggestion")]
    public void The_suppression_check_recognises_every_way_of_switching_them_off(string text)
    {
        Assert.Contains(Rules, rule => Suppression(rule).IsMatch(text));
    }

    [Theory]
    [InlineData("<WarningsAsErrors>$(WarningsAsErrors);EF1002;EF1003</WarningsAsErrors>")]
    [InlineData("dotnet_diagnostic.EF1002.severity = error")]
    [InlineData("// EF1002 flags interpolated strings passed to ExecuteSqlRaw")]
    public void The_suppression_check_does_not_flag_configuration_that_keeps_them_on(string text)
    {
        Assert.DoesNotContain(Rules, rule => Suppression(rule).IsMatch(text));
    }

    private static Regex Suppression(string rule) => rule switch
    {
        "EF1002" => Ef1002Suppression(),
        "EF1003" => Ef1003Suppression(),
        _ => throw new ArgumentOutOfRangeException(nameof(rule)),
    };

    // #pragma warning disable ... RULE / SuppressMessage("...", "RULE...") / <NoWarn>...RULE / severity none|silent|suggestion|warning
    [GeneratedRegex(@"#pragma\s+warning\s+disable[^\r\n]*\bEF1002\b|SuppressMessage\([^)]*""EF1002|<NoWarn>[^<]*\bEF1002\b|dotnet_diagnostic\.EF1002\.severity\s*=\s*(none|silent|suggestion|warning)")]
    private static partial Regex Ef1002Suppression();

    [GeneratedRegex(@"#pragma\s+warning\s+disable[^\r\n]*\bEF1003\b|SuppressMessage\([^)]*""EF1003|<NoWarn>[^<]*\bEF1003\b|dotnet_diagnostic\.EF1003\.severity\s*=\s*(none|silent|suggestion|warning)")]
    private static partial Regex Ef1003Suppression();
}
