using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ruumly.Backend.Migrations
{
    /// <inheritdoc />
    public partial class AddRebateInvoices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RebateInvoices",
                columns: table => new
                {
                    Id          = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierId  = table.Column<Guid>(type: "uuid", nullable: false),
                    Period      = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TotalMargin = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    OrderCount  = table.Column<int>(type: "integer", nullable: false),
                    Status      = table.Column<string>(type: "text", nullable: false, defaultValue: "Draft"),
                    SentAt      = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PaidAt      = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Notes       = table.Column<string>(type: "text", nullable: true),
                    CreatedAt   = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt   = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RebateInvoices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RebateInvoices_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "Suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RebateInvoices_SupplierId_Period",
                table: "RebateInvoices",
                columns: new[] { "SupplierId", "Period" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "RebateInvoices");
        }
    }
}
