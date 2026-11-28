using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using VehicleRental.Infrastructure.Tenancy;

namespace VehicleRental.Api.Http;

/// <summary>
/// Decides the role check mode in force. Production-safe by default: when <c>Tenancy:DatabaseRoleCheck</c> is not set,
/// Development uses Warn (local work with a privileged role keeps going) and every other environment uses Enforce, so a
/// deployment that forgets the setting cannot start as an unsafe role. Off is refused outside Development. An explicit
/// Warn outside Development is allowed only as the documented temporary emergency measure, and is logged as such.
/// </summary>
internal static class DatabaseRoleCheckPolicy
{
    public static DatabaseRoleCheckMode Resolve(DatabaseRoleCheckMode? configured, IHostEnvironment environment)
    {
        bool development = environment.IsDevelopment();

        return configured switch
        {
            null => development ? DatabaseRoleCheckMode.Warn : DatabaseRoleCheckMode.Enforce,
            DatabaseRoleCheckMode.Off when !development => throw new InvalidOperationException(
                $"Tenancy:DatabaseRoleCheck=Off is allowed only in the Development environment, not in '{environment.EnvironmentName}'. " +
                "Remove the setting (Enforce is the default) or, as a temporary emergency measure only, use Warn; " +
                "see docs/multi-tenancy/runtime-role-runbook.md."),
            _ => configured.Value,
        };
    }
}

/// <summary>
/// At startup, checks that the database role the API connects as is safe (not a superuser, no BYPASSRLS, owns no tables,
/// cannot create objects, cannot change the migration history). What happens on a violation depends on the mode chosen by
/// <see cref="DatabaseRoleCheckPolicy"/>: Off does nothing, Warn logs it, Enforce refuses to start.
/// An unreachable database is not a violation here (the API already starts degraded in that case); the health check below
/// keeps checking and reports Unhealthy in Enforce mode until it can confirm the role is safe.
/// </summary>
internal sealed class DatabaseRoleStartupCheck : IHostedService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly IOptions<TenancyOptions> _options;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<DatabaseRoleStartupCheck> _logger;

    public DatabaseRoleStartupCheck(
        IServiceScopeFactory scopes,
        IOptions<TenancyOptions> options,
        IHostEnvironment environment,
        ILogger<DatabaseRoleStartupCheck> logger)
    {
        _scopes = scopes;
        _options = options;
        _environment = environment;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // Throws (and so refuses to start) for Off outside Development.
        DatabaseRoleCheckMode mode = DatabaseRoleCheckPolicy.Resolve(_options.Value.DatabaseRoleCheck, _environment);

        if (mode == DatabaseRoleCheckMode.Warn && !_environment.IsDevelopment())
        {
            _logger.LogWarning(
                "Tenancy:DatabaseRoleCheck is Warn outside Development ({Environment}). This is a temporary emergency setting only: " +
                "an unsafe database role will not stop the API. Remove it as soon as the role is fixed.",
                _environment.EnvironmentName);
        }

        if (mode == DatabaseRoleCheckMode.Off)
        {
            return;
        }

        IReadOnlyList<string> violations;

        try
        {
            await using AsyncServiceScope scope = _scopes.CreateAsyncScope();
            violations = await scope.ServiceProvider.GetRequiredService<DatabaseRoleSafetyCheck>().FindViolationsAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Only the type is logged: driver messages can contain connection details.
            _logger.LogWarning(
                "The database role safety check could not run ({ExceptionType}). The health check will keep trying.",
                ex.GetType().Name);
            return;
        }

        if (violations.Count == 0)
        {
            _logger.LogInformation("The database role passed the safety check.");
            return;
        }

        foreach (string violation in violations)
        {
            _logger.LogWarning("Unsafe database role: {Violation}.", violation);
        }

        if (mode == DatabaseRoleCheckMode.Enforce)
        {
            throw new InvalidOperationException(
                "The application's database role is not safe (the database role check is Enforce). " +
                "Connect as the restricted runtime role created by the db-roles service; see docs/multi-tenancy/tenant-infrastructure.md.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>
/// The same check as a readiness probe (/health only, not /health/live), cached briefly so probes do not hit the database
/// each time. Enforce mode reports Unhealthy on a violation. Warn mode never changes the readiness status (the startup log
/// carries the warning, so existing monitoring keeps seeing Healthy); it only says so in the description. Responses carry
/// only a generic description: never a role name, password or connection string.
/// </summary>
internal sealed class DatabaseRoleHealthCheck : IHealthCheck
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(60);

    private readonly IServiceScopeFactory _scopes;
    private readonly IOptions<TenancyOptions> _options;
    private readonly IHostEnvironment _environment;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTimeOffset _checkedAt = DateTimeOffset.MinValue;
    private HealthCheckResult _last;

    public DatabaseRoleHealthCheck(IServiceScopeFactory scopes, IOptions<TenancyOptions> options, IHostEnvironment environment)
    {
        _scopes = scopes;
        _options = options;
        _environment = environment;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        DatabaseRoleCheckMode mode = DatabaseRoleCheckPolicy.Resolve(_options.Value.DatabaseRoleCheck, _environment);

        if (mode == DatabaseRoleCheckMode.Off)
        {
            return HealthCheckResult.Healthy("The database role check is switched off.");
        }

        await _gate.WaitAsync(cancellationToken);

        try
        {
            if (DateTimeOffset.UtcNow - _checkedAt < CacheFor)
            {
                return _last;
            }

            HealthCheckResult result;

            try
            {
                await using AsyncServiceScope scope = _scopes.CreateAsyncScope();
                IReadOnlyList<string> violations = await scope.ServiceProvider
                    .GetRequiredService<DatabaseRoleSafetyCheck>()
                    .FindViolationsAsync(cancellationToken);

                result = violations.Count == 0
                    ? HealthCheckResult.Healthy("The database role is safe.")
                    : mode == DatabaseRoleCheckMode.Enforce
                        ? HealthCheckResult.Unhealthy("The database role is not safe.")
                        : HealthCheckResult.Healthy("Warning: the database role is not safe (see the startup log).");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                // Not confirmed is not safe in Enforce mode, but it is not cached: the next probe tries again.
                return mode == DatabaseRoleCheckMode.Enforce
                    ? HealthCheckResult.Unhealthy("The database role could not be checked.")
                    : HealthCheckResult.Healthy("Warning: the database role could not be checked.");
            }

            _last = result;
            _checkedAt = DateTimeOffset.UtcNow;

            return result;
        }
        finally
        {
            _gate.Release();
        }
    }
}
