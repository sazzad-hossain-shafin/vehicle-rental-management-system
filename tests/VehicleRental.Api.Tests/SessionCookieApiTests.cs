using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using VehicleRental.Api.Tests.Support;

namespace VehicleRental.Api.Tests;

/// <summary>
/// The browser session: sign-in sets an HttpOnly cookie instead of returning the token, the cookie authenticates
/// like a bearer token, and state-changing requests it authenticates need the anti-CSRF header.
/// </summary>
public class SessionCookieApiTests : ApiTestBase
{
    private const string V1 = ApiTestExtensions.V1;
    private const string CsrfHeader = "X-Requested-With";
    private const string CsrfValue = "VehicleRentalWeb";

    public SessionCookieApiTests(PostgresApiFixture api) : base(api)
    {
    }

    /// <summary>A client that does not manage cookies itself, so each test controls exactly what is sent.</summary>
    private HttpClient NewBrowserLikeClient(ApiFactory? factory = null) =>
        (factory ?? Api.Factory).CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

    private static HttpRequestMessage Request(HttpMethod method, string url, object? body = null, string? cookie = null, bool csrf = false)
    {
        var request = new HttpRequestMessage(method, url);

        if (body is not null)
        {
            request.Content = System.Net.Http.Json.JsonContent.Create(body, options: Json.Options);
        }

        if (cookie is not null)
        {
            request.Headers.Add("Cookie", cookie);
        }

        if (csrf)
        {
            request.Headers.Add(CsrfHeader, CsrfValue);
        }

        return request;
    }

    private static string? SetCookie(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var values) ? values.FirstOrDefault(v => v.StartsWith("vr_session=")) : null;

    /// <summary>Registers a customer and signs in through the cookie endpoint; returns the cookie pair to send back.</summary>
    private async Task<(string Cookie, string SetCookieHeader, string Email)> SignInAsync(HttpClient browser, ApiFactory? factory = null)
    {
        var customer = await Api.CreateCustomerClientAsync("Casey Cookie");

        var response = await browser.SendAsync(Request(
            HttpMethod.Post, $"{V1}/auth/session", new { email = customer.Email, password = customer.Password }, csrf: true));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string setCookie = SetCookie(response)!;

        return (setCookie.Split(';')[0], setCookie, customer.Email);
    }

    [DatabaseFact]
    public async Task SigningIn_SetsAnHttpOnlySameSiteStrictCookie_AndNeverReturnsTheToken()
    {
        using var browser = NewBrowserLikeClient();
        var customer = await Api.CreateCustomerClientAsync("Casey Cookie");

        var response = await browser.SendAsync(Request(
            HttpMethod.Post, $"{V1}/auth/session", new { email = customer.Email, password = customer.Password }, csrf: true));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string setCookie = SetCookie(response)!;
        string lower = setCookie.ToLowerInvariant();
        Assert.Contains("httponly", lower);
        Assert.Contains("samesite=strict", lower);
        Assert.Contains("path=/api", lower);
        Assert.Contains("expires=", lower);
        Assert.DoesNotContain("secure", lower.Replace("samesite", ""));   // plain HTTP in tests

        // The body carries who signed in and when the session ends, but not the token.
        string tokenValue = setCookie.Split(';')[0]["vr_session=".Length..];
        Assert.Equal(3, tokenValue.Split('.').Length);                    // a signed JWT
        string body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(tokenValue, body);
        Assert.DoesNotContain("accessToken", body, StringComparison.OrdinalIgnoreCase);

        var json = await response.ReadJsonAsync();
        Assert.Equal(customer.Email, json.GetProperty("user").GetProperty("email").GetString());
        Assert.True(json.TryGetProperty("expiresAtUtc", out _));
    }

