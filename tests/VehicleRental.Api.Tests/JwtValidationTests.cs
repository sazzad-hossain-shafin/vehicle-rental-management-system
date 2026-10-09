using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using VehicleRental.Api.Tests.Support;
using VehicleRental.Application.Security;

namespace VehicleRental.Api.Tests;

/// <summary>
/// What the API accepts as proof of sign-in. Each test presents a token that is wrong in exactly one way
/// (or correct, as a control) to a staff-only endpoint. No database is needed.
/// </summary>
public class JwtValidationTests : IDisposable
{
    private const string Protected = "/api/v1/rentals"; // staff/admin only

    private readonly ApiFactory _factory = new(ApiFactory.UnreachableDatabase);

    public void Dispose() => _factory.Dispose();

    private async Task<HttpResponseMessage> CallWithAsync(string? authorizationHeader)
    {
        using var client = _factory.CreateClient();

        if (authorizationHeader is not null)
        {
            client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", authorizationHeader);
        }

        return await client.GetAsync(Protected);
    }

    private Task<HttpResponseMessage> CallWithTokenAsync(string token) => CallWithAsync("Bearer " + token);

    private string Token(
        string? signingKey = null,
        string[]? roles = null,
        string issuer = ApiFactory.Issuer,
        string audience = ApiFactory.Audience,
        DateTime? issuedAt = null,
        TimeSpan? lifetime = null,
        string algorithm = SecurityAlgorithms.HmacSha256) =>
        TestTokens.Create(signingKey ?? _factory.SigningKey, roles ?? [Roles.Staff], issuer: issuer, audience: audience,
            issuedAt: issuedAt, lifetime: lifetime, algorithm: algorithm);

