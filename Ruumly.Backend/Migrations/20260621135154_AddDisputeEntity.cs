using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ruumly.Backend.Migrations
{
    /// <inheritdoc />
    public partial class AddDisputeEntity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Disputes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BookingId = table.Column<Guid>(type: "uuid", nullable: true),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: false),
                    RaisedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    RaisedByRole = table.Column<string>(type: "text", nullable: false),
                    Type = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    Subject = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    ContactEmail = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    AmountClaimed = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    EvidenceJson = table.Column<string>(type: "text", nullable: false),
                    AdminNotes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    Resolution = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ResolvedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Disputes", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Disputes_Status",
                table: "Disputes",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Disputes_SupplierId_Status",
                table: "Disputes",
                columns: new[] { "SupplierId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Disputes");
        }
    }
}
