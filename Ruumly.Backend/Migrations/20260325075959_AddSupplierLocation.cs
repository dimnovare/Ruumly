using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ruumly.Backend.Migrations
{
    /// <inheritdoc />
    public partial class AddSupplierLocation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "LocationId",
                table: "Listings",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "QuantityTotal",
                table: "Listings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "SizeM2",
                table: "Listings",
                type: "numeric(18,4)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SupplierLocations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Address = table.Column<string>(type: "text", nullable: false),
                    City = table.Column<string>(type: "text", nullable: false),
                    Lat = table.Column<double>(type: "double precision", nullable: false),
                    Lng = table.Column<double>(type: "double precision", nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupplierLocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupplierLocations_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "Suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Listings_LocationId",
                table: "Listings",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierLocations_SupplierId",
                table: "SupplierLocations",
                column: "SupplierId");

            migrationBuilder.AddForeignKey(
                name: "FK_Listings_SupplierLocations_LocationId",
                table: "Listings",
                column: "LocationId",
                principalTable: "SupplierLocations",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Listings_SupplierLocations_LocationId",
                table: "Listings");

            migrationBuilder.DropTable(
                name: "SupplierLocations");

            migrationBuilder.DropIndex(
                name: "IX_Listings_LocationId",
                table: "Listings");

            migrationBuilder.DropColumn(
                name: "LocationId",
                table: "Listings");

            migrationBuilder.DropColumn(
                name: "QuantityTotal",
                table: "Listings");

            migrationBuilder.DropColumn(
                name: "SizeM2",
                table: "Listings");
        }
    }
}
