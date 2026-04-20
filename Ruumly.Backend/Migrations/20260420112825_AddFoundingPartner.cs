using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ruumly.Backend.Migrations
{
    /// <inheritdoc />
    public partial class AddFoundingPartner : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "FoundingPartner",
                table: "Suppliers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "FoundingPartnerUntil",
                table: "Suppliers",
                type: "timestamp with time zone",
                nullable: true);

            // Grant founding partner status to all existing suppliers
            migrationBuilder.Sql("""
                UPDATE "Suppliers"
                SET "FoundingPartner" = true,
                    "FoundingPartnerUntil" = NOW() + INTERVAL '12 months'
                WHERE "CreatedAt" < NOW();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FoundingPartner",
                table: "Suppliers");

            migrationBuilder.DropColumn(
                name: "FoundingPartnerUntil",
                table: "Suppliers");
        }
    }
}
