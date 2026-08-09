using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ruumly.Backend.Migrations
{
    /// <inheritdoc />
    public partial class AddSupplierProfileClaims : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ClaimedAt",
                table: "Suppliers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ClaimedByEmail",
                table: "Suppliers",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SupplierClaims",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestedEmail = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    EmailMatched = table.Column<bool>(type: "boolean", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    TokenExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    VerifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SessionTokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    SessionExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RequestIp = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Language = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupplierClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupplierClaims_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "Suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SupplierClaims_SessionTokenHash",
                table: "SupplierClaims",
                column: "SessionTokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SupplierClaims_SupplierId",
                table: "SupplierClaims",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierClaims_TokenHash",
                table: "SupplierClaims",
                column: "TokenHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SupplierClaims");

            migrationBuilder.DropColumn(
                name: "ClaimedAt",
                table: "Suppliers");

            migrationBuilder.DropColumn(
                name: "ClaimedByEmail",
                table: "Suppliers");
        }
    }
}
