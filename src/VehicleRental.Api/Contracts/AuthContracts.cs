using System.ComponentModel.DataAnnotations;
using VehicleRental.Application.Accounts;

namespace VehicleRental.Api.Contracts;

/// <summary>Credentials for signing in.</summary>
public sealed class LoginRequest
{
    [Required]
    [StringLength(256)]
    public string? Email { get; init; }

    [Required]
    [StringLength(128)]
    public string? Password { get; init; }
}

/// <summary>
/// Creates a customer account. There is no role field: every account created here is a Customer, and
/// anything else sent in the body is ignored.
/// </summary>
public sealed class RegisterCustomerRequest
{
    [Required]
    [EmailAddress]
    [StringLength(256)]
    public string? Email { get; init; }

    /// <summary>At least 10 characters with an upper-case letter, a lower-case letter and a digit.</summary>
    [Required]
    [StringLength(128)]
    public string? Password { get; init; }

    /// <summary>The customer's name.</summary>
    [Required]
    public string? Name { get; init; }
}

/// <summary>
/// Creates a staff account (admins only). The role is fixed to Staff and cannot be chosen.
/// </summary>
public sealed class CreateStaffRequest
{
    [Required]
    [EmailAddress]
    [StringLength(256)]
    public string? Email { get; init; }

    [Required]
    [StringLength(128)]
    public string? Password { get; init; }
}

/// <summary>
/// A successful browser sign-in. The access token is not in the body: it is set as an <c>HttpOnly</c> cookie that
/// scripts cannot read.
/// </summary>
/// <param name="ExpiresAtUtc">When the session stops being valid. Sign in again after that.</param>
/// <param name="User">The account that signed in.</param>
public sealed record SessionResponse(DateTimeOffset ExpiresAtUtc, UserDto User);

/// <summary>A successful sign-in.</summary>
/// <param name="AccessToken">Send as <c>Authorization: Bearer {accessToken}</c>.</param>
/// <param name="TokenType">Always "Bearer".</param>
/// <param name="ExpiresAtUtc">When the token stops being valid. Sign in again after that.</param>
/// <param name="User">The account that signed in.</param>
public sealed record LoginResponse(string AccessToken, string TokenType, DateTimeOffset ExpiresAtUtc, UserDto User);
