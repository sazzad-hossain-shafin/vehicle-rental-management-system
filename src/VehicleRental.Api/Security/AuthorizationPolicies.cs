using Microsoft.AspNetCore.Authorization;
using VehicleRental.Application.Security;

namespace VehicleRental.Api.Security;

/// <summary>
/// The capabilities the API protects. Endpoints name a capability, not a list of roles, so who may do
/// what is decided in one place.
/// </summary>
public static class Policies
{
    /// <summary>Add and change fleet data (today: add vehicles).</summary>
    public const string FleetManage = nameof(FleetManage);

    /// <summary>Register customers and look up any customer.</summary>
    public const string CustomerManage = nameof(CustomerManage);

    /// <summary>Run the rental desk: start and return rentals, and read the whole rental history.</summary>
    public const string RentalManage = nameof(RentalManage);

    /// <summary>Run the reservation desk: list, look up, book for a customer, cancel and pick up any reservation.</summary>
    public const string ReservationManage = nameof(ReservationManage);

    /// <summary>Create staff accounts.</summary>
    public const string UserAdministration = nameof(UserAdministration);

    /// <summary>A customer account acting on its own data (<c>/me/...</c>).</summary>
    public const string CustomerSelfService = nameof(CustomerSelfService);
}

internal static class AuthorizationSetup
{
    /// <summary>
    /// Registers the policies, and makes "must be signed in" the default for every endpoint. An endpoint is
    /// only reachable anonymously if it says so explicitly with <c>AllowAnonymous</c>, so a new endpoint can
    /// never be left open by accident.
    /// </summary>
    public static IServiceCollection AddApiAuthorization(this IServiceCollection services)
    {
        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
            .AddPolicy(Policies.FleetManage, p => p.RequireRole(Roles.Staff, Roles.Admin))
            .AddPolicy(Policies.CustomerManage, p => p.RequireRole(Roles.Staff, Roles.Admin))
            .AddPolicy(Policies.RentalManage, p => p.RequireRole(Roles.Staff, Roles.Admin))
            .AddPolicy(Policies.ReservationManage, p => p.RequireRole(Roles.Staff, Roles.Admin))
            .AddPolicy(Policies.UserAdministration, p => p.RequireRole(Roles.Admin))
            .AddPolicy(Policies.CustomerSelfService, p => p
                .RequireRole(Roles.Customer)
                .RequireClaim(AuthClaims.CustomerId));

        return services;
    }
}
