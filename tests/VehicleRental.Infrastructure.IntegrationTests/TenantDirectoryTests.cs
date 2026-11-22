using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using VehicleRental.Application.Tenancy;
using VehicleRental.Domain.Enums;
using VehicleRental.Infrastructure.IntegrationTests.Support;

namespace VehicleRental.Infrastructure.IntegrationTests;

/// <summary>Company slug resolution and active-membership lookup, as the runtime role reads them.</summary>
public class TenantDirectoryTests : IClassFixture<TenantTestDatabase>
{
    private readonly TenantTestDatabase _db;

    public TenantDirectoryTests(TenantTestDatabase db) => _db = db;

    private async Task<T> WithScopeAsync<T>(Func<IServiceProvider, Task<T>> work)
    {
        await using ServiceProvider services = _db.BuildServices();
        await using AsyncServiceScope scope = services.CreateAsyncScope();

        return await work(scope.ServiceProvider);
    }

    private async Task<Guid> NewUserAsync()
    {
        Guid id = Guid.NewGuid();
        await using var owner = new NpgsqlConnection(_db.OwnerConnectionString);
        await owner.OpenAsync();
        await TenantTestDatabase.Execute(
            owner,
            $"INSERT INTO \"Users\" (\"Id\", \"UserName\", \"AccessFailedCount\", \"EmailConfirmed\", \"LockoutEnabled\", \"PhoneNumberConfirmed\", \"TwoFactorEnabled\") VALUES ('{id}', 'u-{id:N}', 0, false, true, false, false)");

        return id;
    }

    private async Task AddMembershipAsync(Guid company, Guid user, string role, string status = "Active")
    {
        await using var owner = new NpgsqlConnection(_db.OwnerConnectionString);
        await owner.OpenAsync();
        await TenantTestDatabase.Execute(
            owner,
            $"INSERT INTO \"CompanyMemberships\" (\"Id\", \"CompanyId\", \"UserId\", \"Role\", \"Status\", \"CreatedAt\") VALUES ('{Guid.NewGuid()}', '{company}', '{user}', '{role}', '{status}', now())");
    }

    [DatabaseFact]
    public async Task An_active_company_is_found_by_its_slug_whatever_the_case_or_spacing()
    {
        Guid company = await _db.NewCompanyAsync("acme-rentals");

        CompanySummary? found = await WithScopeAsync(s => s.GetRequiredService<ICompanyDirectory>().FindActiveBySlugAsync("  Acme-Rentals "));

        Assert.NotNull(found);
        Assert.Equal(company, found.Id);
        Assert.Equal("acme-rentals", found.Slug);
        Assert.Equal(CompanyStatus.Active, found.Status);
    }

    [DatabaseFact]
    public async Task Pending_suspended_unknown_malformed_and_reserved_all_look_the_same()
    {
        await _db.NewCompanyAsync("pending-co", "PendingReview");
        await _db.NewCompanyAsync("suspended-co", "Suspended");

        foreach (string slug in new[] { "pending-co", "suspended-co", "does-not-exist", "", "  ", "a", "-bad", "bad-", "a--b", "has space", "admin", "api", "../etc" })
        {
            Assert.Null(await WithScopeAsync(s => s.GetRequiredService<ICompanyDirectory>().FindActiveBySlugAsync(slug)));
        }

        Assert.Null(await WithScopeAsync(s => s.GetRequiredService<ICompanyDirectory>().FindActiveBySlugAsync(null)));
    }

    [DatabaseFact]
    public async Task A_company_a_member_may_manage_is_pending_or_active_but_never_suspended_or_unknown()
    {
        Guid pending = await _db.NewCompanyAsync(status: "PendingReview");
        Guid active = await _db.NewCompanyAsync();
        Guid suspended = await _db.NewCompanyAsync(status: "Suspended");

        Assert.NotNull(await WithScopeAsync(s => s.GetRequiredService<ICompanyDirectory>().FindManageableAsync(pending)));
        Assert.NotNull(await WithScopeAsync(s => s.GetRequiredService<ICompanyDirectory>().FindManageableAsync(active)));
        Assert.Null(await WithScopeAsync(s => s.GetRequiredService<ICompanyDirectory>().FindManageableAsync(suspended)));
        Assert.Null(await WithScopeAsync(s => s.GetRequiredService<ICompanyDirectory>().FindManageableAsync(Guid.NewGuid())));
        Assert.Null(await WithScopeAsync(s => s.GetRequiredService<ICompanyDirectory>().FindManageableAsync(Guid.Empty)));
    }

    [DatabaseFact]
    public async Task Only_active_companies_are_listed_for_background_work()
    {
        Guid active = await _db.NewCompanyAsync();
        Guid pending = await _db.NewCompanyAsync(status: "PendingReview");

        IReadOnlyList<Guid> ids = await WithScopeAsync(s => s.GetRequiredService<ICompanyDirectory>().ListActiveIdsAsync());

        Assert.Contains(active, ids);
        Assert.DoesNotContain(pending, ids);
    }

    [DatabaseFact]
    public async Task Membership_is_the_users_active_role_in_that_company_only()
    {
        Guid acme = await _db.NewCompanyAsync();
        Guid zeta = await _db.NewCompanyAsync();
        Guid owner = await NewUserAsync();
        Guid staff = await NewUserAsync();
        Guid removed = await NewUserAsync();
        Guid stranger = await NewUserAsync();
        await AddMembershipAsync(acme, owner, "Owner");
        await AddMembershipAsync(acme, staff, "Staff");
        await AddMembershipAsync(acme, removed, "Staff", "Removed");

        async Task<CompanyRole?> RoleAsync(Guid company, Guid user) =>
            await WithScopeAsync(s => s.GetRequiredService<IMembershipDirectory>().FindActiveRoleAsync(company, user));

        Assert.Equal(CompanyRole.Owner, await RoleAsync(acme, owner));
        Assert.Equal(CompanyRole.Staff, await RoleAsync(acme, staff));
        Assert.Null(await RoleAsync(acme, removed));    // history, not access
        Assert.Null(await RoleAsync(acme, stranger));
        Assert.Null(await RoleAsync(zeta, owner));      // a role in one company says nothing about another
        Assert.Null(await RoleAsync(Guid.NewGuid(), owner));
        Assert.Null(await RoleAsync(Guid.Empty, owner));
        Assert.Null(await RoleAsync(acme, Guid.Empty));
    }

    [DatabaseFact]
    public async Task Removing_a_member_takes_effect_on_the_very_next_lookup()
    {
        Guid company = await _db.NewCompanyAsync();
        Guid user = await NewUserAsync();
        await AddMembershipAsync(company, user, "Staff");

        Assert.Equal(CompanyRole.Staff, await WithScopeAsync(s => s.GetRequiredService<IMembershipDirectory>().FindActiveRoleAsync(company, user)));

        await using (var owner = new NpgsqlConnection(_db.OwnerConnectionString))
        {
            await owner.OpenAsync();
            await TenantTestDatabase.Execute(owner, $"UPDATE \"CompanyMemberships\" SET \"Status\" = 'Removed' WHERE \"CompanyId\" = '{company}' AND \"UserId\" = '{user}'");
        }

        Assert.Null(await WithScopeAsync(s => s.GetRequiredService<IMembershipDirectory>().FindActiveRoleAsync(company, user)));
    }
}
