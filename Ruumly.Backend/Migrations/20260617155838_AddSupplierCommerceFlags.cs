using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ruumly.Backend.Migrations
{
    /// <inheritdoc />
    public partial class AddSupplierCommerceFlags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "BookingEnabled",
                table: "Suppliers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ContractSigningEnabled",
                table: "Suppliers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "DirectPaymentEnabled",
                table: "Suppliers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "RuumlyPaymentEnabled",
                table: "Suppliers",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BookingEnabled",
                table: "Suppliers");

            migrationBuilder.DropColumn(
                name: "ContractSigningEnabled",
                table: "Suppliers");

            migrationBuilder.DropColumn(
                name: "DirectPaymentEnabled",
                table: "Suppliers");

            migrationBuilder.DropColumn(
                name: "RuumlyPaymentEnabled",
                table: "Suppliers");
        }
    }
}
