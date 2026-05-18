using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ruumly.Backend.Migrations
{
    /// <inheritdoc />
    public partial class AddPollingConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PollingEndpoint",
                table: "Suppliers",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalId",
                table: "SupplierLocations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PollMappingProfile",
                table: "IntegrationSettings",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PollingEndpoint",
                table: "Suppliers");

            migrationBuilder.DropColumn(
                name: "ExternalId",
                table: "SupplierLocations");

            migrationBuilder.DropColumn(
                name: "PollMappingProfile",
                table: "IntegrationSettings");
        }
    }
}
