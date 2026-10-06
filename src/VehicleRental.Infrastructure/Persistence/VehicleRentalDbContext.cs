using Microsoft.EntityFrameworkCore;
using VehicleRental.Domain.Entities;

namespace VehicleRental.Infrastructure.Persistence;

/// <summary>
/// The PostgreSQL database for the rental system. Mapping is configured with the Fluent API in
/// <c>Persistence/Configurations</c>, so the domain classes carry no persistence attributes.
/// </summary>
public class VehicleRentalDbContext : DbContext
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
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(VehicleRentalDbContext).Assembly);
    }
}
