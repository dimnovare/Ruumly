using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ruumly.Backend.Migrations
{
    /// <inheritdoc />
    public partial class AddUserSupplierId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SupplierId",
                table: "Users",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_SupplierId",
                table: "Users",
                column: "SupplierId");

            migrationBuilder.AddForeignKey(
                name: "FK_Users_Suppliers_SupplierId",
                table: "Users",
                column: "SupplierId",
                principalTable: "Suppliers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Users_Suppliers_SupplierId",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Users_SupplierId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "SupplierId",
                table: "Users");
        }
    }
}
