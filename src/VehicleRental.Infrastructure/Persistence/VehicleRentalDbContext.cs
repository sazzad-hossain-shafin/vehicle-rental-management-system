using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
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
