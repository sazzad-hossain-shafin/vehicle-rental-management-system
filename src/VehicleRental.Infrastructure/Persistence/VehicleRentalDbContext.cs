using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using VehicleRental.Domain.Entities;
using VehicleRental.Infrastructure.Identity;

namespace VehicleRental.Infrastructure.Persistence;

/// <summary>
/// The PostgreSQL database for the rental system: the business data and the ASP.NET Core Identity tables.
/// They share one context deliberately, so a customer and their login can be created in a single
/// database transaction. Mapping is configured with the Fluent API in <c>Persistence/Configurations</c>,
/// so the domain classes carry no persistence attributes.
/// </summary>
public class VehicleRentalDbContext : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>
{
    public VehicleRentalDbContext(DbContextOptions<VehicleRentalDbContext> options)
        : base(options)
    {
    }

    public DbSet<Vehicle> Vehicles => Set<Vehicle>();

    public DbSet<Customer> Customers => Set<Customer>();

    public DbSet<Rental> Rentals => Set<Rental>();

    public DbSet<Reservation> Reservations => Set<Reservation>();

    // Phase 14A-3 (expand): present in the schema, used by nothing in the running application yet.
    public DbSet<Company> Companies => Set<Company>();

    public DbSet<CompanyMembership> CompanyMemberships => Set<CompanyMembership>();

    public DbSet<CompanyCustomer> CompanyCustomers => Set<CompanyCustomer>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);

        configurationBuilder.Conventions.Replace<ForeignKeyIndexConvention>(
            serviceProvider => new NoUnneededForeignKeyIndexesConvention(
                serviceProvider.GetRequiredService<ProviderConventionSetBuilderDependencies>()));
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Same plain table names as the business tables, instead of Identity's AspNet* prefix.
        modelBuilder.Entity<IdentityUserRole<Guid>>().ToTable("UserRoles");
        modelBuilder.Entity<IdentityUserClaim<Guid>>().ToTable("UserClaims");
        modelBuilder.Entity<IdentityUserLogin<Guid>>().ToTable("UserLogins");
        modelBuilder.Entity<IdentityUserToken<Guid>>().ToTable("UserTokens");
        modelBuilder.Entity<IdentityRoleClaim<Guid>>().ToTable("RoleClaims");

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(VehicleRentalDbContext).Assembly);
    }
}
