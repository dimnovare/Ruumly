using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ruumly.Backend.Migrations
{
    /// <inheritdoc />
    public partial class AddProviderQuote : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "QuoteToken",
                table: "ProviderOutreaches",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "QuotedAmount",
                table: "ProviderOutreaches",
                type: "numeric(18,4)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "QuotedAt",
                table: "ProviderOutreaches",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "QuotedAvailability",
                table: "ProviderOutreaches",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "QuotedNote",
                table: "ProviderOutreaches",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "QuotedUnit",
                table: "ProviderOutreaches",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProviderOutreaches_QuoteToken",
                table: "ProviderOutreaches",
                column: "QuoteToken",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ProviderOutreaches_QuoteToken",
                table: "ProviderOutreaches");

            migrationBuilder.DropColumn(
                name: "QuoteToken",
                table: "ProviderOutreaches");

            migrationBuilder.DropColumn(
                name: "QuotedAmount",
                table: "ProviderOutreaches");

            migrationBuilder.DropColumn(
                name: "QuotedAt",
                table: "ProviderOutreaches");

            migrationBuilder.DropColumn(
                name: "QuotedAvailability",
                table: "ProviderOutreaches");

            migrationBuilder.DropColumn(
                name: "QuotedNote",
                table: "ProviderOutreaches");

            migrationBuilder.DropColumn(
                name: "QuotedUnit",
                table: "ProviderOutreaches");
        }
    }
}
