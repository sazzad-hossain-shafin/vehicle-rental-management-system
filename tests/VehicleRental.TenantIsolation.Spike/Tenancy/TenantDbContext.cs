using Microsoft.EntityFrameworkCore;

namespace VehicleRental.TenantIsolation.Spike.Tenancy;

public sealed class Company
{
    public Guid Id { get; set; }

    public string Slug { get; set; } = "";

    public string Name { get; set; } = "";

    public string Status { get; set; } = "";
}

public sealed class Vehicle
{
    public Guid Id { get; set; }

    public Guid CompanyId { get; set; }

    public string Registration { get; set; } = "";

    public decimal DailyRate { get; set; }
}

public sealed class Reservation
{
    public Guid Id { get; set; }

    public Guid CompanyId { get; set; }

    public Guid VehicleId { get; set; }

    public DateOnly StartDate { get; set; }

    public DateOnly EndDate { get; set; }

    public string Status { get; set; } = "Active";
}

/// <summary>
/// A context that is bound to exactly one company for its whole life. Layer one of the isolation: a global query
/// filter. Layer two (PostgreSQL Row-Level Security) does not depend on this filter, which is why the tests also run
/// with <c>IgnoreQueryFilters()</c> and raw SQL.
/// </summary>
public sealed class TenantDbContext : DbContext
{
    private readonly Guid _filterCompany;

    public TenantDbContext(DbContextOptions<TenantDbContext> options, Guid? companyId)
        : base(options)
    {
        CompanyId = companyId;

        // With no tenant the filter compares with Guid.Empty and matches nothing.
        _filterCompany = companyId ?? Guid.Empty;

        // FINDING: for a single-statement save EF Core does not open a transaction at all (it relies on the statement
        // being atomic), so no transaction means no tenant setting and RLS would hide the row. Always asking EF to wrap
        // SaveChanges in a transaction makes the interceptor apply the tenant to it. Reads still need the explicit
        // transaction from TenantSession; the command guard refuses anything else.
        Database.AutoTransactionBehavior = AutoTransactionBehavior.Always;
    }

    /// <summary>The company this context acts for; null means "no tenant" (everything is then refused).</summary>
    public Guid? CompanyId { get; }

    public DbSet<Company> Companies => Set<Company>();

    public DbSet<Vehicle> Vehicles => Set<Vehicle>();

    public DbSet<Reservation> Reservations => Set<Reservation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Company>(e =>
        {
            e.ToTable("companies");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            e.Property(x => x.Slug).HasColumnName("slug");
            e.Property(x => x.Name).HasColumnName("name");
            e.Property(x => x.Status).HasColumnName("status");
        });

        modelBuilder.Entity<Vehicle>(e =>
        {
            e.ToTable("vehicles");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            e.Property(x => x.CompanyId).HasColumnName("company_id");
            e.Property(x => x.Registration).HasColumnName("registration");
            e.Property(x => x.DailyRate).HasColumnName("daily_rate");

            e.HasQueryFilter(x => x.CompanyId == _filterCompany);
        });

        modelBuilder.Entity<Reservation>(e =>
        {
            e.ToTable("reservations");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            e.Property(x => x.CompanyId).HasColumnName("company_id");
            e.Property(x => x.VehicleId).HasColumnName("vehicle_id");
            e.Property(x => x.StartDate).HasColumnName("start_date");
            e.Property(x => x.EndDate).HasColumnName("end_date");
            e.Property(x => x.Status).HasColumnName("status");
            e.HasQueryFilter(x => x.CompanyId == _filterCompany);
        });
    }
}
