using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ruumly.Backend.Migrations
{
    /// <inheritdoc />
    public partial class AllowNullRegistryCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Suppliers_RegistryCode",
                table: "Suppliers");

            migrationBuilder.AlterColumn<string>(
                name: "RegistryCode",
                table: "Suppliers",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.CreateIndex(
                name: "IX_Suppliers_RegistryCode",
                table: "Suppliers",
                column: "RegistryCode",
                unique: true,
                filter: "\"RegistryCode\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Suppliers_RegistryCode",
                table: "Suppliers");

            migrationBuilder.AlterColumn<string>(
                name: "RegistryCode",
                table: "Suppliers",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Suppliers_RegistryCode",
                table: "Suppliers",
                column: "RegistryCode",
                unique: true);
        }
    }
}
