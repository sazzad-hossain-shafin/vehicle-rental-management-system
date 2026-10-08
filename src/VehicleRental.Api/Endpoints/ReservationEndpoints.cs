using VehicleRental.Api.Contracts;
using VehicleRental.Api.Http;
using VehicleRental.Api.Security;
using VehicleRental.Application;
using VehicleRental.Application.Reservations;
using VehicleRental.Domain.Enums;

namespace VehicleRental.Api.Endpoints;

/// <summary>
/// The rental desk's view of reservations (staff and admin). Customers use the <c>/me/reservations</c> endpoints,
/// which take the customer from the signed token and never from client input.
/// </summary>
internal static class ReservationEndpoints
{
    public static IEndpointRouteBuilder MapReservationEndpoints(this IEndpointRouteBuilder routes)
    {
        RouteGroupBuilder group = routes.MapGroup("/reservations").WithTags("Reservations");

        group.MapPost("/", CreateReservationAsync)
            .WithName("CreateReservation")
            .RequireAuthorization(Policies.ReservationManage)
            .WithSummary("Reserves a vehicle for a customer (desk booking)")
            .WithDescription(
                "The period is the half-open interval [startDate, endDate): the vehicle is held on the start date " +
                "and free again on the end date. The price is quoted now and stored.")
            .WithRequestValidation<CreateDeskReservationRequest>()
            .Produces<ReservationDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/", ListReservationsAsync)
            .WithName("ListReservations")
            .RequireAuthorization(Policies.ReservationManage)
            .WithSummary("Lists all reservations, one page at a time")
            .WithDescription("Earliest start date first. Optionally filter by status.")
            .Produces<PagedResult<ReservationDto>>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapGet("/{id}", GetReservationAsync)
            .WithName("GetReservationById")
            .RequireAuthorization(Policies.ReservationManage)
            .WithSummary("Gets a reservation by its ID")
            .Produces<ReservationDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        // Cancelling and picking up are state changes with rules, not edits of a status field, so they are
        // explicit actions: clients cannot set arbitrary states.
        group.MapPost("/{id}/cancel", CancelReservationAsync)
            .WithName("CancelReservation")
            .RequireAuthorization(Policies.ReservationManage)
            .WithSummary("Cancels an active reservation")
            .Produces<ReservationDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{id}/pickup", PickUpReservationAsync)
            .WithName("PickUpReservation")
            .RequireAuthorization(Policies.ReservationManage)
            .WithSummary("Hands the vehicle over: starts the rental and fulfils the reservation")
            .WithDescription(
                "Possible from the start date until the day before the end date, and only while the vehicle is " +
                "back at the desk. The rental honours the reserved quote. The rental, the vehicle status and the " +
                "reservation are saved together, and a reservation can be picked up only once.")
            .Produces<PickupResultDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return routes;
    }

    private static async Task<IResult> CreateReservationAsync(
        CreateDeskReservationRequest request,
        ReservationService reservations,
        CancellationToken cancellationToken)
    {
        ReservationDto reservation = await reservations.CreateAsync(
            new CreateReservationRequest(
                request.CustomerId!.Value,
                request.VehicleId!.Value,
                request.StartDate!.Value,
                request.EndDate!.Value,
                request.PromotionalDiscountRequested),
            cancellationToken);

        return Results.CreatedAtRoute("GetReservationById", new { id = reservation.Id }, reservation);
    }

    private static async Task<IResult> ListReservationsAsync(
        [AsParameters] ReservationListQuery query,
        ReservationService reservations,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();

        ReservationStatus? status = VehicleEndpoints.ParseFilter<ReservationStatus>(query.Status, "status", errors);

        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        return Results.Ok(await reservations.GetPageAsync(status, query.Page, query.PageSize, cancellationToken));
    }

    private static async Task<IResult> GetReservationAsync(
        Guid id,
        ReservationService reservations,
        CancellationToken cancellationToken) =>
        Results.Ok(await reservations.GetAsync(id, cancellationToken));

    private static async Task<IResult> CancelReservationAsync(
        Guid id,
        ReservationService reservations,
        CancellationToken cancellationToken) =>
        Results.Ok(await reservations.CancelAsync(id, cancellationToken));

    private static async Task<IResult> PickUpReservationAsync(
        Guid id,
        ReservationService reservations,
        CancellationToken cancellationToken) =>
        Results.Ok(await reservations.PickUpAsync(id, cancellationToken));
}
