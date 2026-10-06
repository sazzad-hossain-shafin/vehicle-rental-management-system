using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace VehicleRental.Api.Security;

internal static class OpenApiSecurity
{
    private const string SchemeName = "Bearer";

    /// <summary>
    /// Describes the bearer-token scheme and marks every protected operation with it, so the interactive
    /// documentation offers a token box and shows which operations need one.
    /// </summary>
    public static OpenApiOptions AddBearerSecurity(this OpenApiOptions options)
    {
        options.AddDocumentTransformer((document, _, _) =>
        {
            document.Components ??= new OpenApiComponents();
            document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
            document.Components.SecuritySchemes[SchemeName] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                Description = "Sign in with POST /api/v1/auth/login, then paste the returned accessToken here."
            };

            return Task.CompletedTask;
        });

        options.AddOperationTransformer((operation, context, _) =>
        {
            var metadata = context.Description.ActionDescriptor.EndpointMetadata;

            // Everything not explicitly anonymous is protected (the fallback policy requires a sign-in).
            if (metadata.OfType<IAllowAnonymous>().Any())
            {
                return Task.CompletedTask;
            }

            operation.Security ??= new List<OpenApiSecurityRequirement>();
            operation.Security.Add(new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference(SchemeName, context.Document)] = new List<string>()
            });

            operation.Responses ??= new OpenApiResponses();
            operation.Responses.TryAdd("401", new OpenApiResponse { Description = "Not signed in: the bearer token is missing, invalid or expired." });

            bool restrictedToRoles = metadata.OfType<IAuthorizeData>().Any(a => a.Policy is not null || a.Roles is not null);

            if (restrictedToRoles)
            {
                operation.Responses.TryAdd("403", new OpenApiResponse { Description = "Signed in, but this account's role is not allowed to do this." });
            }

            return Task.CompletedTask;
        });

        return options;
    }
}
