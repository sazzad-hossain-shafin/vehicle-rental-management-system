using VehicleRental.Domain.Enums;

namespace VehicleRental.Application.Tenancy;

/// <summary>How the company of the current scope was established. Informational: it never grants anything.</summary>
public enum TenantSource
{
    /// <summary>No tenant is bound (every request today).</summary>
    None,

    /// <summary>From a public company web address (slug) of an active company.</summary>
    PublicSlug,

    /// <summary>From a verified active membership of the signed-in user.</summary>
    Membership,

    /// <summary>By a background or administrative operation that names its company explicitly.</summary>
    Background,
}

/// <summary>
/// The company the current unit of work acts for. Scoped: one per request or background scope. Nothing binds a tenant
/// on any production path yet (Phase 14A-4 only builds the infrastructure), so everything that runs today sees an
/// unbound context and behaves exactly as before.
/// </summary>
public interface ITenantContext
{
    bool IsBound { get; }

    Guid? CompanyId { get; }

    TenantSource Source { get; }

    /// <summary>The bound company, or <see cref="TenantRequiredException"/> (fail closed) when there is none.</summary>
    Guid RequireCompanyId();
}

/// <summary>Binds the tenant of a scope exactly once. Only the resolvers and the background runner use it.</summary>
public interface ITenantBinder
{
    /// <summary>
    /// Binds the company. Binding the same company again is a no-op; binding a different one throws
    /// <see cref="TenantRebindException"/>. Throws <see cref="TenancyDisabledException"/> while tenancy is disabled.
    /// </summary>
    void Bind(Guid companyId, TenantSource source);
}

/// <summary>
/// The tenant-scoped database transaction of the current scope. The tenant is bound first, then the transaction is
/// started, and the transaction-local tenant setting is applied inside it before any tenant query can run.
/// </summary>
public interface ITenantTransaction : IAsyncDisposable
{
    bool IsActive { get; }

    /// <summary>Starts the transaction for the bound tenant. Throws if no tenant is bound or a transaction is already open.</summary>
    Task BeginAsync(CancellationToken cancellationToken = default);

    Task CommitAsync(CancellationToken cancellationToken = default);

    Task RollbackAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Runs work for one company in its own scope and tenant transaction (commit on success, roll back otherwise).
/// It is never "all companies at once".
/// </summary>
public interface ITenantScopeRunner
{
    Task<T> RunAsync<T>(
        Guid companyId,
        Func<IServiceProvider, CancellationToken, Task<T>> work,
        CancellationToken cancellationToken = default);

    /// <summary>Visits every active company in turn, each in its own scope and transaction.</summary>
    Task ForEachActiveCompanyAsync(
        Func<Guid, IServiceProvider, CancellationToken, Task> work,
        CancellationToken cancellationToken = default);
}

/// <summary>A company as the resolvers see it. Deliberately small: no settings, no members.</summary>
public sealed record CompanySummary(Guid Id, string Slug, string TimeZoneId, CompanyStatus Status);

/// <summary>Looks companies up for resolution. Reads the companies table, which is not tenant data.</summary>
public interface ICompanyDirectory
{
    /// <summary>
    /// The ACTIVE company with this public web address, or null. A malformed slug returns null without touching the
    /// database; pending, suspended and unknown companies are indistinguishable.
    /// </summary>
    Task<CompanySummary?> FindActiveBySlugAsync(string? slug, CancellationToken cancellationToken = default);

    /// <summary>A company a member may manage: PendingReview or Active (a suspended company is refused). Null otherwise.</summary>
    Task<CompanySummary?> FindManageableAsync(Guid companyId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Guid>> ListActiveIdsAsync(CancellationToken cancellationToken = default);
}

/// <summary>Reads memberships. Always consulted per request: access is never taken from the token.</summary>
public interface IMembershipDirectory
{
    /// <summary>The user's role in the company if they have an ACTIVE membership, otherwise null.</summary>
    Task<CompanyRole?> FindActiveRoleAsync(Guid companyId, Guid userId, CancellationToken cancellationToken = default);
}

/// <summary>The tenant was required but none is bound (or no tenant transaction is open). Fails closed.</summary>
public sealed class TenantRequiredException : InvalidOperationException
{
    public TenantRequiredException(string message)
        : base(message)
    {
    }
}

/// <summary>An attempt to bind a scope to a second, different company.</summary>
public sealed class TenantRebindException : InvalidOperationException
{
    public TenantRebindException()
        : base("The tenant of this scope is already bound and cannot be changed.")
    {
    }
}

/// <summary>Tenant mode is switched off (Tenancy:Enabled is false), so no tenant can be bound.</summary>
public sealed class TenancyDisabledException : InvalidOperationException
{
    public TenancyDisabledException()
        : base("Tenancy is disabled (Tenancy:Enabled is false), so no tenant can be bound.")
    {
    }
}
