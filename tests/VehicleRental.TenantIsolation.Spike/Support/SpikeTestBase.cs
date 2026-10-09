using Microsoft.EntityFrameworkCore;
using Npgsql;
using VehicleRental.TenantIsolation.Spike.Tenancy;

namespace VehicleRental.TenantIsolation.Spike.Support;

[Collection(SpikeCollection.Name)]
public abstract class SpikeTestBase
{
    protected SpikeTestBase(SpikeDatabase database) => Database = database;

    protected SpikeDatabase Database { get; }

    /// <summary>
    /// A connection string with its OWN pool (the pool is keyed by the whole string, so a unique application name
    /// gives each test a private one) and the pool settings the test needs.
    /// </summary>
    protected string AppPool(int maxPoolSize, bool noResetOnClose = false)
    {
        string extra = $"Application Name=spike-{Guid.NewGuid():N};Minimum Pool Size=0;Maximum Pool Size={maxPoolSize};Timeout=60;Command Timeout=60";

        if (noResetOnClose)
        {
            // Npgsql normally runs DISCARD ALL when a connection goes back to the pool. Turning that off removes the
            // driver's safety net, so a test can prove isolation does not depend on it.
            extra += ";No Reset On Close=true";
        }

        return Database.AppConnectionString(extra);
    }

    protected async Task<(Guid Company, List<Guid> Vehicles)> SeedAsync(int vehicles = 5) =>
        await Database.SeedCompanyAsync("c" + Guid.NewGuid().ToString("N")[..10], vehicles);

    protected static async Task<NpgsqlConnection> OpenAsync(string connectionString)
    {
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        return connection;
    }

    protected static async Task<T?> ScalarAsync<T>(NpgsqlConnection connection, string sql, params (string Name, object Value)[] parameters)
    {
        await using var command = new NpgsqlCommand(sql, connection);

        foreach ((string name, object value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        object? result = await command.ExecuteScalarAsync();

        return result is null or DBNull ? default : (T)result;
    }

    protected static async Task<int> ExecuteAsync(NpgsqlConnection connection, string sql, params (string Name, object Value)[] parameters)
    {
        await using var command = new NpgsqlCommand(sql, connection);

        foreach ((string name, object value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        return await command.ExecuteNonQueryAsync();
    }

    /// <summary>Runs a raw SQL statement as the application role with a given tenant, inside a transaction (the only supported way).</summary>
    protected async Task<T?> AsTenantAsync<T>(string connectionString, Guid company, Func<NpgsqlConnection, NpgsqlTransaction, Task<T?>> work)
    {
        await using NpgsqlConnection connection = await OpenAsync(connectionString);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync();
        await SpikeDatabase.SetTenant(connection, transaction, company);

        T? result = await work(connection, transaction);
        await transaction.CommitAsync();

        return result;
    }

    protected static async Task<int> BackendPidAsync(TenantDbContext db) =>
        await db.Database.SqlQuery<int>($"SELECT pg_backend_pid() AS \"Value\"").SingleAsync();
}
