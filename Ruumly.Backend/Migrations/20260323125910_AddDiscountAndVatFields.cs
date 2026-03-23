using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ruumly.Backend.Migrations
{
    /// <inheritdoc />
    public partial class AddDiscountAndVatFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ClientDiscountRate",
                table: "Suppliers",
                type: "numeric(18,4)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "PartnerDiscountRate",
                table: "Suppliers",
                type: "numeric(18,4)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ClientDiscountRateOverride",
                table: "Listings",
                type: "numeric(18,4)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PartnerDiscountRateOverride",
                table: "Listings",
                type: "numeric(18,4)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "PricesIncludeVat",
                table: "Listings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "VatRate",
                table: "Listings",
                type: "numeric(18,4)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "VatAmount",
                table: "Bookings",
                type: "numeric(18,4)",
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ClientDiscountRate",
                table: "Suppliers");

            migrationBuilder.DropColumn(
                name: "PartnerDiscountRate",
                table: "Suppliers");

            migrationBuilder.DropColumn(
                name: "ClientDiscountRateOverride",
                table: "Listings");

            migrationBuilder.DropColumn(
                name: "PartnerDiscountRateOverride",
                table: "Listings");

            migrationBuilder.DropColumn(
                name: "PricesIncludeVat",
                table: "Listings");

            migrationBuilder.DropColumn(
                name: "VatRate",
                table: "Listings");

            migrationBuilder.DropColumn(
                name: "VatAmount",
                table: "Bookings");
        }
    }
}
