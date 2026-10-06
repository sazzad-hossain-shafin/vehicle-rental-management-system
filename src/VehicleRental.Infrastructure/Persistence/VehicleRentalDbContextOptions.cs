using Microsoft.EntityFrameworkCore;

namespace VehicleRental.Infrastructure.Persistence;

/// <summary>
/// The one place that chooses the database provider and where the connection string is named.
/// </summary>
public static class VehicleRentalDbContextOptions
{
    /// <summary>The key under "ConnectionStrings" in configuration.</summary>
    public const string ConnectionStringName = "VehicleRentalDatabase";

    /// <summary>The configuration key as an environment variable, which .NET reads as a normal setting.</summary>
    public const string ConnectionStringEnvironmentVariable = "ConnectionStrings__" + ConnectionStringName;

    public static DbContextOptions<VehicleRentalDbContext> Create(string connectionString) =>
        new DbContextOptionsBuilder<VehicleRentalDbContext>()
            .UseNpgsql(connectionString)
            .Options;
}
