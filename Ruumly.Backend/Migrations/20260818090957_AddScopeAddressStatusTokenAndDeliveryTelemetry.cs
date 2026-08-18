using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ruumly.Backend.Migrations
{
    /// <inheritdoc />
    public partial class AddScopeAddressStatusTokenAndDeliveryTelemetry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DeliveredAt",
                table: "ProviderOutreaches",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "OpenedAt",
                table: "ProviderOutreaches",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FromAddress",
                table: "DemandLeads",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ScopeJson",
                table: "DemandLeads",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StatusToken",
                table: "DemandLeads",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ToAddress",
                table: "DemandLeads",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_DemandLeads_StatusToken",
                table: "DemandLeads",
                column: "StatusToken",
                unique: true,
                filter: "\"StatusToken\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DemandLeads_StatusToken",
                table: "DemandLeads");

            migrationBuilder.DropColumn(
                name: "DeliveredAt",
                table: "ProviderOutreaches");

            migrationBuilder.DropColumn(
                name: "OpenedAt",
                table: "ProviderOutreaches");

            migrationBuilder.DropColumn(
                name: "FromAddress",
                table: "DemandLeads");

            migrationBuilder.DropColumn(
                name: "ScopeJson",
                table: "DemandLeads");

            migrationBuilder.DropColumn(
                name: "StatusToken",
                table: "DemandLeads");

            migrationBuilder.DropColumn(
                name: "ToAddress",
                table: "DemandLeads");
        }
    }
}
