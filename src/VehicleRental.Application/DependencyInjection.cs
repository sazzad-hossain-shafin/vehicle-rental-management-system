using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VehicleRental.Application.Customers;
using VehicleRental.Application.Rentals;
using VehicleRental.Application.Reservations;
using VehicleRental.Application.Vehicles;

namespace VehicleRental.Application;

public static class DependencyInjection
{
    /// <summary>
    /// Registers the use-case services. They are scoped, so each request (or other unit of work)
    /// gets its own, sharing the repositories and unit of work registered for that scope.
    /// </summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<VehicleService>();
        services.AddScoped<CustomerService>();
        services.AddScoped<RentalService>();
        services.AddScoped<ReservationService>();

        return services;
    }
}
