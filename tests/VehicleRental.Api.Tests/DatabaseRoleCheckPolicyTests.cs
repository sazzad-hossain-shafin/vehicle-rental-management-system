using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using VehicleRental.Api.Http;
using VehicleRental.Infrastructure.Tenancy;

namespace VehicleRental.Api.Tests;

/// <summary>
/// Which role check mode is in force, by environment and setting. Production-safe by default: only Development may run
/// without enforcement unless an operator explicitly chooses Warn, and nothing outside Development may switch it off.
/// </summary>
public class DatabaseRoleCheckPolicyTests
{
    private static IHostEnvironment In(string name) => new HostingEnvironment { EnvironmentName = name };

    [Theory]
    [InlineData(null, "Development", DatabaseRoleCheckMode.Warn)]
    [InlineData(null, "Production", DatabaseRoleCheckMode.Enforce)]
    [InlineData(null, "Staging", DatabaseRoleCheckMode.Enforce)]
    [InlineData(null, "Testing", DatabaseRoleCheckMode.Enforce)]
    [InlineData(null, "development", DatabaseRoleCheckMode.Warn)] // environment names are case-insensitive
    [InlineData(DatabaseRoleCheckMode.Enforce, "Development", DatabaseRoleCheckMode.Enforce)]
    [InlineData(DatabaseRoleCheckMode.Enforce, "Production", DatabaseRoleCheckMode.Enforce)]
    [InlineData(DatabaseRoleCheckMode.Warn, "Production", DatabaseRoleCheckMode.Warn)] // the documented emergency setting
    [InlineData(DatabaseRoleCheckMode.Off, "Development", DatabaseRoleCheckMode.Off)]
    public void The_mode_in_force(DatabaseRoleCheckMode? configured, string environment, DatabaseRoleCheckMode expected)
    {
        Assert.Equal(expected, DatabaseRoleCheckPolicy.Resolve(configured, In(environment)));
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("Testing")]
    public void Off_is_refused_outside_Development(string environment)
    {
        var error = Assert.Throws<InvalidOperationException>(() => DatabaseRoleCheckPolicy.Resolve(DatabaseRoleCheckMode.Off, In(environment)));

        Assert.Contains($"not in '{environment}'", error.Message, StringComparison.Ordinal);
    }
}
