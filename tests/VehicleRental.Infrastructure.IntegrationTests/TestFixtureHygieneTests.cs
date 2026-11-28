using System.Text.RegularExpressions;
using Npgsql;
using VehicleRental.Infrastructure.IntegrationTests.Support;

namespace VehicleRental.Infrastructure.IntegrationTests;

/// <summary>
/// Test classes in this assembly run in parallel in one process. A fixture's cleanup must remove everything it created
/// (sessions, database, role) and touch nothing it did not create, in particular not another test's connection pool.
/// </summary>
public partial class TestFixtureHygieneTests
{
    private static async Task<long> ServerCountAsync(string sql, string name)
    {
        await using var admin = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(TestDatabase.ServerConnectionString!) { Database = "postgres" }.ConnectionString);
        await admin.OpenAsync();
        await using var command = new NpgsqlCommand(sql, admin);
        command.Parameters.AddWithValue("name", name);

        return (long)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<int> BackendPidAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT pg_backend_pid()", connection);

        return (int)(await command.ExecuteScalarAsync())!;
    }

    [DatabaseFact]
    public async Task Disposing_a_fixture_removes_what_it_created_and_leaves_other_pools_alone()
    {
        // A bystander: another fixture with an idle pooled connection, as a test running in parallel would have.
        var bystander = new TenantTestDatabase { PreviewRls = false };
        await bystander.InitializeAsync();

        try
        {
            string bystanderPool = bystander.AppPool(maxPoolSize: 1);
            int before = await BackendPidAsync(bystanderPool);

            var fixture = new TenantTestDatabase { PreviewRls = false };
            await fixture.InitializeAsync();
            string database = new NpgsqlConnectionStringBuilder(fixture.OwnerConnectionString).Database!;
            string role = fixture.AppUser;

            // Leave idle connections behind in two of the fixture's own pools, as tests do.
            await BackendPidAsync(fixture.AppPool(maxPoolSize: 2));
            await BackendPidAsync(fixture.OwnerConnectionString);

            await fixture.DisposeAsync();

            Assert.Equal(0, await ServerCountAsync("SELECT count(*) FROM pg_stat_activity WHERE datname = @name", database));
            Assert.Equal(0, await ServerCountAsync("SELECT count(*) FROM pg_database WHERE datname = @name", database));
            Assert.Equal(0, await ServerCountAsync("SELECT count(*) FROM pg_roles WHERE rolname = @name", role));

            // The bystander's single pooled connection survived: the same backend serves it again.
            Assert.Equal(before, await BackendPidAsync(bystanderPool));
        }
        finally
        {
            await bystander.DisposeAsync();
        }
    }

    [Fact]
    public void No_code_in_this_assembly_clears_every_connection_pool()
    {
        string project = FindProjectDirectory();
        var offenders = new List<string>();

        foreach (string file in Directory.EnumerateFiles(project, "*.cs", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(project, file);

            if (relative.StartsWith("bin", StringComparison.Ordinal) || relative.StartsWith("obj", StringComparison.Ordinal))
            {
                continue;
            }

            string[] lines = File.ReadAllLines(file);

            for (int i = 0; i < lines.Length; i++)
            {
                if (!lines[i].TrimStart().StartsWith("//", StringComparison.Ordinal) && ClearAllPoolsCall().IsMatch(lines[i]))
                {
                    offenders.Add($"{relative}:{i + 1}");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "Clearing every Npgsql pool closes the pools of tests running in parallel; clear only the fixture's own pools "
            + "(OwnedConnectionPools). Found at: " + string.Join(", ", offenders));
    }

    [Theory]
    // Split so that these samples are not themselves a call the guard above would report.
    [InlineData("NpgsqlConnection.ClearAll" + "Pools();", true)]
    [InlineData("        Npgsql.NpgsqlConnection.ClearAll" + "Pools ();", true)]
    [InlineData("NpgsqlConnection.ClearPool(connection);", false)]
    public void The_guard_recognises_the_call(string line, bool expected)
    {
        Assert.Equal(expected, ClearAllPoolsCall().IsMatch(line));
    }

    private static string FindProjectDirectory()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "VehicleRental.Infrastructure.IntegrationTests.csproj")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("Could not find the test project directory.");
    }

    [GeneratedRegex(@"\bClearAllPools\s*\(")]
    private static partial Regex ClearAllPoolsCall();
}
