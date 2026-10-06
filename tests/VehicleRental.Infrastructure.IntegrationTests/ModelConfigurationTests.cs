using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using VehicleRental.Domain.Entities;
using VehicleRental.Infrastructure.Persistence;

namespace VehicleRental.Infrastructure.IntegrationTests;

/// <summary>
/// Checks the EF Core model and the migration without a database: the context is built but never
/// connects. These tests always run, so the mapping rules are verified even where PostgreSQL is not available.
/// </summary>
public class ModelConfigurationTests
{
    private static VehicleRentalDbContext CreateContext() =>
        new(VehicleRentalDbContextOptions.Create("Host=localhost;Database=model_tests_never_connected"));

    private static IEntityType EntityType<T>(VehicleRentalDbContext context) =>
        context.Model.FindEntityType(typeof(T))!;

    [Fact]
    public void Migrations_MatchTheModel_WithNothingPending()
    {
        using var context = CreateContext();

        Assert.False(
            context.Database.HasPendingModelChanges(),
            "The model has changes that no migration contains. Add a migration.");
    }

    [Fact]
    public void Migrations_ContainTheInitialCreateMigration()
    {
        using var context = CreateContext();

        var migrations = context.Database.GetMigrations().ToList();

        Assert.Contains(migrations, m => m.EndsWith("_InitialCreate"));
    }

    [Fact]
    public void Vehicle_RegistrationNumber_IsRequiredBoundedAndUnique()
    {
        using var context = CreateContext();
        var entity = EntityType<Vehicle>(context);
        var property = entity.FindProperty(nameof(Vehicle.RegistrationNumber))!;

        Assert.False(property.IsNullable);
        Assert.Equal(Vehicle.RegistrationNumberMaxLength, property.GetMaxLength());
        Assert.Contains(entity.GetIndexes(), i => i.IsUnique && i.Properties.Single() == property);
    }

    [Fact]
    public void Customer_CustomerNumber_IsRequiredBoundedAndUnique()
    {
        using var context = CreateContext();
        var entity = EntityType<Customer>(context);
        var property = entity.FindProperty(nameof(Customer.CustomerNumber))!;

        Assert.False(property.IsNullable);
        Assert.Equal(Customer.CustomerNumberMaxLength, property.GetMaxLength());
        Assert.Contains(entity.GetIndexes(), i => i.IsUnique && i.Properties.Single() == property);
    }

    [Fact]
    public void AllEntities_UseGeneratedGuidKeysThatTheDatabaseDoesNotGenerate()
    {
        using var context = CreateContext();

        foreach (var type in new[] { EntityType<Vehicle>(context), EntityType<Customer>(context), EntityType<Rental>(context) })
        {
            var key = type.FindPrimaryKey()!.Properties.Single();

            Assert.Equal(typeof(Guid), key.ClrType);
            Assert.Equal(ValueGenerated.Never, key.ValueGenerated);
        }
    }

    [Fact]
    public void Money_IsStoredWithExplicitDecimalPrecision()
    {
        using var context = CreateContext();

        Assert.Equal(10, EntityType<Vehicle>(context).FindProperty(nameof(Vehicle.DailyRate))!.GetPrecision());
        Assert.Equal(2, EntityType<Vehicle>(context).FindProperty(nameof(Vehicle.DailyRate))!.GetScale());
        Assert.Equal(10, EntityType<Rental>(context).FindProperty(nameof(Rental.DailyRateAtRental))!.GetPrecision());
        Assert.Equal(12, EntityType<Rental>(context).FindProperty(nameof(Rental.TotalCost))!.GetPrecision());
        Assert.Equal(2, EntityType<Rental>(context).FindProperty(nameof(Rental.TotalCost))!.GetScale());
    }

    [Fact]
    public void Enums_AreStoredAsText()
    {
        using var context = CreateContext();

        var stored = new[]
        {
            EntityType<Vehicle>(context).FindProperty(nameof(Vehicle.VehicleType))!,
            EntityType<Vehicle>(context).FindProperty(nameof(Vehicle.AvailabilityStatus))!,
            EntityType<Rental>(context).FindProperty(nameof(Rental.Status))!
        };

        foreach (var property in stored)
        {
            Assert.Equal(typeof(string), property.GetProviderClrType());
        }
    }

    [Fact]
    public void Rental_ForeignKeys_NeverCascadeDeleteHistory()
    {
        using var context = CreateContext();
        var foreignKeys = EntityType<Rental>(context).GetForeignKeys().ToList();

        Assert.Equal(2, foreignKeys.Count);
        Assert.All(foreignKeys, fk =>
        {
            Assert.Equal(DeleteBehavior.Restrict, fk.DeleteBehavior);
            Assert.True(fk.IsRequired);
        });
    }

