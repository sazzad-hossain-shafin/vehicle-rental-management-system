using VehicleRental.Infrastructure;
using VehicleRental.Infrastructure.Identity;

namespace VehicleRental.Api.Http;

/// <summary>
/// Creates the initial admin account at startup, but only when its credentials were supplied through
/// configuration (user secrets or environment variables) and the database is migrated. It never stops the
/// API, never changes an existing account, and never logs the password.
/// </summary>
internal sealed class AdminSeedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AdminSeedService> _logger;

    public AdminSeedService(IServiceScopeFactory scopeFactory, ILogger<AdminSeedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            using IServiceScope scope = _scopeFactory.CreateScope();

            DatabaseStatus status = await scope.ServiceProvider
                .GetRequiredService<DatabaseStatusChecker>()
                .CheckAsync(stoppingToken);

            if (status != DatabaseStatus.Ready)
            {
                _logger.LogWarning("Skipping initial admin creation: the database is not ready (reachable and migrated).");
                return;
            }

            await scope.ServiceProvider.GetRequiredService<AdminSeeder>().SeedAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // shutting down
        }
        catch (Exception ex)
        {
            // Only the type: messages from database drivers can contain connection details.
            _logger.LogError("Initial admin creation failed ({ExceptionType}).", ex.GetType().Name);
        }
    }
}
