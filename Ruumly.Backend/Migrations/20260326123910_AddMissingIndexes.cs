using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ruumly.Backend.Migrations
{
    /// <inheritdoc />
    public partial class AddMissingIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_TokenHash_IsRevoked",
                table: "RefreshTokens",
                columns: new[] { "TokenHash", "IsRevoked" });

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_CreatedAt",
                table: "Bookings",
                column: "CreatedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RefreshTokens_TokenHash_IsRevoked",
                table: "RefreshTokens");

            migrationBuilder.DropIndex(
                name: "IX_Bookings_CreatedAt",
                table: "Bookings");
        }
    }
}
