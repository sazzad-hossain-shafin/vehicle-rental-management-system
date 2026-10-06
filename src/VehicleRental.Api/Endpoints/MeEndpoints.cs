using System.Security.Claims;
using VehicleRental.Api.Contracts;
using VehicleRental.Api.Security;
using VehicleRental.Application;
using VehicleRental.Application.Accounts;
using VehicleRental.Application.Customers;
using VehicleRental.Application.Rentals;

namespace VehicleRental.Api.Endpoints;

/// <summary>
/// "My" endpoints: who the caller is, and for customer accounts their own profile and rentals. They take
/// no customer ID from the client; the customer always comes from the verified token, so there is nothing
/// to tamper with.
/// </summary>
internal static class MeEndpoints
{
    public static IEndpointRouteBuilder MapMeEndpoints(this IEndpointRouteBuilder routes)
    {
        RouteGroupBuilder me = routes.MapGroup("/me").WithTags("Current user");

        me.MapGet("/", GetMeAsync)
            .WithName("GetCurrentUser")
            .WithSummary("Describes the signed-in account")
            .WithDescription("Id, email, roles and, for customer accounts, the linked customer. No security data.")
            .RequireAuthorization()
            .Produces<UserDto>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        me.MapGet("/customer", GetMyCustomerAsync)
            .WithName("GetMyCustomer")
            .WithSummary("Gets the signed-in customer's own profile")
            .RequireAuthorization(Policies.CustomerSelfService)
            .Produces<CustomerDto>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        me.MapGet("/rentals", GetMyRentalsAsync)
            .WithName("ListMyRentals")
            .WithSummary("Lists the signed-in customer's own rentals, one page at a time")
            .RequireAuthorization(Policies.CustomerSelfService)
            .Produces<PagedResult<RentalDto>>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        return routes;
    }

    private static async Task<IResult> GetMeAsync(
        ClaimsPrincipal principal,
        IAccountService accounts,
        CancellationToken cancellationToken)
    {
        Guid? userId = principal.GetUserId();
        UserDto? user = userId is { } id ? await accounts.GetUserAsync(id, cancellationToken) : null;

        // A valid token for an account that no longer exists is no longer a valid sign-in.
        return user is null ? Results.Unauthorized() : Results.Ok(user);
    }

    private static async Task<IResult> GetMyCustomerAsync(
        ClaimsPrincipal principal,
        CustomerService customers,
        CancellationToken cancellationToken) =>
        Results.Ok(await customers.GetByIdAsync(principal.GetCustomerId()!.Value, cancellationToken));

    private static async Task<IResult> GetMyRentalsAsync(
        [AsParameters] PageQuery query,
        ClaimsPrincipal principal,
        RentalService rentals,
        CancellationToken cancellationToken) =>
        Results.Ok(await rentals.GetCustomerRentalHistoryPageAsync(
            principal.GetCustomerId()!.Value, query.Page, query.PageSize, cancellationToken));
}
