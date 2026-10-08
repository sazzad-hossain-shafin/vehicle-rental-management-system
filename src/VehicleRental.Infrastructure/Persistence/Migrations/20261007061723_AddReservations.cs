using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VehicleRental.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReservations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Reservations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    VehicleId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CancelledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FulfilledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RentalId = table.Column<Guid>(type: "uuid", nullable: true),
                    DailyRateAtReservation = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    BillableDays = table.Column<int>(type: "integer", nullable: false),
                    PricingDescription = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    TotalCost = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Reservations", x => x.Id);
                    table.CheckConstraint("CK_Reservations_BillableDays_Positive", "\"BillableDays\" >= 1");
                    table.CheckConstraint("CK_Reservations_CancelledHasTimestamp", "(\"Status\" = 'Cancelled') = (\"CancelledAt\" IS NOT NULL)");
                    table.CheckConstraint("CK_Reservations_EndAfterStart", "\"EndDate\" > \"StartDate\"");
                    table.CheckConstraint("CK_Reservations_FulfilledHasRental", "(\"Status\" = 'Fulfilled') = (\"RentalId\" IS NOT NULL)");
                    table.CheckConstraint("CK_Reservations_TotalCost_NotNegative", "\"TotalCost\" >= 0");
                    table.ForeignKey(
                        name: "FK_Reservations_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Reservations_Rentals_RentalId",
                        column: x => x.RentalId,
                        principalTable: "Rentals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Reservations_Vehicles_VehicleId",
                        column: x => x.VehicleId,
                        principalTable: "Vehicles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_CustomerId_StartDate",
                table: "Reservations",
                columns: new[] { "CustomerId", "StartDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_StartDate_Id",
                table: "Reservations",
                columns: new[] { "StartDate", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_VehicleId",
                table: "Reservations",
                column: "VehicleId");

            migrationBuilder.CreateIndex(
                name: "UX_Reservations_RentalId",
                table: "Reservations",
                column: "RentalId",
                unique: true,
                filter: "\"RentalId\" IS NOT NULL");

            // The database-level guarantee against double booking. An exclusion constraint says: no two rows may
            // have the same VehicleId AND overlapping date ranges, where the range [StartDate, EndDate) is
            // half-open, so a booking that ends on a day does not clash with one that starts on that day. It
            // applies only to active reservations; cancelled and fulfilled ones no longer hold the vehicle.
            // Comparing a uuid with "=" inside a GiST index needs the btree_gist extension. It ships with
            // PostgreSQL and is marked trusted, so the database owner can create it without being a superuser.
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS btree_gist;");

            migrationBuilder.Sql(
                """
                ALTER TABLE "Reservations"
                    ADD CONSTRAINT "EX_Reservations_NoOverlappingActive"
                    EXCLUDE USING gist (
                        "VehicleId" WITH =,
                        daterange("StartDate", "EndDate", '[)') WITH &&)
                    WHERE ("Status" = 'Active');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Dropping the table drops its exclusion constraint. The btree_gist extension is left installed:
            // it is harmless, and dropping it could break anything else in the database that uses it.
            migrationBuilder.DropTable(
                name: "Reservations");
        }
    }
}
