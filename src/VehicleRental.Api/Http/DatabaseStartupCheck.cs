using VehicleRental.Infrastructure;

namespace VehicleRental.Api.Http;

/// <summary>
/// Logs, once the API is running, whether the database is reachable and migrated. It only reports:
/// it never applies migrations and never stops the API, so a missing database shows up clearly in the
/// log and in <c>/health</c> instead of crashing the process.
/// </summary>
internal sealed class DatabaseStartupCheck : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DatabaseStartupCheck> _logger;

    public DatabaseStartupCheck(IServiceScopeFactory scopeFactory, ILogger<DatabaseStartupCheck> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            var checker = scope.ServiceProvider.GetRequiredService<DatabaseStatusChecker>();

            switch (await checker.CheckAsync(stoppingToken))
            {
                case DatabaseStatus.Ready:
                    _logger.LogInformation("Database is reachable and up to date.");
                    break;

                case DatabaseStatus.MigrationsPending:
                    _logger.LogWarning(
                        "Database schema is missing or out of date. Apply the migrations: " +
                        "dotnet ef database update --project src/VehicleRental.Infrastructure");
                    break;

                default:
                    _logger.LogWarning("Database is unreachable. Check that PostgreSQL is running and the connection string is correct.");
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            // shutting down
        }
        catch (Exception ex)
        {
            // Only the type is logged: messages from database drivers can contain connection details.
            _logger.LogError("Could not determine the database status ({ExceptionType}). Is the connection string configured?",
                ex.GetType().Name);
        }
    }
}
