using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using VehicleRental.Application.Accounts;
using VehicleRental.Application.Exceptions;
using VehicleRental.Application.Security;
using VehicleRental.Domain.Entities;
using VehicleRental.Infrastructure.Persistence;

namespace VehicleRental.Infrastructure.Identity;

/// <summary>
/// Accounts and sign-in over ASP.NET Core Identity. Passwords are hashed, validated and locked out by
/// Identity itself; nothing here hashes or compares passwords by hand.
/// </summary>
public sealed class AccountService : IAccountService
{
    private const string UniqueViolation = "23505";

    // Verified against when the email is unknown, so that "no such account" costs about as much time as
    // "wrong password" and response time does not reveal which accounts exist.
    private static readonly ApplicationUser DummyUser = new();
    private static readonly string DummyHash = new PasswordHasher<ApplicationUser>().HashPassword(DummyUser, "unused-dummy-Password-1");

    private readonly UserManager<ApplicationUser> _users;
    private readonly VehicleRentalDbContext _db;
    private readonly JwtTokenService _tokens;
    private readonly ILogger<AccountService> _logger;

    public AccountService(
        UserManager<ApplicationUser> users,
        VehicleRentalDbContext db,
        JwtTokenService tokens,
        ILogger<AccountService> logger)
    {
        _users = users;
        _db = db;
        _tokens = tokens;
        _logger = logger;
    }

    public async Task<LoginResult?> LoginAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ApplicationUser? user = await _users.FindByEmailAsync(email ?? "");

        if (user is null)
        {
            _users.PasswordHasher.VerifyHashedPassword(DummyUser, DummyHash, password ?? "");
            _logger.LogInformation("Sign-in failed.");
            return null;
        }

        if (await _users.IsLockedOutAsync(user))
        {
            _logger.LogWarning("Sign-in refused for locked-out account {UserId}.", user.Id);
            return null;
        }

        if (!await _users.CheckPasswordAsync(user, password ?? ""))
        {
            await _users.AccessFailedAsync(user);
            _logger.LogInformation("Sign-in failed.");
            return null;
        }

        await _users.ResetAccessFailedCountAsync(user);

        UserDto dto = await ToDtoAsync(user, cancellationToken);
        (string token, DateTimeOffset expires) = _tokens.Create(user.Id, dto.Roles, user.CustomerId);

        return new LoginResult(token, expires, dto);
    }

    public async Task<UserDto> RegisterCustomerAsync(
        string email,
        string password,
        string name,
        CancellationToken cancellationToken = default)
    {
        // The customer number is generated: online customers do not choose a business identifier.
        var customer = new Customer($"WEB-{Guid.NewGuid():N}"[..14].ToUpperInvariant(), name);

        var user = new ApplicationUser
        {
            Id = Guid.CreateVersion7(),
            UserName = email,
            Email = email,
            CustomerId = customer.Id
        };

        // One database transaction covers the customer, the login and the role: either all exist or none do.
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            _db.Customers.Add(customer);

            // UserManager.CreateAsync saves the pending customer together with the new user.
            IdentityResult created = await _users.CreateAsync(user, password);

            if (!created.Succeeded)
            {
                _db.Entry(customer).State = EntityState.Detached;
                Throw(created);
            }

            Require(await _users.AddToRoleAsync(user, Roles.Customer));

            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: UniqueViolation })
        {
            // Two registrations for the same email at the same moment: the database index decided.
            throw new ConflictException("An account with this email already exists.");
        }

        return await ToDtoAsync(user, cancellationToken);
    }

    public async Task<UserDto> CreateStaffAsync(
        string email,
        string password,
        CancellationToken cancellationToken = default)
    {
        var user = new ApplicationUser { Id = Guid.CreateVersion7(), UserName = email, Email = email };

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            Throw(await _users.CreateAsync(user, password));
            Require(await _users.AddToRoleAsync(user, Roles.Staff));

            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: UniqueViolation })
        {
            throw new ConflictException("An account with this email already exists.");
        }

        return await ToDtoAsync(user, cancellationToken);
    }

    public async Task<UserDto?> GetUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        ApplicationUser? user = await _users.FindByIdAsync(userId.ToString());

        return user is null ? null : await ToDtoAsync(user, cancellationToken);
    }

    private async Task<UserDto> ToDtoAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        IList<string> roles = await _users.GetRolesAsync(user);

        string? customerNumber = user.CustomerId is { } customerId
            ? await _db.Customers
                .AsNoTracking()
                .Where(c => c.Id == customerId)
                .Select(c => c.CustomerNumber)
                .FirstOrDefaultAsync(cancellationToken)
            : null;

        return new UserDto(user.Id, user.Email!, roles.ToList(), user.CustomerId, customerNumber);
    }

    /// <summary>Turns a failed Identity result into the matching Application error.</summary>
    private static void Throw(IdentityResult result)
    {
        if (result.Succeeded)
        {
            return;
        }

        if (result.Errors.Any(e => e.Code is nameof(IdentityErrorDescriber.DuplicateEmail)
                                    or nameof(IdentityErrorDescriber.DuplicateUserName)))
        {
            throw new ConflictException("An account with this email already exists.");
        }

        // Password policy and email format problems are the caller's to fix; Identity's messages are written for that.
        throw new ArgumentException(string.Join(" ", result.Errors.Select(e => e.Description)));
    }

    /// <summary>A failure here is a setup problem (for example, roles missing because migrations were not applied).</summary>
    private static void Require(IdentityResult result)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                "The account could not be completed. Check that the database migrations have been applied.");
        }
    }
}

