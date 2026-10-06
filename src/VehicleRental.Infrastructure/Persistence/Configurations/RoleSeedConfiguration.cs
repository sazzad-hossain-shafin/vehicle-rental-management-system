using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VehicleRental.Application.Security;

namespace VehicleRental.Infrastructure.Persistence.Configurations;

/// <summary>
/// The three roles are reference data, created by the migration itself with fixed IDs, so they exist in
/// every database without any startup code, and the application never has to create roles at runtime.
/// </summary>
internal sealed class RoleSeedConfiguration : IEntityTypeConfiguration<IdentityRole<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityRole<Guid>> builder)
    {
        builder.ToTable("Roles");

        builder.HasData(
            Role("5d1f6c7e-0b0a-4d1e-9a4e-2c5e8f1a0001", Roles.Admin, "c1a8e5f0-0001-4e0b-8a11-6d1b2f9a0001"),
            Role("5d1f6c7e-0b0a-4d1e-9a4e-2c5e8f1a0002", Roles.Staff, "c1a8e5f0-0002-4e0b-8a11-6d1b2f9a0002"),
            Role("5d1f6c7e-0b0a-4d1e-9a4e-2c5e8f1a0003", Roles.Customer, "c1a8e5f0-0003-4e0b-8a11-6d1b2f9a0003"));
    }

    private static IdentityRole<Guid> Role(string id, string name, string concurrencyStamp) =>
        new()
        {
            Id = Guid.Parse(id),
            Name = name,
            NormalizedName = name.ToUpperInvariant(),
            ConcurrencyStamp = concurrencyStamp
        };
}
