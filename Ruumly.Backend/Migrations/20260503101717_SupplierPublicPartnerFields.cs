using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ruumly.Backend.Migrations
{
    /// <inheritdoc />
    public partial class SupplierPublicPartnerFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FoundedYear",
                table: "Suppliers",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HeroImageUrl",
                table: "Suppliers",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LogoUrl",
                table: "Suppliers",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LongDescriptionTranslationsJson",
                table: "Suppliers",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Slug",
                table: "Suppliers",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Tagline",
                table: "Suppliers",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WebsiteUrl",
                table: "Suppliers",
                type: "text",
                nullable: true);

            // Partial unique index: multiple NULLs allowed, every set value is globally unique.
            // EF's CreateIndex with HasFilter is provider-specific, so emit raw SQL.
            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX \"IX_Suppliers_Slug_Unique\" ON \"Suppliers\" (\"Slug\") WHERE \"Slug\" IS NOT NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_Suppliers_Slug_Unique\";");

            migrationBuilder.DropColumn(
                name: "FoundedYear",
                table: "Suppliers");

            migrationBuilder.DropColumn(
                name: "HeroImageUrl",
                table: "Suppliers");

            migrationBuilder.DropColumn(
                name: "LogoUrl",
                table: "Suppliers");

            migrationBuilder.DropColumn(
                name: "LongDescriptionTranslationsJson",
                table: "Suppliers");

            migrationBuilder.DropColumn(
                name: "Slug",
                table: "Suppliers");

            migrationBuilder.DropColumn(
                name: "Tagline",
                table: "Suppliers");

            migrationBuilder.DropColumn(
                name: "WebsiteUrl",
                table: "Suppliers");
        }
    }
}
