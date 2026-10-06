using System.ComponentModel.DataAnnotations;

namespace VehicleRental.Api.Http;

/// <summary>
/// Checks the shape of a request body (required fields present) before the endpoint runs. It does
/// not repeat business rules such as "the rate must be positive"; those belong to the domain and
/// come back as 400 responses through the exception handler.
/// </summary>
internal sealed class RequestValidationFilter<TRequest> : IEndpointFilter
    where TRequest : class
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        TRequest? request = context.Arguments.OfType<TRequest>().FirstOrDefault();

        if (request is not null)
        {
            var results = new List<ValidationResult>();

            if (!Validator.TryValidateObject(request, new ValidationContext(request), results, validateAllProperties: true))
            {
                var errors = results
                    .SelectMany(r => (r.MemberNames.Any() ? r.MemberNames : [""]).Select(member => (member, r.ErrorMessage)))
                    .GroupBy(e => ToCamelCase(e.member))
                    .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage ?? "Invalid value.").ToArray());

                return Results.ValidationProblem(errors);
            }
        }

        return await next(context);
    }

    private static string ToCamelCase(string name) =>
        name.Length == 0 ? name : char.ToLowerInvariant(name[0]) + name[1..];
}

internal static class RequestValidationExtensions
{
    public static RouteHandlerBuilder WithRequestValidation<TRequest>(this RouteHandlerBuilder builder)
        where TRequest : class =>
        builder
            .AddEndpointFilter<RequestValidationFilter<TRequest>>()
            .ProducesValidationProblem();
}
