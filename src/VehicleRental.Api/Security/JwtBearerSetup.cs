using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using VehicleRental.Application.Security;
using VehicleRental.Infrastructure.Identity;

namespace VehicleRental.Api.Security;

internal static class JwtBearerSetup
{
    /// <summary>
    /// Bearer-token authentication. A token is accepted only if its signature, issuer, audience and expiry
    /// are all valid, and only with the one signing algorithm we use. The settings are read from
    /// <see cref="JwtOptions"/>, which is validated when the application starts.
    /// </summary>
    public static IServiceCollection AddJwtAuthentication(this IServiceCollection services)
    {
        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer();

        services
            .AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((bearer, jwtOptions) =>
            {
                JwtOptions jwt = jwtOptions.Value;

                // Keep the claim names exactly as issued ("sub", "role") instead of renaming them.
                bearer.MapInboundClaims = false;

                // A browser session: with no Authorization header, take the same token from the HttpOnly session
                // cookie. A bearer header always wins, so API clients are unaffected. The request is marked so the
                // anti-CSRF check knows it was authenticated by a cookie the browser attached by itself.
                bearer.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        if (string.IsNullOrEmpty(context.Request.Headers.Authorization)
                            && context.Request.Cookies.TryGetValue(SessionCookie.Name, out string? token)
                            && !string.IsNullOrEmpty(token))
                        {
                            context.Token = token;
                            context.HttpContext.Items[SessionCookie.UsedKey] = true;
                        }

                        return Task.CompletedTask;
                    }
                };

                bearer.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwt.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    RequireSignedTokens = true,
                    ValidateLifetime = true,
                    RequireExpirationTime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = AuthClaims.Subject,
                    RoleClaimType = AuthClaims.Role
                };
            });

        return services;
    }
}

internal static class CurrentUserExtensions
{
    /// <summary>The signed-in account's ID, from the verified token.</summary>
    public static Guid? GetUserId(this ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue(AuthClaims.Subject), out Guid id) ? id : null;

    /// <summary>
    /// The customer a customer account belongs to, from the verified token. This is the only trusted source of
    /// "who is this customer"; an ID supplied in a URL or body is never proof of ownership.
    /// </summary>
    public static Guid? GetCustomerId(this ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue(AuthClaims.CustomerId), out Guid id) ? id : null;

    public static bool IsStaffOrAdmin(this ClaimsPrincipal user) =>
        user.IsInRole(Roles.Staff) || user.IsInRole(Roles.Admin);
}
