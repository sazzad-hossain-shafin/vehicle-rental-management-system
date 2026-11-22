using System.Reflection;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using VehicleRental.Application;
using VehicleRental.Application.Tenancy;
using VehicleRental.Domain.Entities;
using VehicleRental.Infrastructure.Persistence;

namespace VehicleRental.Infrastructure.IntegrationTests.Support;

/// <summary>
/// The Phase 14A-4 test environment, one disposable database per test class: migrated as the owner (the test server's
/// admin role), the restricted runtime role provisioned with the SAME SQL file Docker Compose runs, and optionally the
/// PREVIEW Row-Level Security policies applied (never anywhere but here). Nothing touches a development database.
/// </summary>
public class TenantTestDatabase : IAsyncLifetime
{
    private const string Prefix = "vehiclerental_it_tenant_";

    private string? _server;
    private string? _database;

    /// <summary>Apply the preview Row-Level Security policies to the three tenant tables (default). Set false for role tests.</summary>
    public bool PreviewRls { get; init; } = true;

    public string AppUser { get; private set; } = "";

    private string AppPassword { get; set; } = "";

    public async Task InitializeAsync()
    {
        _server = TestDatabase.ServerConnectionString;

        if (_server is null)
        {
            return; // every database test is skipped
        }

        string id = Guid.NewGuid().ToString("N")[..12];
        _database = Prefix + id;
        AppUser = "vr_it_app_" + id;
        AppPassword = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)); // 32 characters

        await using (var admin = new NpgsqlConnection(WithDatabase(_server, "postgres")))
        {
            await admin.OpenAsync();
            await Execute(admin, $"CREATE DATABASE \"{_database}\"");
        }

        await using (var context = new VehicleRentalDbContext(VehicleRentalDbContextOptions.Create(OwnerConnectionString)))
        {
            await context.Database.MigrateAsync();
        }

        await ProvisionRuntimeRoleAsync(AppUser, AppPassword);

        if (PreviewRls)
        {
            await using var owner = new NpgsqlConnection(OwnerConnectionString);
            await owner.OpenAsync();
            await Execute(owner, ReadResource("preview-rls.sql"));
        }
    }

    public async Task DisposeAsync()
    {
        if (_server is null || _database is null || !_database.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return;
        }

        NpgsqlConnection.ClearAllPools();

        await using var admin = new NpgsqlConnection(WithDatabase(_server, "postgres"));
        await admin.OpenAsync();
        await Execute(admin, $"DROP DATABASE IF EXISTS \"{_database}\" WITH (FORCE)");
        await Execute(admin, $"DROP ROLE IF EXISTS \"{AppUser}\"");
    }

    public string OwnerConnectionString => WithDatabase(_server!, _database!);

    /// <summary>The restricted runtime role's connection string. <paramref name="extra"/> adds pool settings and a unique application name.</summary>
    public string AppConnectionString(string? extra = null)
    {
        string text = new NpgsqlConnectionStringBuilder(OwnerConnectionString) { Username = AppUser, Password = AppPassword }.ConnectionString;

        return string.IsNullOrEmpty(extra) ? text : text + ";" + extra;
    }

    /// <summary>A connection string with its own pool (the pool is keyed by the string, so a unique name gives each test one).</summary>
    public string AppPool(int maxPoolSize, bool noResetOnClose = false) =>
        AppConnectionString(
            $"Application Name=t-{Guid.NewGuid():N};Minimum Pool Size=0;Maximum Pool Size={maxPoolSize};Timeout=60;Command Timeout=60"
            + (noResetOnClose ? ";No Reset On Close=true" : ""));

    /// <summary>Runs the provisioning SQL (the single source Compose also runs) as the owner, with the given inputs.</summary>
    public async Task ProvisionRuntimeRoleAsync(string appUser, string appPassword, string? ownerConnectionString = null)
    {
        await using var owner = new NpgsqlConnection(ownerConnectionString ?? OwnerConnectionString);
        await owner.OpenAsync();
        await Execute(owner, $"SELECT set_config('provision.app_user', '{appUser}', false), set_config('provision.app_password', '{appPassword}', false)");
        await Execute(owner, ReadResource("provision-runtime-role.sql"));
    }

    public async Task<Guid> NewCompanyAsync(string? slug = null, string status = "Active")
    {
        Guid id = Guid.NewGuid();
        slug ??= "c" + id.ToString("N")[..12];

        await using var owner = new NpgsqlConnection(OwnerConnectionString);
        await owner.OpenAsync();
        await using var command = new NpgsqlCommand(
            "INSERT INTO \"Companies\" (\"Id\", \"Slug\", \"Name\", \"Status\", \"TimeZoneId\", \"CreatedAt\", \"ApprovedAt\") VALUES (@id, @slug, @name, @status, 'UTC', now(), @approved)",
            owner);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("slug", slug);
        command.Parameters.AddWithValue("name", "Company " + slug);
        command.Parameters.AddWithValue("status", status);
        command.Parameters.AddWithValue("approved", status == "PendingReview" ? DBNull.Value : DateTimeOffset.UtcNow);
        await command.ExecuteNonQueryAsync();

        return id;
    }

    /// <summary>The application's own services, wired as the web application wires them, but connected as the runtime role.</summary>
    public ServiceProvider BuildServices(string? appConnectionString = null, bool tenancyEnabled = true)
    {
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:VehicleRentalDatabase"] = appConnectionString ?? AppPool(maxPoolSize: 8),
            ["Tenancy:Enabled"] = tenancyEnabled ? "true" : "false",

            // Random and in memory only: the account services build tokens, so the token options must validate.
            ["Jwt:Issuer"] = "VehicleRental.IntegrationTests",
            ["Jwt:Audience"] = "VehicleRental.IntegrationTests.Clients",
            ["Jwt:SigningKey"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)),
        };

        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(configuration);
        services.AddApplication();
        services.AddInfrastructure();

        // Scope validation on: it would reveal a scoped service wrongly captured by a singleton (such as an interceptor).
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = false });
    }

    public Task<T> RunAsTenantAsync<T>(ServiceProvider services, Guid company, Func<IServiceProvider, Task<T>> work) =>
        services.GetRequiredService<ITenantScopeRunner>().RunAsync(company, (provider, _) => work(provider));

    /// <summary>Counts rows as the owner (a superuser, exempt from Row-Level Security) to check what really reached the database.</summary>
    public async Task<long> OwnerCountAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var owner = new NpgsqlConnection(OwnerConnectionString);
        await owner.OpenAsync();
        await using var command = new NpgsqlCommand(sql, owner);

        foreach ((string name, object value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        return (long)(await command.ExecuteScalarAsync())!;
    }

    public async Task<Guid> AddCustomerAsync(string number = "T-1")
    {
        var customer = new Customer(number + "-" + Guid.NewGuid().ToString("N")[..6], "Customer " + number);
        await using var context = new VehicleRentalDbContext(VehicleRentalDbContextOptions.Create(OwnerConnectionString));
        context.Customers.Add(customer);
        await context.SaveChangesAsync();

        return customer.Id;
    }

    public static async Task Execute(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    public static string ReadResource(string name)
    {
        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
                              ?? throw new InvalidOperationException($"{name} is not embedded.");
        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }

    private static string WithDatabase(string connectionString, string database) =>
        new NpgsqlConnectionStringBuilder(connectionString) { Database = database }.ConnectionString;
}
