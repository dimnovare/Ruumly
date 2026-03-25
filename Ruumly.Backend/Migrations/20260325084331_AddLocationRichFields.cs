using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ruumly.Backend.Migrations
{
    /// <inheritdoc />
    public partial class AddLocationRichFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "SupplierLocations",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<List<string>>(
                name: "Images",
                table: "SupplierLocations",
                type: "text[]",
                nullable: false);

            migrationBuilder.AddColumn<string>(
                name: "ImagesJson",
                table: "SupplierLocations",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "OpeningHours",
                table: "SupplierLocations",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SupplierLocations_City",
                table: "SupplierLocations",
                column: "City");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierLocations_IsActive_City",
                table: "SupplierLocations",
                columns: new[] { "IsActive", "City" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SupplierLocations_City",
                table: "SupplierLocations");

            migrationBuilder.DropIndex(
                name: "IX_SupplierLocations_IsActive_City",
                table: "SupplierLocations");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "SupplierLocations");

            migrationBuilder.DropColumn(
                name: "Images",
                table: "SupplierLocations");

            migrationBuilder.DropColumn(
                name: "ImagesJson",
                table: "SupplierLocations");

            migrationBuilder.DropColumn(
                name: "OpeningHours",
                table: "SupplierLocations");
        }
    }
}
