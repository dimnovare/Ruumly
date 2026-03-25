using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ruumly.Backend.Migrations
{
    /// <inheritdoc />
    public partial class AddListingSearchIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Listings_CreatedAt",
                table: "Listings",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Listings_IsActive",
                table: "Listings",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_Listings_IsActive_City",
                table: "Listings",
                columns: new[] { "IsActive", "City" });

            migrationBuilder.CreateIndex(
                name: "IX_Listings_IsActive_Type",
                table: "Listings",
                columns: new[] { "IsActive", "Type" });

            migrationBuilder.CreateIndex(
                name: "IX_Listings_PriceFrom",
                table: "Listings",
                column: "PriceFrom");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Listings_CreatedAt",
                table: "Listings");

            migrationBuilder.DropIndex(
                name: "IX_Listings_IsActive",
                table: "Listings");

            migrationBuilder.DropIndex(
                name: "IX_Listings_IsActive_City",
                table: "Listings");

            migrationBuilder.DropIndex(
                name: "IX_Listings_IsActive_Type",
                table: "Listings");

            migrationBuilder.DropIndex(
                name: "IX_Listings_PriceFrom",
                table: "Listings");
        }
    }
}
