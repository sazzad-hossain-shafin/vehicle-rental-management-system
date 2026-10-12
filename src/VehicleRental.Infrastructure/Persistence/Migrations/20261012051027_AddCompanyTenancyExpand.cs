using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VehicleRental.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Phase 14A-3, the "expand" step of ADR 007. Purely additive: three new tables and nullable tenant columns on
    /// Vehicles, Reservations and Rentals. It contains no statement that reads or changes an existing row, and adding a
    /// nullable column without a default does not rewrite a table, so existing data (including row versions) is
    /// untouched. Nothing in the application uses the new structures yet.
    /// </summary>
    public partial class AddCompanyTenancyExpand : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Adding a column, an index or a foreign key briefly needs a lock. Give up after a few seconds rather than
            // queue behind a long transaction and block every booking that queues behind this migration. The migration
            // runs in one transaction, so SET LOCAL lasts until it ends and nothing is left half applied.
            migrationBuilder.Sql("SET LOCAL lock_timeout = '5s';");

            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                table: "Vehicles",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CompanyCustomerId",
                table: "Reservations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                table: "Reservations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CompanyCustomerId",
                table: "Rentals",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                table: "Rentals",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Companies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Slug = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false, collation: "C"),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    TimeZoneId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Companies", x => x.Id);
                    table.CheckConstraint("CK_Companies_ActiveIsApproved", "\"Status\" <> 'Active' OR \"ApprovedAt\" IS NOT NULL");
                    table.CheckConstraint("CK_Companies_Name_NotBlank", "char_length(btrim(\"Name\")) > 0");
                    table.CheckConstraint("CK_Companies_PendingIsNotApproved", "\"Status\" <> 'PendingReview' OR \"ApprovedAt\" IS NULL");
                    table.CheckConstraint("CK_Companies_Slug_Format", "\"Slug\" ~ '^[a-z0-9]+(-[a-z0-9]+)*$' AND char_length(\"Slug\") BETWEEN 3 AND 63");
                    table.CheckConstraint("CK_Companies_Status_Known", "\"Status\" IN ('PendingReview', 'Active', 'Suspended')");
                    table.CheckConstraint("CK_Companies_TimeZoneId_NotBlank", "char_length(btrim(\"TimeZoneId\")) > 0");
                });

            migrationBuilder.CreateTable(
                name: "CompanyCustomers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerNumber = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, collation: "C"),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanyCustomers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CompanyCustomers_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CompanyCustomers_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CompanyMemberships",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanyMemberships", x => x.Id);
                    table.CheckConstraint("CK_CompanyMemberships_Role_Known", "\"Role\" IN ('Owner', 'Staff')");
                    table.CheckConstraint("CK_CompanyMemberships_Status_Known", "\"Status\" IN ('Active', 'Removed')");
                    table.ForeignKey(
                        name: "FK_CompanyMemberships_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CompanyMemberships_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CompanyMemberships_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "UX_Vehicles_CompanyId_Id",
                table: "Vehicles",
                columns: new[] { "CompanyId", "Id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Companies_Slug",
                table: "Companies",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CompanyCustomers_CustomerId",
                table: "CompanyCustomers",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "UX_CompanyCustomers_CompanyId_CustomerId",
                table: "CompanyCustomers",
                columns: new[] { "CompanyId", "CustomerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_CompanyCustomers_CompanyId_CustomerNumber",
                table: "CompanyCustomers",
                columns: new[] { "CompanyId", "CustomerNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_CompanyCustomers_CompanyId_Id",
                table: "CompanyCustomers",
                columns: new[] { "CompanyId", "Id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CompanyMemberships_ActiveOwners",
                table: "CompanyMemberships",
                column: "CompanyId",
                filter: "\"Role\" = 'Owner' AND \"Status\" = 'Active'");

            migrationBuilder.CreateIndex(
                name: "IX_CompanyMemberships_UserId_Status",
                table: "CompanyMemberships",
                columns: new[] { "UserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "UX_CompanyMemberships_CompanyId_UserId",
                table: "CompanyMemberships",
                columns: new[] { "CompanyId", "UserId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Rentals_Companies_CompanyId",
                table: "Rentals",
                column: "CompanyId",
                principalTable: "Companies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Reservations_Companies_CompanyId",
                table: "Reservations",
                column: "CompanyId",
                principalTable: "Companies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Vehicles_Companies_CompanyId",
                table: "Vehicles",
                column: "CompanyId",
                principalTable: "Companies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("SET LOCAL lock_timeout = '5s';");

            // Rolling back is only safe while the new structures hold nothing. Once a company, a membership, a customer
            // link or a tenant column value exists, dropping them would silently destroy tenant data, so refuse. The way
            // out is deliberate: restore the pre-migration backup, or remove that data on purpose first.
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM "Companies")
                       OR EXISTS (SELECT 1 FROM "CompanyMemberships")
                       OR EXISTS (SELECT 1 FROM "CompanyCustomers")
                       OR EXISTS (SELECT 1 FROM "Vehicles" WHERE "CompanyId" IS NOT NULL)
                       OR EXISTS (SELECT 1 FROM "Reservations" WHERE "CompanyId" IS NOT NULL OR "CompanyCustomerId" IS NOT NULL)
                       OR EXISTS (SELECT 1 FROM "Rentals" WHERE "CompanyId" IS NOT NULL OR "CompanyCustomerId" IS NOT NULL)
                    THEN
                        RAISE EXCEPTION 'Refusing to roll back AddCompanyTenancyExpand: company or tenant data exists and would be lost. Restore the pre-migration backup, or remove that data deliberately first.'
                            USING ERRCODE = 'P0001';
                    END IF;
                END
                $$;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_Rentals_Companies_CompanyId",
                table: "Rentals");

            migrationBuilder.DropForeignKey(
                name: "FK_Reservations_Companies_CompanyId",
                table: "Reservations");

            migrationBuilder.DropForeignKey(
                name: "FK_Vehicles_Companies_CompanyId",
                table: "Vehicles");

            migrationBuilder.DropTable(
                name: "CompanyCustomers");

            migrationBuilder.DropTable(
                name: "CompanyMemberships");

            migrationBuilder.DropTable(
                name: "Companies");

            migrationBuilder.DropIndex(
                name: "UX_Vehicles_CompanyId_Id",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "CompanyCustomerId",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "CompanyCustomerId",
                table: "Rentals");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "Rentals");
        }
    }
}
