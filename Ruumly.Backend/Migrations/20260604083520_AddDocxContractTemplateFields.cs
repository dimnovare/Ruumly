using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ruumly.Backend.Migrations
{
    /// <inheritdoc />
    public partial class AddDocxContractTemplateFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DetectedTokens",
                table: "ContractTemplates",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DocxObjectKey",
                table: "ContractTemplates",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TemplateType",
                table: "ContractTemplates",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DetectedTokens",
                table: "ContractTemplates");

            migrationBuilder.DropColumn(
                name: "DocxObjectKey",
                table: "ContractTemplates");

            migrationBuilder.DropColumn(
                name: "TemplateType",
                table: "ContractTemplates");
        }
    }
}
