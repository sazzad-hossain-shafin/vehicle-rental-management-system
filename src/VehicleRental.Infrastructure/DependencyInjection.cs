using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VehicleRental.Application.Abstractions;
using VehicleRental.Infrastructure.Persistence;

namespace VehicleRental.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registers the PostgreSQL database context, repositories and unit of work, all scoped, so one
    /// request shares one context. The connection string is read from
    /// <c>ConnectionStrings:VehicleRentalDatabase</c> when the context is first needed (not when this
    /// method runs), so configuration added later, for example by tests, is honoured. A missing
    /// connection string is reported with a clear message, never with a default.
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        services.AddDbContext<VehicleRentalDbContext>((serviceProvider, options) =>
        {
            string? connectionString = serviceProvider
                .GetRequiredService<IConfiguration>()
                .GetConnectionString(VehicleRentalDbContextOptions.ConnectionStringName);

            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    $"No database connection string is configured. Set " +
                    $"'ConnectionStrings:{VehicleRentalDbContextOptions.ConnectionStringName}' " +
                    "(user secrets or the " +
                    $"{VehicleRentalDbContextOptions.ConnectionStringEnvironmentVariable} environment variable).");
            }

            options.UseNpgsql(connectionString);
        });

        services.AddScoped<IVehicleRepository, VehicleRepository>();
        services.AddScoped<ICustomerRepository, CustomerRepository>();
        services.AddScoped<IRentalRepository, RentalRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<DatabaseStatusChecker>();

        return services;
    }
}
