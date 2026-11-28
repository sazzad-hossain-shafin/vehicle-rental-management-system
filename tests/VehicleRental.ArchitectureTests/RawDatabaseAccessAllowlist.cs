using VehicleRental.ArchitectureTests.Support;

namespace VehicleRental.ArchitectureTests;

/// <summary>One reviewed group of raw database access: the kind, where, how many times, and why it is acceptable.</summary>
public sealed record AllowedRawAccess(RawAccessKind Kind, string File, string Type, int Count, string Reason);

/// <summary>
/// The ONLY places production code may access the database outside EF Core LINQ. Every entry is reviewed; the count is
/// exact, so even one more call in an allowed type fails the build until this file is changed (and that change is
/// reviewed). An entry that no longer matches anything also fails, so the list cannot quietly grow stale.
/// <para>
/// Before adding an entry, check: does the code run inside a tenant transaction (then the command must be part of it)?
/// Is the SQL a compile-time constant (the scanner also enforces this)? Could it read or write another company's rows?
/// See docs/multi-tenancy/security-hardening-before-rls.md.
/// </para>
/// </summary>
public static class RawDatabaseAccessAllowlist
{
    public static IReadOnlyList<AllowedRawAccess> Entries { get; } =
    [
        new(
            RawAccessKind.RawSql,
            "src/VehicleRental.Infrastructure/Persistence/UnitOfWork.cs",
            "UnitOfWork",
            Count: 2,
            Reason: "SET LOCAL lock_timeout from a compile-time constant, inside the booking transaction. No table access, no input."),
        new(
            RawAccessKind.RawConnection,
            "src/VehicleRental.Infrastructure/Tenancy/DatabaseRoleSafetyCheck.cs",
            "DatabaseRoleSafetyCheck",
            Count: 5,
            Reason: "Read-only catalog queries about the connection's own role (startup and health check). Constant SQL, no tenant, no application tables."),
        new(
            RawAccessKind.RawConnection,
            "src/VehicleRental.Infrastructure/Tenancy/TenantInterceptors.cs",
            "TenantTransactionInterceptor",
            Count: 3,
            Reason: "Applies and verifies the transaction-local tenant setting as the first statement of the tenant transaction. Constant SQL; the company is a bound parameter."),
    ];
}
