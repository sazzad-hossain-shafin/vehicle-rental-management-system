using VehicleRental.Api.Contracts;
using VehicleRental.Api.Http;
using VehicleRental.Api.Security;
using VehicleRental.Application.Customers;

namespace VehicleRental.Api.Endpoints;

internal static class CustomerEndpoints
{
    public static IEndpointRouteBuilder MapCustomerEndpoints(this IEndpointRouteBuilder routes)
    {
        RouteGroupBuilder group = routes.MapGroup("/customers").WithTags("Customers");

        group.MapGet("/{id}", GetCustomerAsync)
            .WithName("GetCustomerById")
            .RequireAuthorization(Policies.CustomerManage)
            .WithSummary("Gets a customer by their ID")
            .Produces<CustomerDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/by-number/{customerNumber}", GetCustomerByNumberAsync)
            .WithName("GetCustomerByNumber")
            .RequireAuthorization(Policies.CustomerManage)
            .WithSummary("Gets a customer by their customer number")
            .WithDescription("Case-insensitive. Use the customer's ID for everything else.")
            .Produces<CustomerDto>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/", CreateCustomerAsync)
            .WithName("CreateCustomer")
            .RequireAuthorization(Policies.CustomerManage)
            .WithSummary("Registers a customer")
            .WithDescription("The customer number must be unique; creating the same number twice is a conflict.")
            .WithRequestValidation<CreateCustomerRequest>()
            .Produces<CustomerDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return routes;
    }

    private static async Task<IResult> GetCustomerAsync(
        Guid id,
        CustomerService customers,
        CancellationToken cancellationToken) =>
        Results.Ok(await customers.GetByIdAsync(id, cancellationToken));

    private static async Task<IResult> GetCustomerByNumberAsync(
        string customerNumber,
        CustomerService customers,
        CancellationToken cancellationToken) =>
        Results.Ok(await customers.GetByCustomerNumberAsync(customerNumber, cancellationToken));

    private static async Task<IResult> CreateCustomerAsync(
        CreateCustomerRequest request,
        CustomerService customers,
        CancellationToken cancellationToken)
    {
        CustomerDto customer = await customers.CreateAsync(
            request.CustomerNumber!,
            request.Name!,
            cancellationToken);

        return Results.CreatedAtRoute("GetCustomerById", new { id = customer.Id }, customer);
    }
}
