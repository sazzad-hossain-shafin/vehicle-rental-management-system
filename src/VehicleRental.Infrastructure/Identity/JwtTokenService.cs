using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using VehicleRental.Application.Security;

namespace VehicleRental.Infrastructure.Identity;

/// <summary>
/// Issues signed access tokens. A token carries only what the API needs to authorize a request: who
/// (the user ID), what role(s), and for customer accounts which customer. It carries no password data,
/// no email and no other personal details.
/// </summary>
public sealed class JwtTokenService
{
    private readonly JwtOptions _options;
    private readonly TimeProvider _timeProvider;

    public JwtTokenService(IOptions<JwtOptions> options, TimeProvider timeProvider)
    {
        _options = options.Value;
        _timeProvider = timeProvider;
    }

    public (string Token, DateTimeOffset ExpiresAtUtc) Create(Guid userId, IEnumerable<string> roles, Guid? customerId)
    {
        DateTime now = _timeProvider.GetUtcNow().UtcDateTime;
        DateTime expires = now.AddMinutes(_options.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new(AuthClaims.Subject, userId.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        claims.AddRange(roles.Select(role => new Claim(AuthClaims.Role, role)));

        if (customerId is { } id)
        {
            claims.Add(new Claim(AuthClaims.CustomerId, id.ToString()));
        }

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            Subject = new ClaimsIdentity(claims),
            NotBefore = now,
            IssuedAt = now,
            Expires = expires,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey)),
                SecurityAlgorithms.HmacSha256)
        };

        string token = new JsonWebTokenHandler().CreateToken(descriptor);

        return (token, new DateTimeOffset(expires, TimeSpan.Zero));
    }
}
