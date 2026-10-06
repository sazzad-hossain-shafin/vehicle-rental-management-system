using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using VehicleRental.Application.Accounts;
using VehicleRental.Application.Exceptions;
using VehicleRental.Application.Security;
using VehicleRental.Infrastructure.Identity;
using VehicleRental.Infrastructure.IntegrationTests.Support;
using VehicleRental.Infrastructure.Persistence;

namespace VehicleRental.Infrastructure.IntegrationTests;

public class AccountServiceTests : DatabaseTestBase, IDisposable
{
    private readonly IdentityTestHost _host;

    public AccountServiceTests(PostgresFixture database) : base(database)
    {
        // The connection string is empty when no database is configured; those tests are skipped anyway.
        _host = new IdentityTestHost(string.IsNullOrEmpty(database.ConnectionString)
            ? "Host=localhost;Database=none"
            : database.ConnectionString);
    }

    public void Dispose() => _host.Dispose();

    private static string NewEmail() => $"user-{Guid.NewGuid():N}@example.test";

    private async Task<(int Customers, int Users)> CountsAsync()
    {
        await using var context = Database.CreateContext();

        return (await context.Customers.CountAsync(), await context.Users.CountAsync());
    }

    // ----- Registration -----

    [DatabaseFact]
    public async Task RegisterCustomer_CreatesTheCustomer_TheLoginAndTheRole_Together()
    {
        using var scope = _host.CreateScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IAccountService>();
        string email = NewEmail();

        UserDto user = await accounts.RegisterCustomerAsync(email, IdentityTestHost.NewPassword(), "Alice Example");

        Assert.Equal(email, user.Email);
        Assert.Equal(new[] { Roles.Customer }, user.Roles);
        Assert.NotNull(user.CustomerId);
        Assert.StartsWith("WEB-", user.CustomerNumber);
        Assert.Equal((1, 1), await CountsAsync());

        await using var context = Database.CreateContext();
        var customer = await context.Customers.SingleAsync();
        Assert.Equal("Alice Example", customer.Name);
        Assert.Equal(customer.Id, user.CustomerId);
    }

    [DatabaseFact]
    public async Task RegisterCustomer_WithAWeakPassword_LeavesNoCustomerOrUserBehind()
    {
        using var scope = _host.CreateScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IAccountService>();

        var error = await Assert.ThrowsAsync<ArgumentException>(
            () => accounts.RegisterCustomerAsync(NewEmail(), "short", "Alice"));

        Assert.Contains("at least 10", error.Message);
        Assert.Equal((0, 0), await CountsAsync());
    }

    [DatabaseFact]
    public async Task RegisterCustomer_WithAnInvalidName_LeavesNothingBehind()
    {
        using var scope = _host.CreateScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IAccountService>();

        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => accounts.RegisterCustomerAsync(NewEmail(), IdentityTestHost.NewPassword(), "   "));

