using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ruumly.Backend.Migrations
{
    /// <inheritdoc />
    public partial class AddSlugAndPaymentOrderIdIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Suppliers_Slug",
                table: "Suppliers",
                column: "Slug",
                unique: true,
                filter: "\"Slug\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_PaymentOrderId",
                table: "Invoices",
                column: "PaymentOrderId",
                unique: true,
                filter: "\"PaymentOrderId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Suppliers_Slug",
                table: "Suppliers");

            migrationBuilder.DropIndex(
                name: "IX_Invoices_PaymentOrderId",
                table: "Invoices");
        }
    }
}
