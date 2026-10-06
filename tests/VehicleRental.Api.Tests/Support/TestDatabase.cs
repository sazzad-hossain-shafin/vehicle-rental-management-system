namespace VehicleRental.Api.Tests.Support;

/// <summary>
/// Where the PostgreSQL-backed API tests find their server. They run only when the environment
/// variable below holds a connection string for a server on which the user may create and drop
/// databases. Each test run creates its own uniquely named databases and drops them afterwards.
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

/// <summary>A data-driven test that needs PostgreSQL; skipped, with the reason, when none is configured.</summary>
public sealed class DatabaseTheoryAttribute : TheoryAttribute
{
    public DatabaseTheoryAttribute()
    {
        if (!TestDatabase.IsConfigured)
        {
            Skip = $"Needs PostgreSQL: set the {TestDatabase.ConnectionStringVariable} environment variable (see the README).";
        }
    }
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
