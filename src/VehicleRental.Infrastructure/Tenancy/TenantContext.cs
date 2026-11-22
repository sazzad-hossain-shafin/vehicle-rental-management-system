using System.Data.Common;
using Microsoft.Extensions.Options;
using VehicleRental.Application.Tenancy;

namespace VehicleRental.Infrastructure.Tenancy;

public enum DatabaseRoleCheckMode
{
    /// <summary>The check does not run.</summary>
    Off,

    /// <summary>Violations are logged and shown as "degraded" by the health check. The application starts.</summary>
    Warn,

    /// <summary>Violations stop the application from starting and make the health check unhealthy.</summary>
    Enforce,
}

/// <summary>The "Tenancy" configuration section. The defaults leave every existing behaviour exactly as it was.</summary>
public sealed class TenancyOptions
{
    public const string SectionName = "Tenancy";

    /// <summary>
    /// Master switch for tenant mode. While false (the default) no tenant can be bound anywhere: binding throws, so an
    /// accidental call cannot change how data is read or written.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// What to do when the application's database role is unsafe (superuser, BYPASSRLS, owns tables, can create objects,
    /// has migration privileges). Warn by default for local development; Docker Compose and CI set Enforce.
    /// </summary>
    public DatabaseRoleCheckMode DatabaseRoleCheck { get; set; } = DatabaseRoleCheckMode.Warn;
}

/// <summary>
/// The tenant of one scope (one request or background operation). It also remembers which database transaction the
/// transaction-local tenant setting was applied to, so a command can be checked against it.
/// </summary>
public sealed class TenantContext : ITenantContext, ITenantBinder
{
    private readonly TenancyOptions _options;
    private readonly object _gate = new();
    private Guid? _companyId;
    private TenantSource _source = TenantSource.None;
    private DbTransaction? _appliedTransaction;

    public TenantContext(IOptions<TenancyOptions> options) => _options = options.Value;

    public bool IsBound
    {
        get
        {
            lock (_gate)
            {
                return _companyId is not null;
            }
        }
    }

    public Guid? CompanyId
    {
        get
        {
            lock (_gate)
            {
                return _companyId;
            }
        }
    }

    public TenantSource Source
    {
        get
        {
            lock (_gate)
            {
                return _source;
            }
        }
    }

    public Guid RequireCompanyId() =>
        CompanyId ?? throw new TenantRequiredException("No company is bound to this scope.");

    public void Bind(Guid companyId, TenantSource source)
    {
        if (!_options.Enabled)
        {
            throw new TenancyDisabledException();
        }

        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("A company is required.", nameof(companyId));
        }

        if (source == TenantSource.None)
        {
            throw new ArgumentException("A tenant source is required.", nameof(source));
        }

        lock (_gate)
        {
            if (_companyId is { } existing)
            {
                // The same company again changes nothing; a different one is never accepted.
                if (existing != companyId)
                {
                    throw new TenantRebindException();
                }

                return;
            }

            _companyId = companyId;
            _source = source;
        }
    }

    // ---- the transaction the tenant setting was applied to (used by the interceptors and the unit of work) ----------

    internal void MarkApplied(DbTransaction transaction)
    {
        lock (_gate)
        {
            _appliedTransaction = transaction;
        }
    }

    internal void ClearApplied(DbTransaction transaction)
    {
        lock (_gate)
        {
            if (ReferenceEquals(_appliedTransaction, transaction))
            {
                _appliedTransaction = null;
            }
        }
    }

    internal bool IsApplied(DbTransaction? transaction)
    {
        lock (_gate)
        {
            return transaction is not null && ReferenceEquals(_appliedTransaction, transaction);
        }
    }
}
