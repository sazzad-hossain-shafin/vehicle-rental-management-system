using System.Diagnostics;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.Json;
using Scalar.AspNetCore;
using VehicleRental.Api.Endpoints;
using VehicleRental.Api.Http;
using VehicleRental.Api.Security;
using VehicleRental.Application;
using VehicleRental.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// The API is the composition root: built-in dependency injection wires the layers together.
builder.Services.AddApplication();
builder.Services.AddInfrastructure();

// Who you are (a signed bearer token) and what you may do (named policies). Everything requires a
// sign-in unless an endpoint explicitly allows anonymous access.
builder.Services.AddJwtAuthentication();
builder.Services.AddApiAuthorization();

// Enums travel as names ("Car"), and numbers are refused, so clients cannot send meaningless values.
builder.Services.Configure<JsonOptions>(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false)));

builder.Services.AddProblemDetails(options =>
    options.CustomizeProblemDetails = context =>
        context.ProblemDetails.Extensions["traceId"] = Activity.Current?.Id ?? context.HttpContext.TraceIdentifier);
builder.Services.AddExceptionHandler<ApiExceptionHandler>();

builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, _, _) =>
    {
        document.Info.Title = "Vehicle Rental API";
        document.Info.Version = "v1";
        document.Info.Description =
            "Manage a rental fleet: vehicles, customers and rentals. Sign in with /auth/login and send the token as a bearer token.";
        return Task.CompletedTask;
    });

    options.AddBearerSecurity();
});

builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("database", timeout: TimeSpan.FromSeconds(5));
builder.Services.AddHostedService<DatabaseStartupCheck>();
builder.Services.AddHostedService<AdminSeedService>();

builder.WebHost.ConfigureKestrel(options =>
{
    // Request bodies here are tiny JSON documents; refuse anything bigger than 64 KB.
    options.Limits.MaxRequestBodySize = 64 * 1024;

    // Do not advertise the server software.
    options.AddServerHeader = false;
});

var app = builder.Build();

app.Logger.LogInformation("Starting the Vehicle Rental API in the {Environment} environment.", app.Environment.EnvironmentName);

app.Use(async (context, next) =>
{
    // Stops browsers from guessing a different content type than the one we send. Set when the response
    // starts, because the exception handler clears any headers added earlier.
    context.Response.OnStarting(() =>
    {
        context.Response.Headers.XContentTypeOptions = "nosniff";
        return Task.CompletedTask;
    });

    await next();
});

app.UseExceptionHandler();
app.UseStatusCodePages();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    // The API description and its interactive page are for development only.
    app.MapOpenApi().AllowAnonymous();
    app.MapScalarApiReference().AllowAnonymous();
}

app.MapHealthEndpoints();

RouteGroupBuilder v1 = app.MapGroup("/api/v1");
v1.MapAuthEndpoints();
v1.MapMeEndpoints();
v1.MapVehicleEndpoints();
v1.MapCustomerEndpoints();
v1.MapRentalEndpoints();

app.Run();

// Lets the integration tests start the whole application.
public partial class Program;
