using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ruumly.Backend.Migrations
{
    /// <inheritdoc />
    public partial class AddBlockedDates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BlockedDates",
                columns: table => new
                {
                    Id         = table.Column<Guid>(type: "uuid", nullable: false),
                    LocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Date       = table.Column<DateOnly>(type: "date", nullable: false),
                    Reason     = table.Column<string>(type: "text", nullable: true),
                    CreatedAt  = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BlockedDates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BlockedDates_SupplierLocations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "SupplierLocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BlockedDates_LocationId_Date",
                table: "BlockedDates",
                columns: new[] { "LocationId", "Date" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "BlockedDates");
        }
    }
}
