using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VehicleRental.Application.Security;
using VehicleRental.Infrastructure.Persistence;

namespace VehicleRental.Infrastructure.Identity;

/// <summary>
/// Optional first-admin credentials. They come from configuration (user secrets or environment
/// variables), never from the repository; if either value is missing, no admin is created.
/// </summary>
public sealed class AdminSeedOptions
{
    public const string SectionName = "Seed:Admin";

    public string Email { get; set; } = "";

    public string Password { get; set; } = "";
}

public enum AdminSeedOutcome
{
    /// <summary>No credentials were configured, so nothing was done.</summary>
    NotConfigured,

    Created,

    /// <summary>An account with that email exists already; it was left untouched.</summary>
    AlreadyExists,

    /// <summary>The configured email or password was refused by Identity's rules.</summary>
    Rejected
}

/// <summary>
/// Creates the initial admin account from configuration, once. It never changes an existing account,
/// never overwrites a password, and never logs or returns the password.
/// </summary>
public sealed class AdminSeeder
{
    private readonly UserManager<ApplicationUser> _users;
    private readonly VehicleRentalDbContext _db;
    private readonly AdminSeedOptions _options;
    private readonly ILogger<AdminSeeder> _logger;

    public AdminSeeder(
        UserManager<ApplicationUser> users,
        VehicleRentalDbContext db,
        IOptions<AdminSeedOptions> options,
        ILogger<AdminSeeder> logger)
    {
        _users = users;
        _db = db;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<AdminSeedOutcome> SeedAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.Email) || string.IsNullOrWhiteSpace(_options.Password))
        {
            _logger.LogInformation(
                "No initial admin configured ({Section}:Email and {Section}:Password); skipping admin creation.",
                AdminSeedOptions.SectionName,
                AdminSeedOptions.SectionName);

            return AdminSeedOutcome.NotConfigured;
        }

        if (await _users.FindByEmailAsync(_options.Email) is not null)
        {
            _logger.LogInformation("The initial admin account already exists; leaving it unchanged.");

            return AdminSeedOutcome.AlreadyExists;
        }

        var user = new ApplicationUser { Id = Guid.CreateVersion7(), UserName = _options.Email, Email = _options.Email };

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        IdentityResult created = await _users.CreateAsync(user, _options.Password);

        if (!created.Succeeded)
        {
            // Only the rule violations are logged, never the values.
            _logger.LogError(
                "The configured initial admin was rejected: {Problems}",
                string.Join(" ", created.Errors.Select(e => e.Description)));

            return AdminSeedOutcome.Rejected;
        }

        IdentityResult inRole = await _users.AddToRoleAsync(user, Roles.Admin);

        if (!inRole.Succeeded)
        {
            _logger.LogError("The initial admin could not be given the Admin role; check that migrations were applied.");

            return AdminSeedOutcome.Rejected;
        }

        await transaction.CommitAsync(cancellationToken);

        _logger.LogInformation("Created the initial admin account {UserId}.", user.Id);

        return AdminSeedOutcome.Created;
    }
}
