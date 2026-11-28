using VehicleRental.ArchitectureTests.Support;

namespace VehicleRental.ArchitectureTests;

/// <summary>
/// The scanner itself: it must find every pattern it claims to find, accept the safe ones, and reject SQL that is not a
/// compile-time constant. Without these, a scanner that found nothing would make the production checks pass vacuously.
/// </summary>
public class RawDatabaseAccessScannerTests
{
    private static ScanResult Scan(string body, string members = "") => RawDatabaseAccessScanner.Scan(
        "Sample.cs",
        $$"""
        public class Sample
        {
            private const string Constant = "5s";
            {{members}}

            public async Task Run(DbContext db, DbSet<Vehicle> set, string input, string connectionString, CancellationToken token)
            {
                {{body}}
            }
        }
        """);

    [Theory]
    [InlineData("var c = db.Database.GetDbConnection();", "GetDbConnection")]
    [InlineData("var c = connection.CreateCommand();", "CreateCommand")]
    [InlineData("await command.ExecuteReaderAsync(token);", "ExecuteReaderAsync")]
    [InlineData("command.ExecuteNonQuery();", "ExecuteNonQuery")]
    [InlineData("await command.ExecuteScalarAsync(token);", "ExecuteScalarAsync")]
    [InlineData("var c = new NpgsqlConnection(connectionString);", "new NpgsqlConnection")]
    [InlineData("var c = new Npgsql.NpgsqlConnection(connectionString);", "new NpgsqlConnection")]
    [InlineData("NpgsqlConnection c = new(connectionString);", "new NpgsqlConnection")]
    [InlineData("var c = new NpgsqlCommand(\"SELECT 1\");", "new NpgsqlCommand")]
    [InlineData("var s = NpgsqlDataSource.Create(connectionString);", "NpgsqlDataSource.Create")]
    [InlineData("var b = new NpgsqlDataSourceBuilder(connectionString);", "new NpgsqlDataSourceBuilder")]
    public void Raw_connections_and_commands_are_found(string body, string what)
    {
        ScanResult result = Scan(body);

        RawAccessSite site = Assert.Single(result.Sites);
        Assert.Equal(RawAccessKind.RawConnection, site.Kind);
        Assert.Equal(what, site.What);
        Assert.Equal("Sample", site.Type);
    }

    [Theory]
    [InlineData("db.Database.ExecuteSqlRaw(\"SELECT 1\");")]
    [InlineData("await db.Database.ExecuteSqlRawAsync(\"SET LOCAL lock_timeout = '\" + Constant + \"'\", token);")]
    [InlineData("await db.Database.ExecuteSqlRawAsync(\"SET LOCAL lock_timeout = '\" + Sample.Constant + \"'\", token);")]
    [InlineData("set.FromSqlRaw(\"\"\"\n SELECT * FROM \"Vehicles\"\n \"\"\");")]
    [InlineData("db.Database.SqlQueryRaw<int>(sql: \"SELECT 1\");")]
    public void Raw_SQL_with_constant_text_is_found_but_not_a_violation(string body)
    {
        ScanResult result = Scan(body);

        Assert.Equal(RawAccessKind.RawSql, Assert.Single(result.Sites).Kind);
        Assert.Empty(result.Violations);
    }

    [Theory]
    [InlineData("db.Database.ExecuteSqlRaw($\"SELECT {input}\");")]                       // EF1002 also catches this one
    [InlineData("db.Database.ExecuteSqlRaw(\"SELECT '\" + input + \"'\");")]              // EF1003 also catches this one
    [InlineData("string sql = \"SELECT \" + input; db.Database.ExecuteSqlRaw(sql);")]   // the analyzers do not
    [InlineData("db.Database.ExecuteSqlRaw(string.Format(\"SELECT {0}\", input));")]     // the analyzers do not
    [InlineData("db.Database.ExecuteSqlRaw(new StringBuilder(\"SELECT \").Append(input).ToString());")]
    [InlineData("db.Database.ExecuteSqlRaw(sql: input);")]
    [InlineData("set.FromSqlRaw(input);")]
    [InlineData("db.Database.SqlQueryRaw<int>(input);")]
    [InlineData("var c = new NpgsqlCommand(input);")]
    [InlineData("command.CommandText = input;")]
    [InlineData("var c = new NpgsqlCommand { CommandText = \"SELECT \" + input };")]
    public void SQL_text_that_is_not_a_compile_time_constant_is_a_violation(string body)
    {
        ScanResult result = Scan(body);

        Assert.NotEmpty(result.Violations);
    }

