namespace VehicleRental.Infrastructure.IntegrationTests.Support;

/// <summary>
/// Where the PostgreSQL tests find their server. They run only when the environment variable below
/// holds a connection string for a server on which the user may create and drop databases.
/// Nothing else is ever touched: each test run creates its own uniquely named database and drops it.
/// </summary>
internal static class TestDatabase
{
    public const string ConnectionStringVariable = "VEHICLERENTAL_TEST_CONNECTION";

    public static string? ServerConnectionString
    {
        get
        {
            string? value = Environment.GetEnvironmentVariable(ConnectionStringVariable);

            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
    }

    public static bool IsConfigured => ServerConnectionString is not null;
}

/// <summary>
/// A test that needs PostgreSQL. It is reported as skipped, with the reason, when
/// <see cref="TestDatabase.ConnectionStringVariable"/> is not set, so a missing database never looks like a pass.
/// </summary>
public sealed class DatabaseFactAttribute : FactAttribute
{
    public DatabaseFactAttribute()
    {
        if (!TestDatabase.IsConfigured)
        {
            Skip = $"Needs PostgreSQL: set the {TestDatabase.ConnectionStringVariable} environment variable (see the README).";
        }
    }
}
