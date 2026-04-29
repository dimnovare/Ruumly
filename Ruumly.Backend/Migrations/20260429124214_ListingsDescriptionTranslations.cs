using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ruumly.Backend.Migrations
{
    /// <inheritdoc />
    public partial class ListingsDescriptionTranslations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DescriptionTranslationsJson",
                table: "Listings",
                type: "text",
                nullable: false,
                defaultValue: "{}");

            // Backfill: copy existing Description into the JSON as the Estonian
            // value so all current listings have at least an ET translation.
            migrationBuilder.Sql(@"
                UPDATE ""Listings""
                SET ""DescriptionTranslationsJson"" = json_build_object('et', ""Description"")::text
                WHERE ""Description"" IS NOT NULL AND ""Description"" != '';
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DescriptionTranslationsJson",
                table: "Listings");
        }
    }
}
