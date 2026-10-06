using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VehicleRental.Domain.Entities;

namespace VehicleRental.Infrastructure.Persistence.Configurations;

internal sealed class RentalConfiguration : IEntityTypeConfiguration<Rental>
{
    /// <summary>The name of the index that allows only one active rental per vehicle.</summary>
    public const string ActiveRentalPerVehicleIndex = "UX_Rentals_ActiveRentalPerVehicle";

    public void Configure(EntityTypeBuilder<Rental> builder)
    {
        // Integrity checks that back up the domain rules; the domain stays the source of the rules.
        builder.ToTable("Rentals", table =>
        {
            table.HasCheckConstraint("CK_Rentals_BillableDays_Positive", "\"BillableDays\" >= 1");
            table.HasCheckConstraint("CK_Rentals_TotalCost_NotNegative", "\"TotalCost\" >= 0");
            table.HasCheckConstraint(
                "CK_Rentals_ReturnAfterStart",
                "\"ExpectedReturnDate\" > \"StartDate\"");
            table.HasCheckConstraint(
                "CK_Rentals_CompletedHasReturnDate",
                "(\"Status\" = 'Completed') = (\"ActualReturnDate\" IS NOT NULL)");
        });

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();

        // Rental history must outlive any change to customers or vehicles, so deleting either
        // is refused while rentals reference it, and nothing is ever cascaded.
        builder.HasOne(r => r.Customer)
            .WithMany()
            .HasForeignKey("CustomerId")
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.Vehicle)
            .WithMany()
            .HasForeignKey("VehicleId")
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(r => r.StartDate).IsRequired();
        builder.Property(r => r.ExpectedReturnDate).IsRequired();
        builder.Property(r => r.ActualReturnDate);

        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(20).IsRequired();

        // The price snapshot taken when the rental started.
        builder.Property(r => r.DailyRateAtRental).HasPrecision(10, 2);
        builder.Property(r => r.BillableDays);
        builder.Property(r => r.PricingDescription).IsRequired().HasMaxLength(100);
        builder.Property(r => r.TotalCost).HasPrecision(12, 2);

        builder.Property<uint>("xmin").IsRowVersion();

        // The database refuses a second active rental for a vehicle, however the request arrives.
        builder.HasIndex("VehicleId")
            .IsUnique()
            .HasFilter("\"Status\" = 'Active'")
            .HasDatabaseName(ActiveRentalPerVehicleIndex);

        builder.HasIndex(r => r.StartDate);
    }
}
