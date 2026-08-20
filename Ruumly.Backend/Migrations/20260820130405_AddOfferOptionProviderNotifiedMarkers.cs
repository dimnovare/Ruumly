using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ruumly.Backend.Migrations
{
    /// <inheritdoc />
    public partial class AddOfferOptionProviderNotifiedMarkers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ProviderNotifiedOutcomeAt",
                table: "OfferOptions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ProviderNotifiedSentAt",
                table: "OfferOptions",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ProviderNotifiedOutcomeAt",
                table: "OfferOptions");

            migrationBuilder.DropColumn(
                name: "ProviderNotifiedSentAt",
                table: "OfferOptions");
        }
    }
}
