using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ruumly.Backend.Migrations
{
    /// <inheritdoc />
    public partial class ExtrasPartnerPricing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "CustomerPriceOverride",
                table: "ListingExtras",
                type: "numeric(18,4)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PartnerDiscountRate",
                table: "ListingExtras",
                type: "numeric(18,4)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PublicPrice",
                table: "ListingExtras",
                type: "numeric(18,4)",
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CustomerPriceOverride",
                table: "ListingExtras");

            migrationBuilder.DropColumn(
                name: "PartnerDiscountRate",
                table: "ListingExtras");

            migrationBuilder.DropColumn(
                name: "PublicPrice",
                table: "ListingExtras");
        }
    }
}
