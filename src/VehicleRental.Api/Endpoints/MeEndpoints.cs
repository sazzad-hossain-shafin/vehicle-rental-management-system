using System.Security.Claims;
using VehicleRental.Api.Contracts;
using VehicleRental.Api.Security;
using VehicleRental.Application;
using VehicleRental.Application.Accounts;
using VehicleRental.Application.Customers;
using VehicleRental.Application.Rentals;
using VehicleRental.Application.Reservations;
using VehicleRental.Api.Http;

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

        me.MapPost("/reservations", CreateMyReservationAsync)
            .WithName("CreateMyReservation")
            .WithSummary("Reserves a vehicle for the signed-in customer")
            .WithDescription(
                "The customer is always the signed-in one; the request has no customer field. The period is the " +
                "half-open interval [startDate, endDate). The price is quoted now and stored.")
            .RequireAuthorization(Policies.CustomerSelfService)
            .WithRequestValidation<CreateMyReservationRequest>()
            .Produces<ReservationDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        me.MapGet("/reservations", GetMyReservationsAsync)
            .WithName("ListMyReservations")
            .WithSummary("Lists the signed-in customer reservations, one page at a time")
            .RequireAuthorization(Policies.CustomerSelfService)
            .Produces<PagedResult<ReservationDto>>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        me.MapGet("/reservations/{id}", GetMyReservationAsync)
            .WithName("GetMyReservationById")
            .WithSummary("Gets one of the signed-in customer own reservations")
            .WithDescription("Someone else reservation is answered 404, exactly like one that does not exist.")
            .RequireAuthorization(Policies.CustomerSelfService)
            .Produces<ReservationDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        me.MapPost("/reservations/{id}/cancel", CancelMyReservationAsync)
            .WithName("CancelMyReservation")
            .WithSummary("Cancels one of the signed-in customer own reservations")
            .WithDescription("Only an active reservation that has not started yet. After its start date the rental desk handles it.")
            .RequireAuthorization(Policies.CustomerSelfService)
            .Produces<ReservationDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

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

    private static async Task<IResult> CreateMyReservationAsync(
        CreateMyReservationRequest request,
        ClaimsPrincipal principal,
        ReservationService reservations,
        CancellationToken cancellationToken)
    {
        // The customer comes only from the verified token. No promotion: a customer cannot grant themselves a discount.
        ReservationDto reservation = await reservations.CreateAsync(
            new CreateReservationRequest(
                principal.GetCustomerId()!.Value,
                request.VehicleId!.Value,
                request.StartDate!.Value,
                request.EndDate!.Value),
            cancellationToken);

        return Results.CreatedAtRoute("GetMyReservationById", new { id = reservation.Id }, reservation);
    }

    private static async Task<IResult> GetMyReservationsAsync(
        [AsParameters] PageQuery query,
        ClaimsPrincipal principal,
        ReservationService reservations,
        CancellationToken cancellationToken) =>
        Results.Ok(await reservations.GetPageForCustomerAsync(
            principal.GetCustomerId()!.Value, query.Page, query.PageSize, cancellationToken));

    private static async Task<IResult> GetMyReservationAsync(
        Guid id,
        ClaimsPrincipal principal,
        ReservationService reservations,
        CancellationToken cancellationToken) =>
        Results.Ok(await reservations.GetForCustomerAsync(id, principal.GetCustomerId()!.Value, cancellationToken));

    private static async Task<IResult> CancelMyReservationAsync(
        Guid id,
        ClaimsPrincipal principal,
        ReservationService reservations,
        CancellationToken cancellationToken) =>
        Results.Ok(await reservations.CancelForCustomerAsync(id, principal.GetCustomerId()!.Value, cancellationToken));
}