    private static async Task AssertRejectedAsync(HttpResponseMessage response)
    {
        var problem = await response.AssertProblemAsync(HttpStatusCode.Unauthorized);

        // The reason is deliberately not explained to the caller.
        string text = problem.GetRawText();
        Assert.DoesNotContain("IDX", text);
        Assert.DoesNotContain("signature", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("expired", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("issuer", text, StringComparison.OrdinalIgnoreCase);
    }

    // ----- Control -----

    [Fact]
    public async Task AValidToken_IsAccepted()
    {
        var response = await CallWithTokenAsync(Token());

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ----- Missing or malformed -----

    [Fact]
    public async Task NoAuthorizationHeader_Is401() => await AssertRejectedAsync(await CallWithAsync(null));

    [Theory]
    [InlineData("Bearer ")]
    [InlineData("Bearer not-a-token")]
    [InlineData("Bearer a.b.c")]
    [InlineData("Bearer eyJhbGciOiJIUzI1NiJ9.e30.")]
    [InlineData("Basic dXNlcjpwYXNz")]
    [InlineData("Token abc")]
    public async Task MalformedOrWrongSchemeCredentials_Are401(string header) =>
        await AssertRejectedAsync(await CallWithAsync(header));

    [Fact]
    public async Task ATokenWithTheBearerWordInTheWrongCase_IsStillAHeaderWeUnderstand()
    {
        // Authentication schemes are case-insensitive (RFC 9110), so this is a valid token and is accepted.
        var response = await CallWithAsync("bearer " + Token());

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ----- Tampering and forgery -----

    [Fact]
    public async Task ATokenSignedWithAnotherKey_Is401() =>
        await AssertRejectedAsync(await CallWithTokenAsync(Token(signingKey: ApiFactory.NewRandomKey())));

    [Fact]
    public async Task AnAdminTokenForgedWithAnotherKey_Is401_NotAPrivilegeEscalation() =>
        await AssertRejectedAsync(await CallWithTokenAsync(Token(signingKey: ApiFactory.NewRandomKey(), roles: [Roles.Admin])));

    [Fact]
    public async Task ATokenWhosePayloadWasEditedAfterSigning_Is401()
    {
        string customerToken = Token(roles: [Roles.Customer]);
        string[] parts = customerToken.Split('.');

        // Swap the payload for one that claims the Admin role, keeping the original signature.
        string payload = Encoding.UTF8.GetString(Base64UrlEncoder.DecodeBytes(parts[1]));
        string edited = payload.Replace("\"Customer\"", "\"Admin\"");
        Assert.NotEqual(payload, edited);
        string tampered = $"{parts[0]}.{Base64UrlEncoder.Encode(edited)}.{parts[2]}";

        await AssertRejectedAsync(await CallWithTokenAsync(tampered));
    }

    [Fact]
    public async Task AnUnsignedToken_WithAlgorithmNone_Is401()
    {
        string header = Base64UrlEncoder.Encode("{\"alg\":\"none\",\"typ\":\"JWT\"}");
        long exp = DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeSeconds();
        string payload = Base64UrlEncoder.Encode(JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["sub"] = Guid.NewGuid().ToString(),
            ["role"] = Roles.Admin,
            ["iss"] = ApiFactory.Issuer,
            ["aud"] = ApiFactory.Audience,
            ["exp"] = exp
        }));

        await AssertRejectedAsync(await CallWithTokenAsync($"{header}.{payload}."));
    }

    [Fact]
    public async Task ATokenUsingADifferentSigningAlgorithm_Is401_EvenWithTheRightKey() =>
        await AssertRejectedAsync(await CallWithTokenAsync(Token(algorithm: SecurityAlgorithms.HmacSha512)));

    // ----- Claims that must match -----

    [Fact]
    public async Task AWrongIssuer_Is401() =>
        await AssertRejectedAsync(await CallWithTokenAsync(Token(issuer: "https://evil.example")));

    [Fact]
    public async Task AWrongAudience_Is401() =>
        await AssertRejectedAsync(await CallWithTokenAsync(Token(audience: "someone-else")));

    // ----- Time -----

    [Fact]
    public async Task AnExpiredToken_Is401() =>
        await AssertRejectedAsync(await CallWithTokenAsync(Token(
            issuedAt: DateTime.UtcNow.AddMinutes(-20), lifetime: TimeSpan.FromMinutes(10))));

    [Fact]
    public async Task ATokenThatExpiredAFewSecondsAgo_IsWithinTheSmallClockAllowance()
    {
        // 30 seconds of clock skew is tolerated between servers; a token 10 seconds past its expiry still works.
        var response = await CallWithTokenAsync(Token(
            issuedAt: DateTime.UtcNow.AddMinutes(-10).AddSeconds(-10), lifetime: TimeSpan.FromMinutes(10)));

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ATokenThatExpiredMoreThanTheAllowanceAgo_Is401() =>
        await AssertRejectedAsync(await CallWithTokenAsync(Token(
            issuedAt: DateTime.UtcNow.AddMinutes(-11), lifetime: TimeSpan.FromMinutes(10))));

    [Fact]
    public async Task ATokenThatIsNotValidYet_Is401() =>
        await AssertRejectedAsync(await CallWithTokenAsync(Token(
            issuedAt: DateTime.UtcNow.AddMinutes(10), lifetime: TimeSpan.FromMinutes(10))));

    // ----- Response hygiene -----

    [Fact]
    public async Task A401_AsksForABearerToken_ButSaysNothingElse()
    {
        var response = await CallWithTokenAsync("garbage");

        Assert.Contains(response.Headers.WwwAuthenticate, h => h.Scheme == "Bearer");
        Assert.DoesNotContain(response.Headers.WwwAuthenticate, h => !string.IsNullOrEmpty(h.Parameter) && h.Parameter!.Contains("error_description"));
    }

    // ----- Startup configuration: the app must not run with insecure token settings -----

    private static async Task<Exception> StartupFailureAsync(Dictionary<string, string?> settings, string environment = "Development")
    {
        await using var factory = new ApiFactory(ApiFactory.UnreachableDatabase, environment, extraSettings: settings);

        var error = Record.Exception(() => factory.CreateClient());

        Assert.NotNull(error);

        return error;
    }

    private static string AllMessages(Exception error)
    {
        var parts = new List<string>();

        for (Exception? current = error; current is not null; current = current.InnerException)
        {
            parts.Add(current.Message);
            parts.Add($"[{current.GetType().Name}]");

            if (current is OptionsValidationException validation)
            {
                parts.AddRange(validation.Failures);
            }
        }

        return string.Join(" | ", parts);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task WithoutASigningKey_TheApiRefusesToStart(string environment)
    {
        var error = await StartupFailureAsync(new() { ["Jwt:SigningKey"] = "" }, environment);

        Assert.Contains("Jwt:SigningKey is not configured", AllMessages(error));
    }

    [Fact]
    public async Task WithAWeakSigningKey_TheApiRefusesToStart_WithoutEchoingTheKey()
    {
        const string weak = "weak-key-1234";

        var error = await StartupFailureAsync(new() { ["Jwt:SigningKey"] = weak });

        string messages = AllMessages(error);
        Assert.Contains("too weak", messages);
        Assert.DoesNotContain(weak, messages);
    }

    [Theory]
    [InlineData("Jwt:Issuer", "Jwt:Issuer is not configured")]
    [InlineData("Jwt:Audience", "Jwt:Audience is not configured")]
    public async Task WithoutAnIssuerOrAudience_TheApiRefusesToStart(string setting, string expected)
    {
        var error = await StartupFailureAsync(new() { [setting] = "" });

        Assert.Contains(expected, AllMessages(error));
    }

    [Fact]
    public async Task ASigningKeyIsNeverGeneratedOnStartup()
    {
        // With no key supplied the app must fail, not quietly make up its own (which would invalidate every
        // token on restart and hide the misconfiguration).
        var error = await StartupFailureAsync(new() { ["Jwt:SigningKey"] = " " });

        Assert.IsType<OptionsValidationException>(error is OptionsValidationException ? error : error.GetBaseException());
    }
}
