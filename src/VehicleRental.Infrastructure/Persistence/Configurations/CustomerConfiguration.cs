using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VehicleRental.Domain.Entities;

namespace VehicleRental.Infrastructure.Persistence.Configurations;

internal sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("Customers");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();

        // Business key, stored in the domain's normalized (upper-case) form.
        builder.Property(c => c.CustomerNumber)
            .IsRequired()
            .HasMaxLength(Customer.CustomerNumberMaxLength)
            .UseCollation("C");
        builder.HasIndex(c => c.CustomerNumber).IsUnique();

        builder.Property(c => c.Name).IsRequired().HasMaxLength(Customer.NameMaxLength);
    }
}
