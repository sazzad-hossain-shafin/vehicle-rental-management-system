using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using VehicleRental.Domain.Entities;
using VehicleRental.Infrastructure.Persistence;

namespace VehicleRental.Infrastructure.IntegrationTests;

/// <summary>
/// Phase 14A-3 model checks that need no database: the tenant columns are EF shadow properties, so the domain classes
/// (and with them the DTOs and the API) are exactly as before.
/// </summary>
public class TenancyModelTests
{
    private static VehicleRentalDbContext CreateContext() =>
        new(VehicleRentalDbContextOptions.Create("Host=localhost;Database=model_tests_never_connected"));

    [Theory]
    [InlineData(typeof(Vehicle))]
    [InlineData(typeof(Reservation))]
    [InlineData(typeof(Rental))]
    [InlineData(typeof(Customer))]
    public void Existing_domain_classes_know_nothing_about_companies(Type type)
    {
        string[] names = type.GetProperties().Select(p => p.Name).ToArray();

        Assert.DoesNotContain(names, n => n.Contains("Company", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(typeof(Vehicle), "CompanyId")]
    [InlineData(typeof(Reservation), "CompanyId")]
    [InlineData(typeof(Reservation), "CompanyCustomerId")]
    [InlineData(typeof(Rental), "CompanyId")]
    [InlineData(typeof(Rental), "CompanyCustomerId")]
    public void The_new_tenant_columns_are_nullable_shadow_properties(Type type, string column)
    {
        using var context = CreateContext();
        IProperty property = context.Model.FindEntityType(type)!.FindProperty(column)!;

        Assert.NotNull(property);
        Assert.True(property.IsShadowProperty());
        Assert.True(property.IsNullable);
        Assert.Equal(typeof(Guid?), property.ClrType);
    }

    [Fact]
    public void Customers_stay_global_with_no_company_column()
    {
        using var context = CreateContext();

        Assert.Null(context.Model.FindEntityType(typeof(Customer))!.FindProperty("CompanyId"));
    }

    [Fact]
    public void The_new_entities_use_domain_generated_guid_keys_and_restrictive_foreign_keys()
    {
        using var context = CreateContext();

        foreach (Type type in new[] { typeof(Company), typeof(CompanyMembership), typeof(CompanyCustomer) })
        {
            IEntityType entity = context.Model.FindEntityType(type)!;

            Assert.Equal(ValueGenerated.Never, entity.FindPrimaryKey()!.Properties.Single().ValueGenerated);
            Assert.All(entity.GetForeignKeys(), fk => Assert.Equal(DeleteBehavior.Restrict, fk.DeleteBehavior));
        }
    }

    [Fact]
    public void Enums_of_the_new_entities_are_stored_as_text()
    {
        using var context = CreateContext();

        Assert.Equal(typeof(string), context.Model.FindEntityType(typeof(Company))!.FindProperty(nameof(Company.Status))!.GetProviderClrType());
        Assert.Equal(typeof(string), context.Model.FindEntityType(typeof(CompanyMembership))!.FindProperty(nameof(CompanyMembership.Role))!.GetProviderClrType());
        Assert.Equal(typeof(string), context.Model.FindEntityType(typeof(CompanyMembership))!.FindProperty(nameof(CompanyMembership.Status))!.GetProviderClrType());
    }

    [Fact]
    public void The_always_null_tenant_columns_have_no_convention_index()
    {
        using var context = CreateContext();

        foreach ((Type type, string column) in new[]
                 {
                     (typeof(Reservation), "CompanyId"),
                     (typeof(Rental), "CompanyId"),
                     (typeof(CompanyMembership), nameof(CompanyMembership.CreatedByUserId)),
                 })
        {
            IEntityType entity = context.Model.FindEntityType(type)!;

            Assert.DoesNotContain(entity.GetIndexes(), i => i.Properties.Count == 1 && i.Properties[0].Name == column);
        }
    }

    [Fact]
    public void Other_foreign_keys_keep_their_convention_index()
    {
        using var context = CreateContext();

        // The customised convention only skips the three keys above: this one still gets its index as before.
        Assert.Contains(
            context.Model.FindEntityType(typeof(Rental))!.GetIndexes(),
            i => i.Properties.Count == 1 && i.Properties[0].Name == "CustomerId");
    }
}
