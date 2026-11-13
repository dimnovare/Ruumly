using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ruumly.Backend.Migrations
{
    /// <inheritdoc />
    public partial class AddPartnerConfirmation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PartnerConfirmAnswer",
                table: "Suppliers",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PartnerConfirmAnsweredAt",
                table: "Suppliers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PartnerConfirmExpiredAt",
                table: "Suppliers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PartnerConfirmInbox",
                table: "Suppliers",
                type: "character varying(320)",
                maxLength: 320,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PartnerConfirmReminderSentAt",
                table: "Suppliers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PartnerConfirmSentAt",
                table: "Suppliers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PartnerConfirmToken",
                table: "Suppliers",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Suppliers_PartnerConfirmToken",
                table: "Suppliers",
                column: "PartnerConfirmToken",
                unique: true,
                filter: "\"PartnerConfirmToken\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Suppliers_PartnerConfirmToken",
                table: "Suppliers");

            migrationBuilder.DropColumn(
                name: "PartnerConfirmAnswer",
                table: "Suppliers");

            migrationBuilder.DropColumn(
                name: "PartnerConfirmAnsweredAt",
                table: "Suppliers");

            migrationBuilder.DropColumn(
                name: "PartnerConfirmExpiredAt",
                table: "Suppliers");

            migrationBuilder.DropColumn(
                name: "PartnerConfirmInbox",
                table: "Suppliers");

            migrationBuilder.DropColumn(
                name: "PartnerConfirmReminderSentAt",
                table: "Suppliers");

            migrationBuilder.DropColumn(
                name: "PartnerConfirmSentAt",
                table: "Suppliers");

            migrationBuilder.DropColumn(
                name: "PartnerConfirmToken",
                table: "Suppliers");
        }
    }
}