    [Theory]
    [InlineData("await db.Database.ExecuteSqlAsync($\"SELECT * FROM \\\"Vehicles\\\" WHERE \\\"Id\\\" = {input}\", token);")]
    [InlineData("set.FromSql($\"SELECT * FROM \\\"Vehicles\\\" WHERE \\\"Id\\\" = {input}\");")]
    [InlineData("db.Database.SqlQuery<int>($\"SELECT {input}\");")]
    public void Parameterising_APIs_given_an_interpolated_string_are_accepted(string body)
    {
        ScanResult result = Scan(body);

        Assert.Empty(result.Sites);
        Assert.Empty(result.Violations);
    }

    [Theory]
    [InlineData("FormattableString query = $\"SELECT {input}\"; db.Database.ExecuteSql(query);")]
    [InlineData("db.Database.ExecuteSql(FormattableStringFactory.Create(input));")]
    [InlineData("set.FromSql(Build(input));")]
    public void Parameterising_APIs_given_anything_but_an_interpolated_string_are_a_violation(string body)
    {
        ScanResult result = Scan(body);

        Assert.NotEmpty(result.Violations);
    }

    [Fact]
    public void A_helper_that_takes_SQL_is_accepted_only_when_every_call_passes_a_constant()
    {
        const string Helper = """
            private static object? Scalar(DbConnection connection, string sql)
            {
                using DbCommand command = connection.CreateCommand();
                command.CommandText = sql;
                return command.ExecuteScalar();
            }
            """;

        ScanResult safe = Scan("Scalar(null!, \"SELECT 1\"); Scalar(null!, \"\"\"\n SELECT 2\n \"\"\");", Helper);
        ScanResult unsafeCall = Scan("Scalar(null!, \"SELECT 1\"); Scalar(null!, input);", Helper);

        Assert.Empty(safe.Violations);
        SqlTextViolation violation = Assert.Single(unsafeCall.Violations);
        Assert.Contains("Scalar(sql)", violation.What, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("public")]
    [InlineData("internal")]
    [InlineData("protected")]
    public void A_helper_visible_outside_its_file_is_never_trusted_with_SQL(string visibility)
    {
        string helper = $$"""
            {{visibility}} static object? Scalar(DbConnection connection, string sql)
            {
                using DbCommand command = connection.CreateCommand();
                command.CommandText = sql;
                return command.ExecuteScalar();
            }
            """;

        // Even when every call in this file passes a constant: another file could pass anything.
        ScanResult result = Scan("Scalar(null!, \"SELECT 1\");", helper);

        Assert.NotEmpty(result.Violations);
    }

    [Fact]
    public void A_private_helper_that_is_never_called_here_is_not_trusted_either()
    {
        ScanResult result = Scan(
            string.Empty,
            "private static void Run(DbConnection connection, string sql) { using DbCommand command = connection.CreateCommand(); command.CommandText = sql; }");

        Assert.NotEmpty(result.Violations);
    }

    [Fact]
    public void Access_is_attributed_to_the_innermost_type()
    {
        ScanResult result = RawDatabaseAccessScanner.Scan("Sample.cs", """
            public class Outer
            {
                private sealed class Inner
                {
                    public void Run(DbContext db) => db.Database.GetDbConnection();
                }
            }
            """);

        Assert.Equal("Inner", Assert.Single(result.Sites).Type);
    }
}
