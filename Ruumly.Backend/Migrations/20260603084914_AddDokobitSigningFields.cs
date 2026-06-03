using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ruumly.Backend.Migrations
{
    /// <inheritdoc />
    public partial class AddDokobitSigningFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DokobitSigningToken",
                table: "SignedContracts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RenderedHtmlHash",
                table: "SignedContracts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SigningMethod",
                table: "SignedContracts",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "SignedContracts",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "VerifiedIdCode",
                table: "SignedContracts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VerifiedName",
                table: "SignedContracts",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DokobitSigningToken",
                table: "SignedContracts");

            migrationBuilder.DropColumn(
                name: "RenderedHtmlHash",
                table: "SignedContracts");

            migrationBuilder.DropColumn(
                name: "SigningMethod",
                table: "SignedContracts");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "SignedContracts");

            migrationBuilder.DropColumn(
                name: "VerifiedIdCode",
                table: "SignedContracts");

            migrationBuilder.DropColumn(
                name: "VerifiedName",
                table: "SignedContracts");
        }
    }
}
