using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using VehicleRental.Application.Security;
using VehicleRental.Infrastructure.Identity;

namespace VehicleRental.Infrastructure.IntegrationTests;

/// <summary>Token issuing and the rules that stop the app starting with insecure token settings. No database needed.</summary>
public class JwtTests
{
    private static JwtOptions ValidOptions() => new()
    {
        Issuer = "issuer",
        Audience = "audience",
        SigningKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)),
        AccessTokenMinutes = 30
    };

    private static JwtTokenService ServiceFor(JwtOptions options, TimeProvider? clock = null) =>
        new(Options.Create(options), clock ?? TimeProvider.System);

    private static JsonWebToken Read(string token) => new JsonWebTokenHandler().ReadJsonWebToken(token);

    // ----- Token content -----

    [Fact]
    public void Token_ForAStaffUser_HasTheSubjectAndRole_AndNothingElsePersonal()
    {
        var userId = Guid.NewGuid();

        var (token, _) = ServiceFor(ValidOptions()).Create(userId, [Roles.Staff], customerId: null);
        var jwt = Read(token);

        Assert.Equal(userId.ToString(), jwt.Subject);
        Assert.Equal(new[] { Roles.Staff }, jwt.Claims.Where(c => c.Type == AuthClaims.Role).Select(c => c.Value));
        Assert.DoesNotContain(jwt.Claims, c => c.Type == AuthClaims.CustomerId);
    }

    [Fact]
    public void Token_ForACustomerUser_CarriesTheirCustomerId()
    {
        var customerId = Guid.NewGuid();

        var (token, _) = ServiceFor(ValidOptions()).Create(Guid.NewGuid(), [Roles.Customer], customerId);

        Assert.Equal(customerId.ToString(), Read(token).Claims.Single(c => c.Type == AuthClaims.CustomerId).Value);
    }

    [Fact]
    public void Token_ContainsOnlyTheExpectedClaimTypes()
    {
        var (token, _) = ServiceFor(ValidOptions()).Create(Guid.NewGuid(), [Roles.Customer], Guid.NewGuid());

        var types = Read(token).Claims.Select(c => c.Type).ToHashSet();

        // Identity and timing metadata only: no email, name, password data or other personal details.
        var allowed = new HashSet<string>
        {
            "sub", "jti", "role", "customer_id", "iss", "aud", "iat", "nbf", "exp"
        };
        Assert.Subset(allowed, types);
    }

    [Fact]
    public void Token_ExpiresAfterTheConfiguredLifetime_InUtc()
    {
        var clock = new FixedClock(new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero));
        var options = ValidOptions();
        options.AccessTokenMinutes = 20;

        var (token, expiresAt) = ServiceFor(options, clock).Create(Guid.NewGuid(), [Roles.Staff], null);
        var jwt = Read(token);

        Assert.Equal(clock.GetUtcNow().AddMinutes(20), expiresAt);
        Assert.Equal(expiresAt.UtcDateTime, jwt.ValidTo);
        Assert.Equal(clock.GetUtcNow().UtcDateTime, jwt.ValidFrom);
        Assert.Equal("issuer", jwt.Issuer);
        Assert.Contains("audience", jwt.Audiences);
    }

    [Fact]
    public void Token_DoesNotContainTheSigningKey()
    {
        var options = ValidOptions();

        var (token, _) = ServiceFor(options).Create(Guid.NewGuid(), [Roles.Admin], null);

        Assert.DoesNotContain(options.SigningKey, token);
        Assert.DoesNotContain(options.SigningKey, Read(token).EncodedPayload);
    }

    [Fact]
    public void EveryTokenHasItsOwnId()
    {
        var service = ServiceFor(ValidOptions());

        var first = Read(service.Create(Guid.NewGuid(), [Roles.Staff], null).Token).Id;
        var second = Read(service.Create(Guid.NewGuid(), [Roles.Staff], null).Token).Id;

        Assert.NotEqual(first, second);
    }

    // ----- Startup validation of the settings -----

    private static ValidateOptionsResult Validate(JwtOptions options) =>
        new JwtOptionsValidator().Validate(null, options);

    [Fact]
    public void ValidSettings_AreAccepted() => Assert.True(Validate(ValidOptions()).Succeeded);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void MissingSigningKey_IsRejected(string key)
    {
        var options = ValidOptions();
        options.SigningKey = key;

        var result = Validate(options);

        Assert.True(result.Failed);
        Assert.Contains("Jwt:SigningKey", result.FailureMessage);
    }

    [Theory]
    [InlineData("short")]
    [InlineData("0123456789012345678901234567890")]                 // 31 characters
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")] // long but no variety
    [InlineData("abababababababababababababababababababababababab")] // long, two characters repeated
    public void WeakSigningKey_IsRejected_WithoutEchoingIt(string key)
    {
        var options = ValidOptions();
        options.SigningKey = key;

        var result = Validate(options);

        Assert.True(result.Failed);
        Assert.Contains("too weak", result.FailureMessage);
        Assert.DoesNotContain(key, result.FailureMessage);
    }

    [Fact]
    public void MissingIssuerOrAudience_IsRejected()
    {
        var noIssuer = ValidOptions();
        noIssuer.Issuer = "";
        var noAudience = ValidOptions();
        noAudience.Audience = " ";

        Assert.Contains("Jwt:Issuer", Validate(noIssuer).FailureMessage);
        Assert.Contains("Jwt:Audience", Validate(noAudience).FailureMessage);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(121)]
    public void UnreasonableTokenLifetime_IsRejected(int minutes)
    {
        var options = ValidOptions();
        options.AccessTokenMinutes = minutes;

        Assert.Contains("AccessTokenMinutes", Validate(options).FailureMessage);
    }

    [Fact]
    public void SeveralProblems_AreReportedTogether()
    {
        var result = Validate(new JwtOptions());

        Assert.Contains("Jwt:Issuer", result.FailureMessage);
        Assert.Contains("Jwt:Audience", result.FailureMessage);
        Assert.Contains("Jwt:SigningKey", result.FailureMessage);
    }

    private sealed class FixedClock : TimeProvider
    {
        private readonly DateTimeOffset _now;

        public FixedClock(DateTimeOffset now) => _now = now;

        public override DateTimeOffset GetUtcNow() => _now;
    }
}
