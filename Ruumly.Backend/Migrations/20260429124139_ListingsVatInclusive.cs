using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ruumly.Backend.Migrations
{
    /// <inheritdoc />
    public partial class ListingsVatInclusive : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Flip existing listings to VAT-inclusive. PriceFrom value stays the same
            // — we're declaring "this number already includes VAT" rather than
            // mutating the price. Going forward, displayed price = price the
            // customer pays. Internal VAT split happens at invoice generation.
            migrationBuilder.Sql(@"
                UPDATE ""Listings"" SET ""PricesIncludeVat"" = true
                WHERE ""PricesIncludeVat"" = false;
            ");

            // Change the column default so EF Core uses true for new rows.
            migrationBuilder.AlterColumn<bool>(
                name: "PricesIncludeVat",
                table: "Listings",
                type: "boolean",
                nullable: false,
                defaultValue: true,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldDefaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<bool>(
                name: "PricesIncludeVat",
                table: "Listings",
                type: "boolean",
                nullable: false,
                defaultValue: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldDefaultValue: true);
        }
    }
}
