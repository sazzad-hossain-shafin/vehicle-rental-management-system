using VehicleRental.Api.Contracts;
using VehicleRental.Api.Http;
using VehicleRental.Api.Security;
using VehicleRental.Application.Accounts;

namespace VehicleRental.Api.Endpoints;

internal static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder routes)
    {
        RouteGroupBuilder auth = routes.MapGroup("/auth").WithTags("Authentication");

        auth.MapPost("/login", LoginAsync)
            .WithName("Login")
            .WithSummary("Signs in and returns an access token")
            .WithDescription(
                "Every failure (unknown email, wrong password, locked account) gets the same 401 response, " +
                "so it does not reveal which accounts exist. Repeated failures lock the account for a while.")
            .AllowAnonymous()
            .WithRequestValidation<LoginRequest>()
            .Produces<LoginResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        auth.MapPost("/session", StartSessionAsync)
            .WithName("StartSession")
            .WithSummary("Signs in a browser: sets an HttpOnly session cookie")
            .WithDescription(
                "Same credentials and the same 401 behaviour as /auth/login, but the access token is returned only " +
                "as an HttpOnly, SameSite=Strict cookie that scripts cannot read. Requires the " +
                "X-Requested-With: VehicleRentalWeb header. Meant for a web client served from the same origin.")
            .AllowAnonymous()
            .RequireCsrfHeader()
            .WithRequestValidation<LoginRequest>()
            .Produces<SessionResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        auth.MapDelete("/session", EndSession)
            .WithName("EndSession")
            .WithSummary("Signs a browser out: clears the session cookie")
            .WithDescription(
                "Tokens are stateless and cannot be revoked, so this removes the cookie from the browser; a copy of " +
                "the token would stay valid until it expires (30 minutes). Requires the X-Requested-With header.")
            .AllowAnonymous()
            .RequireCsrfHeader()
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        auth.MapPost("/register", RegisterCustomerAsync)
            .WithName("RegisterCustomer")
            .WithSummary("Creates a customer account")
            .WithDescription(
                "Creates a new customer record and a login linked to it, together. The account always has the " +
                "Customer role; staff and admin accounts cannot be created here.")
            .AllowAnonymous()
            .WithRequestValidation<RegisterCustomerRequest>()
            .Produces<UserDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict);

        RouteGroupBuilder admin = routes.MapGroup("/admin").WithTags("Administration");

        admin.MapPost("/staff", CreateStaffAsync)
            .WithName("CreateStaffAccount")
            .WithSummary("Creates a staff account")
            .WithDescription("Admins only. The new account has the Staff role.")
            .RequireAuthorization(Policies.UserAdministration)
            .WithRequestValidation<CreateStaffRequest>()
            .Produces<UserDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return routes;
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest request,
        IAccountService accounts,
        CancellationToken cancellationToken)
    {
        LoginResult? result = await accounts.LoginAsync(request.Email!, request.Password!, cancellationToken);

        if (result is null)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Authentication failed.",
                detail: "The email or password is incorrect.");
        }

        return Results.Ok(new LoginResponse(result.AccessToken, "Bearer", result.ExpiresAtUtc, result.User));
    }

    private static async Task<IResult> StartSessionAsync(
        LoginRequest request,
        HttpContext http,
        IAccountService accounts,
        CancellationToken cancellationToken)
    {
        LoginResult? result = await accounts.LoginAsync(request.Email!, request.Password!, cancellationToken);

        if (result is null)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Authentication failed.",
                detail: "The email or password is incorrect.");
        }

        SessionCookie.Append(http, result.AccessToken, result.ExpiresAtUtc);

        return Results.Ok(new SessionResponse(result.ExpiresAtUtc, result.User));
    }

    private static IResult EndSession(HttpContext http)
    {
        SessionCookie.Delete(http);

        return Results.NoContent();
    }

    private static async Task<IResult> RegisterCustomerAsync(
        RegisterCustomerRequest request,
        IAccountService accounts,
        CancellationToken cancellationToken)
    {
        UserDto user = await accounts.RegisterCustomerAsync(
            request.Email!,
            request.Password!,
            request.Name!,
            cancellationToken);

        return Results.Json(user, statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> CreateStaffAsync(
        CreateStaffRequest request,
        IAccountService accounts,
        CancellationToken cancellationToken)
    {
        UserDto user = await accounts.CreateStaffAsync(request.Email!, request.Password!, cancellationToken);

        return Results.Json(user, statusCode: StatusCodes.Status201Created);
    }
}
