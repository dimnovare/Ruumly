using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ruumly.Backend.Migrations
{
    /// <inheritdoc />
    public partial class AddListingCityTrigram : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // pg_trgm must exist before gin_trgm_ops can be referenced.
            // IF NOT EXISTS makes this idempotent and safe to re-run.
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS pg_trgm;");

            migrationBuilder.CreateIndex(
                name: "IX_Listings_City_Trgm",
                table: "Listings",
                column: "City")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Listings_City_Trgm",
                table: "Listings");

            // Do NOT drop the extension in Down() — other indexes (e.g. SearchVector
            // full-text) may depend on it, and dropping extensions is destructive.
        }
    }
}
