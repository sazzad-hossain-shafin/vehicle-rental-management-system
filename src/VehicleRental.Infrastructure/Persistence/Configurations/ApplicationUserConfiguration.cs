using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VehicleRental.Infrastructure.Identity;

namespace VehicleRental.Infrastructure.Persistence.Configurations;

internal sealed class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> builder)
    {
        builder.ToTable("Users");

        // A customer account points at its customer. Deleting a customer that has an account is refused,
        // so an account can never silently lose its customer (or take rental history with it).
        builder.HasOne(u => u.Customer)
            .WithMany()
            .HasForeignKey(u => u.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        // Each customer has at most one login. Staff and admin accounts (no customer) are unconstrained.
        builder.HasIndex(u => u.CustomerId)
            .IsUnique()
            .HasFilter("\"CustomerId\" IS NOT NULL")
            .HasDatabaseName("UX_Users_CustomerId");
    }
}