        Assert.Equal((0, 0), await CountsAsync());
    }

    [DatabaseFact]
    public async Task RegisterCustomer_WithADuplicateEmail_IsAConflict_AndCreatesNoSecondCustomer()
    {
        string email = NewEmail();
        using (var first = _host.CreateScope())
        {
            await first.ServiceProvider.GetRequiredService<IAccountService>()
                .RegisterCustomerAsync(email, IdentityTestHost.NewPassword(), "Alice");
        }

        using var second = _host.CreateScope();
        var accounts = second.ServiceProvider.GetRequiredService<IAccountService>();

        await Assert.ThrowsAsync<ConflictException>(
            () => accounts.RegisterCustomerAsync(email.ToUpperInvariant(), IdentityTestHost.NewPassword(), "Impostor"));

        Assert.Equal((1, 1), await CountsAsync());
    }

    [DatabaseFact]
    public async Task TwoRegistrationsForTheSameEmailAtOnce_ExactlyOneWins_AndNothingIsLeftOver()
    {
        string email = NewEmail();

        async Task<Exception?> Register(string name)
        {
            try
            {
                using var scope = _host.CreateScope();
                await scope.ServiceProvider.GetRequiredService<IAccountService>()
                    .RegisterCustomerAsync(email, IdentityTestHost.NewPassword(), name);

                return null;
            }
            catch (Exception ex)
            {
                return ex;
            }
        }

        var outcomes = await Task.WhenAll(Task.Run(() => Register("First")), Task.Run(() => Register("Second")));

        Assert.Equal(1, outcomes.Count(o => o is null));
        Assert.Single(outcomes, o => o is ConflictException);
        Assert.Equal((1, 1), await CountsAsync()); // the loser's customer was rolled back too
    }

    [DatabaseFact]
    public async Task CreateStaff_MakesAStaffAccountWithNoCustomer()
    {
        using var scope = _host.CreateScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IAccountService>();

        UserDto user = await accounts.CreateStaffAsync(NewEmail(), IdentityTestHost.NewPassword());

        Assert.Equal(new[] { Roles.Staff }, user.Roles);
        Assert.Null(user.CustomerId);
        Assert.Equal((0, 1), await CountsAsync());
    }

    // ----- Passwords -----

    [DatabaseFact]
    public async Task Passwords_AreStoredHashed_NeverAsPlainText()
    {
        string password = IdentityTestHost.NewPassword();
        string email = NewEmail();
        using (var scope = _host.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IAccountService>().CreateStaffAsync(email, password);
        }

        await using var context = Database.CreateContext();
        string? hash = await context.Users.Where(u => u.Email == email).Select(u => u.PasswordHash).SingleAsync();

        Assert.NotNull(hash);
        Assert.NotEqual(password, hash);
        Assert.DoesNotContain(password, hash);
        Assert.StartsWith("AQAAAA", hash); // ASP.NET Core Identity's versioned PBKDF2 format
    }

    [DatabaseFact]
    public async Task TheSamePassword_ForTwoUsers_GivesDifferentHashes()
    {
        string password = IdentityTestHost.NewPassword();
        using (var scope = _host.CreateScope())
        {
            var accounts = scope.ServiceProvider.GetRequiredService<IAccountService>();
            await accounts.CreateStaffAsync(NewEmail(), password);
            await accounts.CreateStaffAsync(NewEmail(), password);
        }

        await using var context = Database.CreateContext();
        var hashes = await context.Users.Select(u => u.PasswordHash).ToListAsync();

        Assert.Equal(2, hashes.Distinct().Count()); // each hash has its own salt
    }

    // ----- Login and lockout -----

    [DatabaseFact]
    public async Task Login_WithTheRightPassword_ReturnsATokenAndTheUser()
    {
        string email = NewEmail();
        string password = IdentityTestHost.NewPassword();
        using var scope = _host.CreateScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IAccountService>();
        await accounts.CreateStaffAsync(email, password);

        LoginResult? result = await accounts.LoginAsync(email.ToUpperInvariant(), password);

        Assert.NotNull(result);
        Assert.False(string.IsNullOrWhiteSpace(result.AccessToken));
        Assert.Equal(email, result.User.Email);
        Assert.True(result.ExpiresAtUtc > DateTimeOffset.UtcNow);
    }

    [DatabaseFact]
    public async Task Login_Fails_WithoutRevealingWhy_ForWrongPasswordAndUnknownEmail()
    {
        string email = NewEmail();
        using var scope = _host.CreateScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IAccountService>();
        await accounts.CreateStaffAsync(email, IdentityTestHost.NewPassword());

        var wrongPassword = await accounts.LoginAsync(email, IdentityTestHost.NewPassword());
        var unknownEmail = await accounts.LoginAsync(NewEmail(), IdentityTestHost.NewPassword());

        Assert.Null(wrongPassword);
        Assert.Null(unknownEmail);
    }

    [DatabaseFact]
    public async Task AfterTooManyFailures_TheAccountIsLocked_EvenForTheCorrectPassword()
    {
        string email = NewEmail();
        string password = IdentityTestHost.NewPassword();
        using var scope = _host.CreateScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IAccountService>();
        await accounts.CreateStaffAsync(email, password);

        for (int i = 0; i < 5; i++)
        {
            Assert.Null(await accounts.LoginAsync(email, "wrong-Password-" + i));
        }

        Assert.Null(await accounts.LoginAsync(email, password));
    }

    [DatabaseFact]
    public async Task ASuccessfulLogin_ResetsTheFailureCount()
    {
        string email = NewEmail();
        string password = IdentityTestHost.NewPassword();
        using var scope = _host.CreateScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IAccountService>();
        await accounts.CreateStaffAsync(email, password);

        for (int i = 0; i < 4; i++)
        {
            await accounts.LoginAsync(email, "wrong-Password-" + i);
        }

        Assert.NotNull(await accounts.LoginAsync(email, password));

        for (int i = 0; i < 4; i++)
        {
            await accounts.LoginAsync(email, "wrong-Password-" + i);
        }

        Assert.NotNull(await accounts.LoginAsync(email, password)); // 4 + 4 failures, but never 5 in a row
    }

    [DatabaseFact]
    public async Task LockingOneAccount_DoesNotLockAnother()
    {
        string victim = NewEmail();
        string other = NewEmail();
        string otherPassword = IdentityTestHost.NewPassword();
        using var scope = _host.CreateScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IAccountService>();
        await accounts.CreateStaffAsync(victim, IdentityTestHost.NewPassword());
        await accounts.CreateStaffAsync(other, otherPassword);

        for (int i = 0; i < 6; i++)
        {
            await accounts.LoginAsync(victim, "wrong-Password-" + i);
        }

        Assert.NotNull(await accounts.LoginAsync(other, otherPassword));
    }

    [DatabaseFact]
    public async Task Logs_NeverContainPasswordsOrEmails()
    {
        string email = NewEmail();
        string password = IdentityTestHost.NewPassword();
        string wrong = IdentityTestHost.NewPassword();
        using var scope = _host.CreateScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IAccountService>();
        await accounts.CreateStaffAsync(email, password);

        await accounts.LoginAsync(email, wrong);
        await accounts.LoginAsync(NewEmail(), wrong);
        await accounts.LoginAsync(email, password);

        string[] logs;
        lock (_host.Logs)
        {
            logs = _host.Logs.ToArray();
        }

        Assert.DoesNotContain(logs, l => l.Contains(password) || l.Contains(wrong) || l.Contains(email));
    }

    // ----- Data model -----

    [DatabaseFact]
    public async Task TheThreeRoles_ExistAfterMigration()
    {
        await using var context = Database.CreateContext();

        var names = await context.Roles.Select(r => r.Name).OrderBy(n => n).ToListAsync();

        Assert.Equal(new[] { "Admin", "Customer", "Staff" }, names);
    }

    [DatabaseFact]
    public async Task ACustomerCanHaveAtMostOneLogin_EnforcedByTheDatabase()
    {
        using var scope = _host.CreateScope();
        UserDto first = await scope.ServiceProvider.GetRequiredService<IAccountService>()
            .RegisterCustomerAsync(NewEmail(), IdentityTestHost.NewPassword(), "Alice");

        await using var context = Database.CreateContext();
        context.Users.Add(new ApplicationUser
        {
            Id = Guid.CreateVersion7(),
            UserName = NewEmail(),
            NormalizedUserName = NewEmail().ToUpperInvariant(),
            CustomerId = first.CustomerId
        });

        var error = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        Assert.Equal("23505", ((PostgresException)error.InnerException!).SqlState);
    }

    [DatabaseFact]
    public async Task ACustomerWithALogin_CannotBeDeleted_SoRentalHistoryAndAccountsStayConsistent()
    {
        using var scope = _host.CreateScope();
        UserDto user = await scope.ServiceProvider.GetRequiredService<IAccountService>()
            .RegisterCustomerAsync(NewEmail(), IdentityTestHost.NewPassword(), "Alice");

        await using var context = Database.CreateContext();
        context.Customers.Remove(await context.Customers.SingleAsync(c => c.Id == user.CustomerId));

        var error = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        Assert.Equal("23503", ((PostgresException)error.InnerException!).SqlState);
    }

    [DatabaseFact]
    public async Task CustomersWithoutALogin_RemainValid()
    {
        // Customers that existed before accounts were introduced, or were created at the desk, have no login.
        await TestEntities.SaveAsync(Database, customer: TestEntities.NewCustomer("C1", "Walk-in"));

        await using var session = Database.CreateSession();
        var customer = await session.Customers.GetByCustomerNumberAsync("C1");

        Assert.NotNull(customer);
        Assert.Equal(0, await Database.CreateContext().Users.CountAsync());
    }

    [DatabaseFact]
    public async Task GetUser_ReturnsASafeView_OrNullForAMissingAccount()
    {
        using var scope = _host.CreateScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IAccountService>();
        UserDto created = await accounts.RegisterCustomerAsync(NewEmail(), IdentityTestHost.NewPassword(), "Alice");

        UserDto? found = await accounts.GetUserAsync(created.Id);

        Assert.NotNull(found);
        Assert.Equal(created.Id, found.Id);
        Assert.Equal(created.Email, found.Email);
        Assert.Equal(created.Roles, found.Roles);
        Assert.Equal(created.CustomerId, found.CustomerId);
        Assert.Equal(created.CustomerNumber, found.CustomerNumber);
        Assert.Null(await accounts.GetUserAsync(Guid.NewGuid()));
    }

    // ----- Initial admin seeding -----

    private static Dictionary<string, string?> SeedSettings(string email, string password) => new()
    {
        ["Seed:Admin:Email"] = email,
        ["Seed:Admin:Password"] = password
    };

    [DatabaseFact]
    public async Task Seeding_WithoutCredentials_CreatesNothing()
    {
        using var scope = _host.CreateScope();

        var outcome = await scope.ServiceProvider.GetRequiredService<AdminSeeder>().SeedAsync();

        Assert.Equal(AdminSeedOutcome.NotConfigured, outcome);
        Assert.Equal((0, 0), await CountsAsync());
    }

    [DatabaseFact]
    public async Task Seeding_CreatesTheAdmin_Once_AndNeverChangesAnExistingAccount()
    {
        string email = NewEmail();
        string password = IdentityTestHost.NewPassword();

        using (var host = new IdentityTestHost(Database.ConnectionString, SeedSettings(email, password)))
        {
            using var scope = host.CreateScope();
            Assert.Equal(AdminSeedOutcome.Created, await scope.ServiceProvider.GetRequiredService<AdminSeeder>().SeedAsync());
        }

        // A later start with a different password must not touch the account.
        using (var host = new IdentityTestHost(Database.ConnectionString, SeedSettings(email, IdentityTestHost.NewPassword())))
        {
            using var scope = host.CreateScope();
            Assert.Equal(AdminSeedOutcome.AlreadyExists, await scope.ServiceProvider.GetRequiredService<AdminSeeder>().SeedAsync());

            var login = await scope.ServiceProvider.GetRequiredService<IAccountService>().LoginAsync(email, password);
            Assert.NotNull(login);
            Assert.Equal(new[] { Roles.Admin }, login.User.Roles);
        }

        Assert.Equal((0, 1), await CountsAsync());
    }

    [DatabaseFact]
    public async Task Seeding_WithAWeakPassword_IsRejected_AndTheLogDoesNotContainIt()
    {
        string weak = "weak";
        using var host = new IdentityTestHost(Database.ConnectionString, SeedSettings(NewEmail(), weak));
        using var scope = host.CreateScope();

        var outcome = await scope.ServiceProvider.GetRequiredService<AdminSeeder>().SeedAsync();

        Assert.Equal(AdminSeedOutcome.Rejected, outcome);
        Assert.Equal((0, 0), await CountsAsync());
        lock (host.Logs)
        {
            Assert.Contains(host.Logs, l => l.Contains("rejected"));
            Assert.DoesNotContain(host.Logs, l => l.Contains(weak, StringComparison.Ordinal) && !l.Contains("rejected"));
        }
    }
}
