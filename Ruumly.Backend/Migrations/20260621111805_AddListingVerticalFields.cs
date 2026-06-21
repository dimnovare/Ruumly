using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ruumly.Backend.Migrations
{
    /// <inheritdoc />
    public partial class AddListingVerticalFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "DepositAmount",
                table: "Listings",
                type: "numeric(18,4)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MinBookingMonths",
                table: "Listings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RequiresLicenseCategory",
                table: "Listings",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DepositAmount",
                table: "Listings");

            migrationBuilder.DropColumn(
                name: "MinBookingMonths",
                table: "Listings");

            migrationBuilder.DropColumn(
                name: "RequiresLicenseCategory",
                table: "Listings");
        }
    }
}
