using System.Collections.Concurrent;
using Npgsql;

namespace VehicleRental.Infrastructure.IntegrationTests.Support;

/// <summary>
/// The connection pools a test fixture created, so it can close exactly those and nothing else when it is disposed.
/// <para>
/// Never use <c>NpgsqlConnection.ClearAllPools()</c> in this assembly: test classes run in parallel in one process, and
/// it closes the idle connections of EVERY pool, including those of tests that are still running. That made tests which
/// rely on one physical connection being reused (TenantPoolingTests) fail intermittently. A guard test enforces this.
/// </para>
/// <para>
/// Clearing the owned pools is client-side hygiene. The authoritative cleanup is the fixture's
/// <c>DROP DATABASE ... WITH (FORCE)</c>, which terminates every remaining session on that database, including sessions
/// from connection strings a test built for itself and never registered here.
/// </para>
/// </summary>
public sealed class OwnedConnectionPools
{
    private readonly ConcurrentDictionary<string, byte> _connectionStrings = new(StringComparer.Ordinal);

    /// <summary>Records a connection string this fixture hands out, and returns it unchanged.</summary>
    public string Track(string connectionString)
    {
        _connectionStrings.TryAdd(connectionString, 0);

        return connectionString;
    }

    /// <summary>Closes the idle connections of every recorded pool; connections still in use close when they are returned.</summary>
    public void Clear()
    {
        foreach (string connectionString in _connectionStrings.Keys)
        {
            using var connection = new NpgsqlConnection(connectionString);
            NpgsqlConnection.ClearPool(connection);
        }
    }
}
