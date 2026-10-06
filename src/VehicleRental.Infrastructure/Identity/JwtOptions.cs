using Microsoft.Extensions.Options;

namespace VehicleRental.Infrastructure.Identity;

/// <summary>
/// Settings for the signed access tokens. Issuer and audience are ordinary settings; the signing key is a
/// secret that must come from user secrets or the environment, never from the repository.
/// </summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    /// <summary>The shortest key accepted: 32 characters (256 bits for HMAC-SHA256).</summary>
    public const int MinimumSigningKeyLength = 32;

    public string Issuer { get; set; } = "";

    public string Audience { get; set; } = "";

    /// <summary>The secret used to sign and verify tokens. Never logged and never returned.</summary>
    public string SigningKey { get; set; } = "";

    /// <summary>How long an access token is valid. Tokens are short-lived because they cannot be revoked.</summary>
    public int AccessTokenMinutes { get; set; } = 30;
}

/// <summary>
/// Refuses to start the application with insecure token settings. No message ever contains the key.
/// </summary>
public sealed class JwtOptionsValidator : IValidateOptions<JwtOptions>
{
    public ValidateOptionsResult Validate(string? name, JwtOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.Issuer))
        {
            failures.Add("Jwt:Issuer is not configured.");
        }

        if (string.IsNullOrWhiteSpace(options.Audience))
        {
            failures.Add("Jwt:Audience is not configured.");
        }

        if (string.IsNullOrWhiteSpace(options.SigningKey))
        {
            failures.Add(
                "Jwt:SigningKey is not configured. Set it with user secrets or the Jwt__SigningKey " +
                $"environment variable (at least {JwtOptions.MinimumSigningKeyLength} random characters).");
        }
        else if (options.SigningKey.Length < JwtOptions.MinimumSigningKeyLength
                 || options.SigningKey.Distinct().Count() < 12)
        {
            failures.Add(
                $"Jwt:SigningKey is too weak: it needs at least {JwtOptions.MinimumSigningKeyLength} characters " +
                "with real variety. Generate a random one.");
        }

        if (options.AccessTokenMinutes is < 1 or > 120)
        {
            failures.Add("Jwt:AccessTokenMinutes must be between 1 and 120.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
