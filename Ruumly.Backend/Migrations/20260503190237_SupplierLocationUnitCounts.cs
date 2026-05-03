using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ruumly.Backend.Migrations
{
    /// <inheritdoc />
    public partial class SupplierLocationUnitCounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AvailableUnitCount",
                table: "SupplierLocations",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TotalUnitCount",
                table: "SupplierLocations",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AvailableUnitCount",
                table: "SupplierLocations");

            migrationBuilder.DropColumn(
                name: "TotalUnitCount",
                table: "SupplierLocations");
        }
    }
}
