using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VehicleRental.Domain.Entities;

namespace VehicleRental.Infrastructure.Persistence.Configurations;

internal sealed class ReservationConfiguration : IEntityTypeConfiguration<Reservation>
{
    /// <summary>
    /// The name of the PostgreSQL exclusion constraint that forbids two active reservations of the same
    /// vehicle with overlapping dates. It cannot be expressed in the EF model, so the migration creates it
    /// with SQL (it needs the <c>btree_gist</c> extension to compare a vehicle ID and a date range together).
    /// </summary>
    public const string NoOverlapConstraint = "EX_Reservations_NoOverlappingActive";

    /// <summary>The name of the index that allows each rental to come from at most one reservation.</summary>
    public const string RentalIndex = "UX_Reservations_RentalId";

    public void Configure(EntityTypeBuilder<Reservation> builder)
    {
        // Integrity checks that back up the domain rules; the domain stays the source of the rules.
        builder.ToTable("Reservations", table =>
        {
            table.HasCheckConstraint("CK_Reservations_EndAfterStart", "\"EndDate\" > \"StartDate\"");
            table.HasCheckConstraint("CK_Reservations_BillableDays_Positive", "\"BillableDays\" >= 1");
            table.HasCheckConstraint("CK_Reservations_TotalCost_NotNegative", "\"TotalCost\" >= 0");
            table.HasCheckConstraint(
                "CK_Reservations_FulfilledHasRental",
                "(\"Status\" = 'Fulfilled') = (\"RentalId\" IS NOT NULL)");
            table.HasCheckConstraint(
                "CK_Reservations_CancelledHasTimestamp",
                "(\"Status\" = 'Cancelled') = (\"CancelledAt\" IS NOT NULL)");
        });

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();

        // Reservations are history too: deleting a customer or vehicle that has any is refused.
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

        // The rental started at pickup. Just an ID on the reservation; the domain does not navigate to it.
        builder.HasOne<Rental>()
            .WithMany()
            .HasForeignKey(r => r.RentalId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(r => r.StartDate).IsRequired();
        builder.Property(r => r.EndDate).IsRequired();
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(r => r.CreatedAt).IsRequired();
        builder.Property(r => r.CancelledAt);
        builder.Property(r => r.FulfilledAt);

        // The price quote taken when the reservation was made.
        builder.Property(r => r.DailyRateAtReservation).HasPrecision(10, 2);
        builder.Property(r => r.BillableDays);
        builder.Property(r => r.PricingDescription).IsRequired().HasMaxLength(100);
        builder.Property(r => r.TotalCost).HasPrecision(12, 2);

        // Optimistic concurrency: two requests changing one reservation (for example two pickups) cannot both save.
        builder.Property<uint>("xmin").IsRowVersion();

        builder.HasIndex(r => r.RentalId)
            .IsUnique()
            .HasFilter("\"RentalId\" IS NOT NULL")
            .HasDatabaseName(RentalIndex);

        // Serve "my reservations" (customer filter, ordered by start date) and the staff list.
        builder.HasIndex("CustomerId", nameof(Reservation.StartDate));
        builder.HasIndex(r => new { r.StartDate, r.Id });
    }
}