    [Fact]
    public void VehicleAndRental_UseTheDatabaseRowVersionAsConcurrencyToken()
    {
        using var context = CreateContext();

        foreach (var type in new[] { EntityType<Vehicle>(context), EntityType<Rental>(context) })
        {
            var token = type.FindProperty("xmin");

            Assert.NotNull(token);
            Assert.True(token!.IsConcurrencyToken);
            Assert.True(token.IsShadowProperty(), "The domain class should not carry the concurrency token.");
        }
    }

    [Fact]
    public void Rental_HasAUniqueFilteredIndexAllowingOneActiveRentalPerVehicle()
    {
        using var context = CreateContext();
        var entity = EntityType<Rental>(context);

        var index = Assert.Single(entity.GetIndexes(), i => i.IsUnique);

        Assert.Equal("VehicleId", index.Properties.Single().Name);
        Assert.Contains("Active", index.GetFilter());
    }

    [Fact]
    public void Domain_ClassesCarryNoPersistenceAttributes()
    {
        var persistenceNamespaces = new[] { "System.ComponentModel.DataAnnotations", "Microsoft.EntityFrameworkCore" };

        foreach (var type in new[] { typeof(Vehicle), typeof(Customer), typeof(Rental) })
        {
            var attributeNamespaces = type.GetProperties()
                .SelectMany(p => p.GetCustomAttributes(inherit: true))
                .Concat(type.GetCustomAttributes(inherit: true))
                .Select(a => a.GetType().Namespace ?? "");

            Assert.DoesNotContain(attributeNamespaces, ns => persistenceNamespaces.Any(ns.StartsWith));
        }
    }

    [Fact]
    public void GeneratedSchema_ContainsTheExpectedConstraints()
    {
        using var context = CreateContext();

        string script = context.Database.GenerateCreateScript();

        Assert.Contains("UX_Rentals_ActiveRentalPerVehicle", script);
        Assert.Contains("ON DELETE RESTRICT", script);
        Assert.Contains("CK_Vehicles_DailyRate_Positive", script);
        Assert.Contains("IX_Vehicles_RegistrationNumber", script);
        Assert.Contains("IX_Customers_CustomerNumber", script);

        // Rental history is never cascade-deleted. (Identity's own join tables, such as a user's role
        // links, do cascade from their user, which is expected, so only the business tables are checked.)
        foreach (string table in new[] { "Vehicles", "Customers", "Rentals" })
        {
            Assert.DoesNotContain("ON DELETE CASCADE", CreateTableStatement(script, table));
        }
    }

    [Fact]
    public void CustomerAccounts_ReferenceTheirCustomer_WithoutCascadeAndAtMostOneEach()
    {
        using var context = CreateContext();
        var users = context.Model.FindEntityType(typeof(VehicleRental.Infrastructure.Identity.ApplicationUser))!;

        var customerLink = Assert.Single(users.GetForeignKeys());
        Assert.Equal(DeleteBehavior.Restrict, customerLink.DeleteBehavior);
        Assert.False(customerLink.IsRequired, "Staff and admin accounts have no customer.");

        var index = Assert.Single(users.GetIndexes(), i => i.IsUnique && i.Properties.Single().Name == "CustomerId");
        Assert.Contains("NOT NULL", index.GetFilter());
    }

    [Fact]
    public void Roles_AreSeededByTheMigration_WithFixedIds()
    {
        using var context = CreateContext();
        var designTimeModel = Microsoft.EntityFrameworkCore.Infrastructure.AccessorExtensions
            .GetService<Microsoft.EntityFrameworkCore.Metadata.IDesignTimeModel>(context).Model;
        var roles = designTimeModel.FindEntityType(typeof(Microsoft.AspNetCore.Identity.IdentityRole<Guid>))!;

        var seeded = roles.GetSeedData().Select(d => (string)d["Name"]!).Order().ToList();

        Assert.Equal(new[] { "Admin", "Customer", "Staff" }, seeded);
    }

    /// <summary>The CREATE TABLE statement for one table, up to the semicolon that ends it.</summary>
    private static string CreateTableStatement(string script, string table)
    {
        int start = script.IndexOf($"CREATE TABLE \"{table}\"", StringComparison.Ordinal);
        Assert.True(start >= 0, $"No CREATE TABLE for {table}.");

        return script[start..script.IndexOf(");", start, StringComparison.Ordinal)];
    }
}
