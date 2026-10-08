namespace VehicleRental.Api.Security;

/// <summary>
/// The browser session: the same signed access token the bearer flow uses, held in an <c>HttpOnly</c> cookie so
/// that JavaScript on the page can never read it. The cookie is <c>SameSite=Strict</c>, scoped to <c>/api</c>, and
/// expires with the token. It is meant for a web client served from the same origin as the API.
/// </summary>
/// <remarks>
/// Because a browser attaches cookies automatically, state-changing requests that were authenticated by the
/// cookie must also carry the <see cref="CsrfHeaderName"/> header. A cross-site page cannot add a custom header
/// without a CORS preflight, and this API allows no cross-origin requests, so the header proves the request came
/// from a script on this origin. Requests authenticated with an <c>Authorization: Bearer</c> header are not
/// affected (a browser never attaches that header by itself).
/// </remarks>
internal static class SessionCookie
{
    public const string Name = "vr_session";

    public const string CsrfHeaderName = "X-Requested-With";

    public const string CsrfHeaderValue = "VehicleRentalWeb";

    /// <summary>Marks a request that was authenticated by the session cookie rather than a bearer header.</summary>
    public const string UsedKey = "vr.session-cookie-used";

    /// <summary>Configuration key: force the <c>Secure</c> flag (for example behind a proxy that ends TLS).</summary>
    public const string ForceSecureSetting = "Session:ForceSecureCookie";

    public static void Append(HttpContext context, string token, DateTimeOffset expiresAtUtc) =>
        context.Response.Cookies.Append(Name, token, Options(context, expiresAtUtc));

    public static void Delete(HttpContext context) =>
        context.Response.Cookies.Delete(Name, Options(context, expiresAtUtc: null));

    public static bool HasCsrfHeader(HttpRequest request) =>
        request.Headers.TryGetValue(CsrfHeaderName, out var value) && value == CsrfHeaderValue;

    public static bool IsSafeMethod(string method) =>
        HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method) || HttpMethods.IsTrace(method);

    private static CookieOptions Options(HttpContext context, DateTimeOffset? expiresAtUtc)
    {
        bool forceSecure = context.RequestServices.GetRequiredService<IConfiguration>().GetValue<bool>(ForceSecureSetting);

        return new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Strict,
            Path = "/api",
            IsEssential = true,

            // Secure whenever the request itself is HTTPS (or it is forced). Plain HTTP is for local development only.
            Secure = forceSecure || context.Request.IsHttps,
            Expires = expiresAtUtc
        };
    }
}

internal static class SessionCookieExtensions
{
    /// <summary>
    /// Requires the anti-CSRF header on the endpoint, whatever way the request is authenticated. Used on sign-in and
    /// sign-out, which change the session cookie.
    /// </summary>
    public static RouteHandlerBuilder RequireCsrfHeader(this RouteHandlerBuilder builder) =>
        builder.AddEndpointFilter(async (context, next) =>
            SessionCookie.HasCsrfHeader(context.HttpContext.Request)
                ? await next(context)
                : Results.Problem(
                    statusCode: StatusCodes.Status403Forbidden,
                    title: "The request was not accepted.",
                    detail: $"The {SessionCookie.CsrfHeaderName} header is required."));
}
