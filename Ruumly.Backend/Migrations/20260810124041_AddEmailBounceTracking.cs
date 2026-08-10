using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ruumly.Backend.Migrations
{
    /// <inheritdoc />
    public partial class AddEmailBounceTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ContactEmailBounceReason",
                table: "Suppliers",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContactEmailBounceType",
                table: "Suppliers",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ContactEmailBouncedAt",
                table: "Suppliers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ContactEmailUnusable",
                table: "Suppliers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "EmailDeliveryEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventId = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    EventType = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Recipient = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    BounceType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    BounceSubType = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: true),
                    OutreachRowsUpdated = table.Column<int>(type: "integer", nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReceivedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailDeliveryEvents", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EmailDeliveryEvents_EventId",
                table: "EmailDeliveryEvents",
                column: "EventId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EmailDeliveryEvents");

            migrationBuilder.DropColumn(
                name: "ContactEmailBounceReason",
                table: "Suppliers");

            migrationBuilder.DropColumn(
                name: "ContactEmailBounceType",
                table: "Suppliers");

            migrationBuilder.DropColumn(
                name: "ContactEmailBouncedAt",
                table: "Suppliers");

            migrationBuilder.DropColumn(
                name: "ContactEmailUnusable",
                table: "Suppliers");
        }
    }
}
