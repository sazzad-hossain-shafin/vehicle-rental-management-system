using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VehicleRental.Domain.Entities;

namespace VehicleRental.Infrastructure.Persistence.Configurations;

internal sealed class VehicleConfiguration : IEntityTypeConfiguration<Vehicle>
{
    public void Configure(EntityTypeBuilder<Vehicle> builder)
    {
        builder.ToTable("Vehicles", table =>
            table.HasCheckConstraint("CK_Vehicles_DailyRate_Positive", "\"DailyRate\" > 0"));

        builder.HasKey(v => v.Id);
        builder.Property(v => v.Id).ValueGeneratedNever();

        // Business key. The "C" collation compares bytes, so ordering and equality match the
        // normalized (upper-case) form the domain stores, whatever the server's locale.
        builder.Property(v => v.RegistrationNumber)
            .IsRequired()
            .HasMaxLength(Vehicle.RegistrationNumberMaxLength)
            .UseCollation("C");
        builder.HasIndex(v => v.RegistrationNumber).IsUnique();

        builder.Property(v => v.Make).IsRequired().HasMaxLength(Vehicle.MakeMaxLength);
        builder.Property(v => v.Model).IsRequired().HasMaxLength(Vehicle.ModelMaxLength);
        builder.Property(v => v.Year);

        // Enums are stored as text so the data stays readable and reordering an enum cannot corrupt it.
        builder.Property(v => v.VehicleType).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(v => v.AvailabilityStatus).HasConversion<string>().HasMaxLength(20).IsRequired();

        builder.Property(v => v.DailyRate).HasPrecision(10, 2);

        // Optimistic concurrency: PostgreSQL's built-in row version (the xmin system column).
        // A shadow property, so the domain class does not know about it.
        builder.Property<uint>("xmin").IsRowVersion();

        // Serves the search screen: availability and type filters.
        builder.HasIndex(v => new { v.AvailabilityStatus, v.VehicleType });
    }
}
