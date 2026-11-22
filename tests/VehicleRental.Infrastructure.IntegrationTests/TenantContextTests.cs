using Microsoft.Extensions.Options;
using VehicleRental.Application.Tenancy;
using VehicleRental.Infrastructure.Tenancy;

namespace VehicleRental.Infrastructure.IntegrationTests;

/// <summary>The tenant lifecycle of one scope: unbound by default, bound once, never changed, and off unless enabled. No database needed.</summary>
public class TenantContextTests
{
    private static TenantContext Create(bool enabled = true) =>
        new(Options.Create(new TenancyOptions { Enabled = enabled }));

    [Fact]
    public void A_new_scope_has_no_tenant()
    {
        TenantContext tenant = Create();

        Assert.False(tenant.IsBound);
        Assert.Null(tenant.CompanyId);
        Assert.Equal(TenantSource.None, tenant.Source);
        Assert.Throws<TenantRequiredException>(() => tenant.RequireCompanyId());
    }

    [Fact]
    public void Tenancy_is_disabled_by_default_and_binding_then_fails_closed_and_changes_nothing()
    {
        var defaults = new TenancyOptions();
        Assert.False(defaults.Enabled);
        Assert.Equal(DatabaseRoleCheckMode.Warn, defaults.DatabaseRoleCheck);

        TenantContext tenant = Create(enabled: false);

        Assert.Throws<TenancyDisabledException>(() => tenant.Bind(Guid.NewGuid(), TenantSource.PublicSlug));
        Assert.False(tenant.IsBound);
    }

    [Fact]
    public void Binding_records_the_company_and_how_it_was_established()
    {
        TenantContext tenant = Create();
        Guid company = Guid.NewGuid();

        tenant.Bind(company, TenantSource.Membership);

        Assert.True(tenant.IsBound);
        Assert.Equal(company, tenant.CompanyId);
        Assert.Equal(company, tenant.RequireCompanyId());
        Assert.Equal(TenantSource.Membership, tenant.Source);
    }

    [Fact]
    public void The_bound_company_is_never_silently_changed()
    {
        TenantContext tenant = Create();
        Guid first = Guid.NewGuid();
        tenant.Bind(first, TenantSource.PublicSlug);

        Assert.Throws<TenantRebindException>(() => tenant.Bind(Guid.NewGuid(), TenantSource.PublicSlug));
        Assert.Throws<TenantRebindException>(() => tenant.Bind(Guid.NewGuid(), TenantSource.Background));

        Assert.Equal(first, tenant.CompanyId);
        Assert.Equal(TenantSource.PublicSlug, tenant.Source);
    }

    [Fact]
    public void Binding_the_same_company_again_is_harmless_and_keeps_the_original_source()
    {
        TenantContext tenant = Create();
        Guid company = Guid.NewGuid();
        tenant.Bind(company, TenantSource.Membership);

        tenant.Bind(company, TenantSource.Background);

        Assert.Equal(company, tenant.CompanyId);
        Assert.Equal(TenantSource.Membership, tenant.Source);
    }

    [Fact]
    public void An_empty_company_or_no_source_is_refused()
    {
        TenantContext tenant = Create();

        Assert.Throws<ArgumentException>(() => tenant.Bind(Guid.Empty, TenantSource.PublicSlug));
        Assert.Throws<ArgumentException>(() => tenant.Bind(Guid.NewGuid(), TenantSource.None));
        Assert.False(tenant.IsBound);
    }

    [Fact]
    public async Task Concurrent_attempts_to_bind_different_companies_leave_exactly_one_winner()
    {
        for (int round = 0; round < 50; round++)
        {
            TenantContext tenant = Create();
            Guid[] companies = Enumerable.Range(0, 16).Select(_ => Guid.NewGuid()).ToArray();
            int rebinds = 0;

            await Task.WhenAll(companies.Select(company => Task.Run(() =>
            {
                try
                {
                    tenant.Bind(company, TenantSource.Background);
                }
                catch (TenantRebindException)
                {
                    Interlocked.Increment(ref rebinds);
                }
            })));

            Assert.Equal(companies.Length - 1, rebinds);
            Assert.Contains(tenant.CompanyId!.Value, companies);
        }
    }
}
