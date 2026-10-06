using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace VehicleRental.Infrastructure;

/// <summary>
/// Readiness check: healthy only when the database is reachable and every migration has been applied.
/// The descriptions are safe to show to anyone; they never include connection details.
/// </summary>
public sealed class DatabaseHealthCheck : IHealthCheck
{
    private readonly IServiceProvider _services;

    public DatabaseHealthCheck(IServiceProvider services) => _services = services;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        DatabaseStatus status;

        try
        {
            // Resolved here, not in the constructor, so that a missing connection string is reported as
            // an unhealthy database instead of failing before the check can report anything.
            var checker = _services.GetRequiredService<DatabaseStatusChecker>();
            status = await checker.CheckAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (InvalidOperationException)
        {
            return HealthCheckResult.Unhealthy("The database is not configured.");
        }
        catch (Exception)
        {
            return HealthCheckResult.Unhealthy("The database is unreachable.");
        }

        return status switch
        {
            DatabaseStatus.Ready => HealthCheckResult.Healthy("The database is reachable and up to date."),
            DatabaseStatus.MigrationsPending => HealthCheckResult.Unhealthy(
                "The database schema is missing or out of date. Apply the migrations."),
            _ => HealthCheckResult.Unhealthy("The database is unreachable.")
        };
    }
}
