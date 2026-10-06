using Microsoft.EntityFrameworkCore.Design;

namespace VehicleRental.Infrastructure.Persistence;

/// <summary>
/// Used only by the EF Core command-line tools (<c>dotnet ef</c>). Creating a migration needs no
/// database, so a credential-free default is enough; applying migrations reads the real connection
/// string from the <c>ConnectionStrings__VehicleRentalDatabase</c> environment variable.
/// </summary>
public sealed class VehicleRentalDbContextFactory : IDesignTimeDbContextFactory<VehicleRentalDbContext>
{
    private const string DesignTimeDefault = "Host=localhost;Database=vehiclerental";

    public VehicleRentalDbContext CreateDbContext(string[] args)
    {
        string connectionString =
            Environment.GetEnvironmentVariable(VehicleRentalDbContextOptions.ConnectionStringEnvironmentVariable)
            ?? DesignTimeDefault;

        return new VehicleRentalDbContext(VehicleRentalDbContextOptions.Create(connectionString));
    }
}
