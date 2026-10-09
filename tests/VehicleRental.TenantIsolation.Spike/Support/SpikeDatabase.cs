using System.Reflection;
using System.Security.Cryptography;
using Npgsql;

namespace VehicleRental.TenantIsolation.Spike.Support;

/// <summary>
/// Creates a throwaway database and three throwaway roles, applies <c>schema.sql</c>, and removes everything afterwards.
/// Nothing here can touch the application's own database: the database and role names are generated and checked.
///
///   owner   owns the tables (and, with FORCE ROW LEVEL SECURITY, is still subject to the policies)
///   app     the role the application would use: owns nothing, no BYPASSRLS
///   bypass  a deliberately unsafe role with BYPASSRLS, used to prove the safety check notices it
/// </summary>
public sealed class SpikeDatabase : IAsyncLifetime
{
    public const string ConnectionVariable = "VEHICLERENTAL_TEST_CONNECTION";

    private const string Prefix = "vr_rls_spike_";

    private string? _server;
    private string? _database;

    public static bool IsConfigured => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ConnectionVariable));

    public string OwnerRole { get; private set; } = "";

    public string AppRole { get; private set; } = "";

    public string BypassRole { get; private set; } = "";

    private string OwnerPassword { get; set; } = "";

    private string AppPassword { get; set; } = "";

    private string BypassPassword { get; set; } = "";

    public async Task InitializeAsync()
    {
        if (!IsConfigured)
        {
            return;
        }

        _server = Environment.GetEnvironmentVariable(ConnectionVariable)!;
        string id = Guid.NewGuid().ToString("N")[..12];
        _database = Prefix + id;
        OwnerRole = Prefix + "owner_" + id;
        AppRole = Prefix + "app_" + id;
        BypassRole = Prefix + "bypass_" + id;
        OwnerPassword = RandomPassword();
        AppPassword = RandomPassword();
        BypassPassword = RandomPassword();

        await using (var admin = new NpgsqlConnection(With(_server, database: "postgres")))
        {
            await admin.OpenAsync();
            await Execute(admin, $"CREATE ROLE \"{OwnerRole}\" LOGIN NOSUPERUSER NOBYPASSRLS PASSWORD '{OwnerPassword}'");
            await Execute(admin, $"CREATE ROLE \"{AppRole}\" LOGIN NOSUPERUSER NOBYPASSRLS PASSWORD '{AppPassword}'");
            await Execute(admin, $"CREATE ROLE \"{BypassRole}\" LOGIN NOSUPERUSER BYPASSRLS PASSWORD '{BypassPassword}'");
            await Execute(admin, $"CREATE DATABASE \"{_database}\" OWNER \"{OwnerRole}\"");
        }

        // btree_gist must be created by someone allowed to; it is a trusted extension, but be explicit.
        await using (var admin = new NpgsqlConnection(With(_server, database: _database)))
        {
            await admin.OpenAsync();
            await Execute(admin, "CREATE EXTENSION IF NOT EXISTS btree_gist");
        }

        string script = ReadSchema().Replace("{app}", $"\"{AppRole}\"", StringComparison.Ordinal);

        await using (var owner = new NpgsqlConnection(OwnerConnectionString()))
        {
            await owner.OpenAsync();
            await Execute(owner, script);
        }
    }

    public async Task DisposeAsync()
    {
        if (_server is null || _database is null || !_database.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return;
        }

        NpgsqlConnection.ClearAllPools();

        await using var admin = new NpgsqlConnection(With(_server, database: "postgres"));
        await admin.OpenAsync();
        await Execute(admin, $"DROP DATABASE IF EXISTS \"{_database}\" WITH (FORCE)");
        await Execute(admin, $"DROP ROLE IF EXISTS \"{OwnerRole}\"");
        await Execute(admin, $"DROP ROLE IF EXISTS \"{AppRole}\"");
        await Execute(admin, $"DROP ROLE IF EXISTS \"{BypassRole}\"");
    }

    public string OwnerConnectionString(string? extra = null) => Build(OwnerRole, OwnerPassword, extra);

    /// <summary>The connection the application would use. <paramref name="extra"/> adds pool settings and a unique application name.</summary>
    public string AppConnectionString(string? extra = null) => Build(AppRole, AppPassword, extra);

    public string BypassConnectionString(string? extra = null) => Build(BypassRole, BypassPassword, extra);

    /// <summary>A superuser connection to the spike database, only for proving the safety check notices superusers.</summary>
    public string AdminConnectionString() => With(_server!, database: _database);

    public static async Task Execute(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>Inserts a company and some vehicles as the owner (the owner is the only role that may bypass for seeding).</summary>
    public async Task<(Guid Company, List<Guid> Vehicles)> SeedCompanyAsync(string slug, int vehicles)
    {
        Guid company = Guid.NewGuid();
        var ids = new List<Guid>();

        await using var owner = new NpgsqlConnection(OwnerConnectionString());
        await owner.OpenAsync();
        await using var transaction = await owner.BeginTransactionAsync();

        // The owner is subject to FORCE ROW LEVEL SECURITY too, so seeding sets the tenant like the application does.
        await SetTenant(owner, transaction, company);

        await using (var insert = new NpgsqlCommand(
            "INSERT INTO companies (id, slug, name, status, internal_note) VALUES (@id, @slug, @name, 'Active', 'note-' || @slug)",
            owner,
            transaction))
        {
            insert.Parameters.AddWithValue("id", company);
            insert.Parameters.AddWithValue("slug", slug);
            insert.Parameters.AddWithValue("name", "Company " + slug);
            await insert.ExecuteNonQueryAsync();
        }

        for (int i = 0; i < vehicles; i++)
        {
            Guid vehicle = Guid.NewGuid();
            ids.Add(vehicle);

            await using var insert = new NpgsqlCommand(
                "INSERT INTO vehicles (id, company_id, registration, daily_rate) VALUES (@id, @company, @reg, @rate)",
                owner,
                transaction);
            insert.Parameters.AddWithValue("id", vehicle);
            insert.Parameters.AddWithValue("company", company);
            insert.Parameters.AddWithValue("reg", $"{slug.ToUpperInvariant()}-{i:000}");
            insert.Parameters.AddWithValue("rate", 40m + i);
            await insert.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();

        return (company, ids);
    }

    public static async Task SetTenant(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid company)
    {
        await using var command = new NpgsqlCommand("SELECT set_config('app.company_id', @company, true)", connection, transaction);
        command.Parameters.AddWithValue("company", company.ToString());
        await command.ExecuteNonQueryAsync();
    }

    private string Build(string user, string password, string? extra)
    {
        var builder = new NpgsqlConnectionStringBuilder(With(_server!, database: _database))
        {
            Username = user,
            Password = password,
        };

        string text = builder.ConnectionString;

        return string.IsNullOrEmpty(extra) ? text : text + ";" + extra;
    }

    private static string With(string connectionString, string? database) =>
        new NpgsqlConnectionStringBuilder(connectionString) { Database = database }.ConnectionString;

    private static string RandomPassword() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16));

    private static string ReadSchema()
    {
        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("schema.sql")
                              ?? throw new InvalidOperationException("schema.sql is not embedded.");
        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }
}

[CollectionDefinition(Name)]
public sealed class SpikeCollection : ICollectionFixture<SpikeDatabase>
{
    public const string Name = "RlsSpike";
}

public sealed class SpikeFactAttribute : FactAttribute
{
    public SpikeFactAttribute()
    {
        if (!SpikeDatabase.IsConfigured)
        {
            Skip = $"Needs PostgreSQL: set the {SpikeDatabase.ConnectionVariable} environment variable (see this project's README).";
        }
    }
}

public sealed class SpikeTheoryAttribute : TheoryAttribute
{
    public SpikeTheoryAttribute()
    {
        if (!SpikeDatabase.IsConfigured)
        {
            Skip = $"Needs PostgreSQL: set the {SpikeDatabase.ConnectionVariable} environment variable (see this project's README).";
        }
    }
}
