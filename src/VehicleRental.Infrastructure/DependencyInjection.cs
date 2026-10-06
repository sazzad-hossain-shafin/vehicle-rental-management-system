using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using VehicleRental.Application.Abstractions;
using VehicleRental.Application.Accounts;
using VehicleRental.Infrastructure.Identity;
using VehicleRental.Infrastructure.Persistence;

namespace VehicleRental.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registers the PostgreSQL database context, repositories, unit of work, ASP.NET Core Identity and the
    /// token service, all scoped, so one request shares one context. The connection string is read from
    /// <c>ConnectionStrings:VehicleRentalDatabase</c> when the context is first needed (not when this
    /// method runs), so configuration added later, for example by tests, is honoured. A missing
    /// connection string is reported with a clear message, never with a default.
    /// </summary>
    /// <remarks>
    /// The token settings (<c>Jwt:*</c>) are validated when the application starts, so it refuses to run
    /// with a missing or weak signing key instead of failing later or using an insecure default.
    /// </remarks>
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

        AddIdentity(services);

        return services;
    }

    private static void AddIdentity(IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);

        // Password hashing, validation and lockout all belong to Identity. Nothing is customized
        // beyond its published options.
        services
            .AddIdentityCore<ApplicationUser>(options =>
            {
                options.User.RequireUniqueEmail = true;

                options.Password.RequiredLength = 10;
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequiredUniqueChars = 4;

                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<VehicleRentalDbContext>();

        services.AddSingleton<IValidateOptions<JwtOptions>, JwtOptionsValidator>();
        services.AddOptions<JwtOptions>().BindConfiguration(JwtOptions.SectionName).ValidateOnStart();
        services.AddOptions<AdminSeedOptions>().BindConfiguration(AdminSeedOptions.SectionName);

        services.AddScoped<JwtTokenService>();
        services.AddScoped<IAccountService, AccountService>();
        services.AddScoped<AdminSeeder>();
    }
}
