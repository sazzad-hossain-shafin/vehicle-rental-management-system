using VehicleRental.Api.Contracts;
using VehicleRental.Api.Http;
using VehicleRental.Application;
using VehicleRental.Application.Abstractions;
using VehicleRental.Application.Vehicles;
using VehicleRental.Domain.Enums;

namespace VehicleRental.Api.Endpoints;

internal static class VehicleEndpoints
{
    public static IEndpointRouteBuilder MapVehicleEndpoints(this IEndpointRouteBuilder routes)
    {
        RouteGroupBuilder group = routes.MapGroup("/vehicles").WithTags("Vehicles");

        group.MapGet("/", ListVehiclesAsync)
            .WithName("ListVehicles")
            .WithSummary("Lists vehicles, one page at a time")
            .WithDescription("Filters are optional and combine. Results are ordered by registration number.")
            .Produces<PagedResult<VehicleDto>>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapGet("/{id}", GetVehicleAsync)
            .WithName("GetVehicleById")
            .WithSummary("Gets a vehicle by its ID")
            .Produces<VehicleDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/by-registration/{registrationNumber}", GetVehicleByRegistrationAsync)
            .WithName("GetVehicleByRegistrationNumber")
            .WithSummary("Gets a vehicle by its registration number")
            .WithDescription("Case-insensitive. Use the vehicle's ID for everything else.")
            .Produces<VehicleDto>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/", CreateVehicleAsync)
            .WithName("CreateVehicle")
            .WithSummary("Adds a vehicle to the fleet")
            .WithDescription("The new vehicle is available. The registration number must be unique.")
            .WithRequestValidation<CreateVehicleRequest>()
            .Produces<VehicleDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return routes;
    }

    private static async Task<IResult> ListVehiclesAsync(
        [AsParameters] VehicleListQuery query,
        VehicleService vehicles,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();

        VehicleType? type = ParseFilter<VehicleType>(query.VehicleType, "vehicleType", errors);
        VehicleAvailabilityStatus? availability =
            ParseFilter<VehicleAvailabilityStatus>(query.Availability, "availability", errors);

        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var criteria = new VehicleSearchCriteria(type, query.MaxDailyRate, availability);

        return Results.Ok(await vehicles.SearchPageAsync(criteria, query.Page, query.PageSize, cancellationToken));
    }

    /// <summary>
    /// Reads an optional enum filter by name, ignoring letter case, like enums in JSON bodies. Numbers
    /// (which would parse to meaningless values) and unknown names are reported as validation errors.
    /// </summary>
    private static TEnum? ParseFilter<TEnum>(string? value, string name, Dictionary<string, string[]> errors)
        where TEnum : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string text = value.Trim();

        if (Enum.GetNames<TEnum>().Any(n => n.Equals(text, StringComparison.OrdinalIgnoreCase))
            && Enum.TryParse(text, ignoreCase: true, out TEnum parsed))
        {
            return parsed;
        }

        errors[name] = [$"Must be one of: {string.Join(", ", Enum.GetNames<TEnum>())}."];

        return null;
    }

    private static async Task<IResult> GetVehicleAsync(
        Guid id,
        VehicleService vehicles,
        CancellationToken cancellationToken) =>
        Results.Ok(await vehicles.GetByIdAsync(id, cancellationToken));

    private static async Task<IResult> GetVehicleByRegistrationAsync(
        string registrationNumber,
        VehicleService vehicles,
        CancellationToken cancellationToken) =>
        Results.Ok(await vehicles.GetByRegistrationNumberAsync(registrationNumber, cancellationToken));

    private static async Task<IResult> CreateVehicleAsync(
        CreateVehicleRequest request,
        VehicleService vehicles,
        CancellationToken cancellationToken)
    {
        VehicleDto vehicle = await vehicles.AddVehicleAsync(
            new AddVehicleRequest(
                request.RegistrationNumber!,
                request.Make!,
                request.Model!,
                request.Year!.Value,
                request.VehicleType!.Value,
                request.DailyRate!.Value),
            cancellationToken);

        return Results.CreatedAtRoute("GetVehicleById", new { id = vehicle.Id }, vehicle);
    }
}
