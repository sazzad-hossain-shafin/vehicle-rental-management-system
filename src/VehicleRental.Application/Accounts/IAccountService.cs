namespace VehicleRental.Application.Accounts;

/// <summary>
/// A safe view of a user account. It never includes password data or other security fields.
/// </summary>
/// <param name="Id">The account's permanent ID.</param>
/// <param name="Email">The login email.</param>
/// <param name="Roles">The account's roles.</param>
/// <param name="CustomerId">The linked customer, for customer accounts only.</param>
/// <param name="CustomerNumber">The linked customer's number, for customer accounts only.</param>
public sealed record UserDto(
    Guid Id,
    string Email,
    IReadOnlyList<string> Roles,
    Guid? CustomerId,
    string? CustomerNumber);

/// <summary>The outcome of a successful login.</summary>
/// <param name="AccessToken">A signed, short-lived token to send as <c>Authorization: Bearer ...</c>.</param>
/// <param name="ExpiresAtUtc">When the token stops being valid.</param>
/// <param name="User">Who logged in.</param>
public sealed record LoginResult(string AccessToken, DateTimeOffset ExpiresAtUtc, UserDto User);

/// <summary>
/// Accounts and sign-in. Implemented over ASP.NET Core Identity in the Infrastructure project;
/// the Application layer only sees this contract, so it stays free of Identity and JWT details.
/// </summary>
public interface IAccountService
{
    /// <summary>
    /// Checks an email and password. Returns null for every kind of failure (unknown email, wrong
    /// password, locked-out account), so callers cannot tell them apart.
    /// </summary>
    Task<LoginResult?> LoginAsync(string email, string password, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a customer account: a new customer record and a login linked to it, saved together or
    /// not at all. The account always gets the Customer role; callers cannot choose roles.
    /// </summary>
    /// <exception cref="Exceptions.ConflictException">The email is already registered.</exception>
    /// <exception cref="ArgumentException">The email, name or password is not acceptable.</exception>
    Task<UserDto> RegisterCustomerAsync(
        string email,
        string password,
        string name,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a staff account (role Staff, no customer record). Authorization to call this is the
    /// caller's responsibility.
    /// </summary>
    /// <exception cref="Exceptions.ConflictException">The email is already registered.</exception>
    /// <exception cref="ArgumentException">The email or password is not acceptable.</exception>
    Task<UserDto> CreateStaffAsync(
        string email,
        string password,
        CancellationToken cancellationToken = default);

    /// <returns>The account, or null if it no longer exists.</returns>
    Task<UserDto?> GetUserAsync(Guid userId, CancellationToken cancellationToken = default);
}