    [DatabaseFact]
    public async Task TheCookie_AuthenticatesLikeABearerToken_AndAuthorizationIsUnchanged()
    {
        using var browser = NewBrowserLikeClient();
        var (cookie, _, email) = await SignInAsync(browser);

        var me = await browser.SendAsync(Request(HttpMethod.Get, $"{V1}/me", cookie: cookie));
        var staffEndpoint = await browser.SendAsync(Request(HttpMethod.Get, $"{V1}/rentals", cookie: cookie));
        var own = await browser.SendAsync(Request(HttpMethod.Get, $"{V1}/me/reservations", cookie: cookie));
        var anonymous = await browser.SendAsync(Request(HttpMethod.Get, $"{V1}/me"));

        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        Assert.Equal(email, (await me.ReadJsonAsync()).GetProperty("email").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, staffEndpoint.StatusCode);   // a customer cookie is still only a customer
        Assert.Equal(HttpStatusCode.OK, own.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }

    [DatabaseFact]
    public async Task StateChangingRequests_AuthenticatedByTheCookie_NeedTheAntiCsrfHeader()
    {
        using var browser = NewBrowserLikeClient();
        var (cookie, _, _) = await SignInAsync(browser);
        string url = $"{V1}/me/reservations/{Guid.NewGuid()}/cancel";

        var withoutHeader = await browser.SendAsync(Request(HttpMethod.Post, url, cookie: cookie));
        var wrongHeader = await browser.SendAsync(Request(HttpMethod.Post, url, cookie: cookie).WithHeader(CsrfHeader, "other"));
        var withHeader = await browser.SendAsync(Request(HttpMethod.Post, url, cookie: cookie, csrf: true));
        var readOnly = await browser.SendAsync(Request(HttpMethod.Get, $"{V1}/me/reservations", cookie: cookie));

        var refused = await withoutHeader.AssertProblemAsync(HttpStatusCode.Forbidden);
        Assert.Contains(CsrfHeader, refused.GetProperty("detail").GetString());
        await wrongHeader.AssertProblemAsync(HttpStatusCode.Forbidden);
        await withHeader.AssertProblemAsync(HttpStatusCode.NotFound);   // it got past the check (and the id does not exist)
        Assert.Equal(HttpStatusCode.OK, readOnly.StatusCode);           // safe methods need no header
    }

    [DatabaseFact]
    public async Task BearerRequests_AreNotAffectedByTheCsrfCheck()
    {
        var customer = await Api.CreateCustomerClientAsync("Casey Bearer");

        // Same unsafe request as above but with an Authorization header and no cookie: a browser never adds that header
        // by itself, so no extra proof is needed.
        var response = await customer.Client.PostAsync($"{V1}/me/reservations/{Guid.NewGuid()}/cancel", null);

        await response.AssertProblemAsync(HttpStatusCode.NotFound);
    }

    [DatabaseFact]
    public async Task SignIn_NeedsTheHeader_AndAFailedSignIn_SetsNoCookie()
    {
        using var browser = NewBrowserLikeClient();
        var customer = await Api.CreateCustomerClientAsync("Casey Cookie");

        var noHeader = await browser.SendAsync(Request(
            HttpMethod.Post, $"{V1}/auth/session", new { email = customer.Email, password = customer.Password }));
        var wrongPassword = await browser.SendAsync(Request(
            HttpMethod.Post, $"{V1}/auth/session", new { email = customer.Email, password = "Wrong-pass-1" }, csrf: true));
        var unknownEmail = await browser.SendAsync(Request(
            HttpMethod.Post, $"{V1}/auth/session", new { email = "nobody@example.test", password = "Wrong-pass-1" }, csrf: true));

        await noHeader.AssertProblemAsync(HttpStatusCode.Forbidden);
        Assert.Null(SetCookie(noHeader));

        var wrong = await wrongPassword.AssertProblemAsync(HttpStatusCode.Unauthorized);
        var unknown = await unknownEmail.AssertProblemAsync(HttpStatusCode.Unauthorized);
        Assert.Null(SetCookie(wrongPassword));
        Assert.Equal(wrong.GetProperty("detail").GetString(), unknown.GetProperty("detail").GetString());   // same failure either way
    }

    [DatabaseFact]
    public async Task SigningOut_ClearsTheCookie_AndNeedsTheHeader()
    {
        using var browser = NewBrowserLikeClient();
        var (cookie, _, _) = await SignInAsync(browser);

        var noHeader = await browser.SendAsync(Request(HttpMethod.Delete, $"{V1}/auth/session", cookie: cookie));
        var signedOut = await browser.SendAsync(Request(HttpMethod.Delete, $"{V1}/auth/session", cookie: cookie, csrf: true));

        await noHeader.AssertProblemAsync(HttpStatusCode.Forbidden);
        Assert.Equal(HttpStatusCode.NoContent, signedOut.StatusCode);

        string cleared = SetCookie(signedOut)!.ToLowerInvariant();
        Assert.StartsWith("vr_session=;", cleared);                           // empty value
        Assert.Contains("expires=thu, 01 jan 1970", cleared);                 // in the past, so the browser drops it
        Assert.Contains("path=/api", cleared);
        Assert.Contains("httponly", cleared);
    }

    [DatabaseFact]
    public async Task AGarbageOrTamperedCookie_IsRejected()
    {
        using var browser = NewBrowserLikeClient();
        var (cookie, _, _) = await SignInAsync(browser);
        string token = cookie["vr_session=".Length..];
        string tampered = token[..^3] + (token.EndsWith("AAA") ? "BBB" : "AAA");   // change the signature

        foreach (string bad in new[] { "vr_session=garbage", "vr_session=a.b.c", $"vr_session={tampered}", "vr_session=" })
        {
            var response = await browser.SendAsync(Request(HttpMethod.Get, $"{V1}/me", cookie: bad));

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }

    [DatabaseFact]
    public async Task ABearerHeader_TakesPrecedenceOverTheCookie()
    {
        using var browser = NewBrowserLikeClient();
        var (cookie, _, cookieEmail) = await SignInAsync(browser);
        var other = await Api.CreateCustomerClientAsync("Other Person");

        var request = Request(HttpMethod.Get, $"{V1}/me", cookie: cookie);
        request.Headers.Authorization = other.Client.DefaultRequestHeaders.Authorization;
        var response = await browser.SendAsync(request);

        Assert.Equal(other.Email, (await response.ReadJsonAsync()).GetProperty("email").GetString());
        Assert.NotEqual(cookieEmail, other.Email);
    }

    [DatabaseFact]
    public async Task TheSecureFlag_CanBeForced_ForDeploymentsBehindATlsProxy()
    {
        await using var secure = new ApiFactory(
            Api.ConnectionString,
            signingKey: Api.Factory.SigningKey,
            extraSettings: new Dictionary<string, string?> { ["Session:ForceSecureCookie"] = "true" });
        using var browser = NewBrowserLikeClient(secure);
        var customer = await Api.CreateCustomerClientAsync("Casey Cookie");

        var response = await browser.SendAsync(Request(
            HttpMethod.Post, $"{V1}/auth/session", new { email = customer.Email, password = customer.Password }, csrf: true));

        Assert.Contains("secure", SetCookie(response)!.ToLowerInvariant().Replace("samesite", ""));
    }
}

internal static class RequestExtensions
{
    public static HttpRequestMessage WithHeader(this HttpRequestMessage request, string name, string value)
    {
        request.Headers.Add(name, value);

        return request;
    }
}
