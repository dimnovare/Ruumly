using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ruumly.Backend.Migrations
{
    /// <inheritdoc />
    public partial class AddDemandLeadRouting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ListingId",
                table: "DemandLeads",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Name",
                table: "DemandLeads",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Phone",
                table: "DemandLeads",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProviderNotes",
                table: "DemandLeads",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "QuotedAt",
                table: "DemandLeads",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "QuotedPrice",
                table: "DemandLeads",
                type: "numeric(18,4)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RespondedAt",
                table: "DemandLeads",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SupplierId",
                table: "DemandLeads",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_DemandLeads_SupplierId",
                table: "DemandLeads",
                column: "SupplierId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DemandLeads_SupplierId",
                table: "DemandLeads");

            migrationBuilder.DropColumn(
                name: "ListingId",
                table: "DemandLeads");

            migrationBuilder.DropColumn(
                name: "Name",
                table: "DemandLeads");

            migrationBuilder.DropColumn(
                name: "Phone",
                table: "DemandLeads");

            migrationBuilder.DropColumn(
                name: "ProviderNotes",
                table: "DemandLeads");

            migrationBuilder.DropColumn(
                name: "QuotedAt",
                table: "DemandLeads");

            migrationBuilder.DropColumn(
                name: "QuotedPrice",
                table: "DemandLeads");

            migrationBuilder.DropColumn(
                name: "RespondedAt",
                table: "DemandLeads");

            migrationBuilder.DropColumn(
                name: "SupplierId",
                table: "DemandLeads");
        }
    }
}
