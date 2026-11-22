using System.Reflection;
using System.Security.Cryptography;
using Npgsql;

namespace VehicleRental.Api.Tests.Support;

/// <summary>
/// Optional "runtime role" mode for the database-backed tests: set VEHICLERENTAL_TEST_RUNTIME_ROLE=1 and the application
/// side of a test connects as the restricted runtime role (provisioned with the very SQL Docker Compose runs) while the
/// migrations, the cleanup between tests and any DDL still use the owner. CI runs the booking and authentication suites
/// both ways, so the restricted role is proven to be enough for everything the API does.
/// </summary>
internal static class RuntimeRole
{
    public const string Variable = "VEHICLERENTAL_TEST_RUNTIME_ROLE";

    public static bool Enabled => Environment.GetEnvironmentVariable(Variable) == "1";

    /// <summary>Creates the role in the given database with a random password and returns the connection string to use as it.</summary>
    public static async Task<(string User, string ConnectionString)> ProvisionAsync(string ownerConnectionString)
    {
        string user = "vr_it_rt_" + Guid.NewGuid().ToString("N")[..12];
        string password = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));

        await using (var owner = new NpgsqlConnection(ownerConnectionString))
        {
            await owner.OpenAsync();

            await using (var inputs = new NpgsqlCommand("SELECT set_config('provision.app_user', @u, false), set_config('provision.app_password', @p, false)", owner))
            {
                inputs.Parameters.AddWithValue("u", user);
                inputs.Parameters.AddWithValue("p", password);
                await inputs.ExecuteNonQueryAsync();
            }

            await using var script = new NpgsqlCommand(ReadScript(), owner);
            await script.ExecuteNonQueryAsync();
        }

        string connection = new NpgsqlConnectionStringBuilder(ownerConnectionString) { Username = user, Password = password }.ConnectionString;

        return (user, connection);
    }

    public static async Task DropAsync(string serverConnectionString, string user)
    {
        await using var admin = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(serverConnectionString) { Database = "postgres" }.ConnectionString);
        await admin.OpenAsync();
        await using var drop = new NpgsqlCommand($"DROP ROLE IF EXISTS \"{user}\"", admin);
        await drop.ExecuteNonQueryAsync();
    }

    private static string ReadScript()
    {
        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("provision-runtime-role.sql")
                              ?? throw new InvalidOperationException("provision-runtime-role.sql is not embedded.");
        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }
}
