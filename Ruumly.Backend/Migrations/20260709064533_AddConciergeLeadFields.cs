using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ruumly.Backend.Migrations
{
    /// <inheritdoc />
    public partial class AddConciergeLeadFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ContactedAt",
                table: "DemandLeads",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Details",
                table: "DemandLeads",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "NeedDate",
                table: "DemandLeads",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "DemandLeads",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ToCity",
                table: "DemandLeads",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ContactedAt",
                table: "DemandLeads");

            migrationBuilder.DropColumn(
                name: "Details",
                table: "DemandLeads");

            migrationBuilder.DropColumn(
                name: "NeedDate",
                table: "DemandLeads");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "DemandLeads");

            migrationBuilder.DropColumn(
                name: "ToCity",
                table: "DemandLeads");
        }
    }
}
