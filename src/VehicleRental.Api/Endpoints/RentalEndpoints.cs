using System.Security.Claims;
using VehicleRental.Api.Contracts;
using VehicleRental.Api.Http;
using VehicleRental.Api.Security;
using VehicleRental.Application;
using VehicleRental.Application.Rentals;

namespace VehicleRental.Api.Endpoints;

internal static class RentalEndpoints
{
    public static IEndpointRouteBuilder MapRentalEndpoints(this IEndpointRouteBuilder routes)
    {
        RouteGroupBuilder group = routes.MapGroup("/rentals").WithTags("Rentals");

        group.MapPost("/", StartRentalAsync)
            .WithName("StartRental")
            .RequireAuthorization(Policies.RentalManage)
            .WithSummary("Starts a rental today")
            .WithDescription(
                "Prices the rental with the pricing policy (long-term discount from 7 days, otherwise normal " +
                "or the optional promotion) and stores the agreed total. The vehicle becomes rented.")
            .WithRequestValidation<StartRentalApiRequest>()
            .Produces<RentalDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/", ListRentalsAsync)
            .WithName("ListRentals")
            .RequireAuthorization(Policies.RentalManage)
            .WithSummary("Lists the rental history, one page at a time")
            .WithDescription("Oldest start date first. Includes active and completed rentals.")
            .Produces<PagedResult<RentalDto>>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapGet("/{id}", GetRentalAsync)
            .WithName("GetRentalById")
            .RequireAuthorization()
            .WithSummary("Gets a rental by its ID")
            .Produces<RentalDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        // Returning is a state change with rules, not an edit of a field, so it is an explicit action
        // on the rental rather than a PATCH of its status: clients cannot set arbitrary states.
        group.MapPost("/{id}/return", ReturnRentalAsync)
            .WithName("ReturnRental")
            .RequireAuthorization(Policies.RentalManage)
            .WithSummary("Completes a rental")
            .WithDescription(
                "Records today as the return date and makes the vehicle available. The agreed total does " +
                "not change. Returning a completed rental is a conflict.")
            .Produces<RentalDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return routes;
    }

    private static async Task<IResult> StartRentalAsync(
        StartRentalApiRequest request,
        RentalService rentals,
        CancellationToken cancellationToken)
    {
        RentalDto rental = await rentals.StartRentalAsync(
            new StartRentalByIdRequest(
                request.VehicleId!.Value,
                request.CustomerId!.Value,
                request.RentalDays!.Value,
                request.PromotionalDiscountRequested),
            cancellationToken);

        return Results.CreatedAtRoute("GetRentalById", new { id = rental.Id }, rental);
    }

    private static async Task<IResult> ListRentalsAsync(
        [AsParameters] PageQuery query,
        RentalService rentals,
        CancellationToken cancellationToken) =>
        Results.Ok(await rentals.GetRentalHistoryPageAsync(query.Page, query.PageSize, cancellationToken));

    /// <summary>
    /// Staff and admins can read any rental. A customer can read only their own: the customer comes from the
    /// verified token, and a rental that belongs to someone else is answered exactly like one that does not
    /// exist (404), so an ID from the URL can neither expose nor confirm another customer's rental.
    /// </summary>
    private static async Task<IResult> GetRentalAsync(
        Guid id,
        ClaimsPrincipal user,
        RentalService rentals,
        CancellationToken cancellationToken)
    {
        if (user.IsStaffOrAdmin())
        {
            return Results.Ok(await rentals.GetRentalAsync(id, cancellationToken));
        }

        if (user.GetCustomerId() is { } customerId)
        {
            return Results.Ok(await rentals.GetCustomerRentalAsync(id, customerId, cancellationToken));
        }

        return Results.Forbid();
    }

    private static async Task<IResult> ReturnRentalAsync(
        Guid id,
        RentalService rentals,
        CancellationToken cancellationToken) =>
        Results.Ok(await rentals.ReturnRentalAsync(id, cancellationToken));
}
