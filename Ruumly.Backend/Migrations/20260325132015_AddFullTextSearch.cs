using Microsoft.EntityFrameworkCore.Migrations;
using NpgsqlTypes;

#nullable disable

namespace Ruumly.Backend.Migrations
{
    /// <inheritdoc />
    public partial class AddFullTextSearch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Column
            migrationBuilder.AddColumn<NpgsqlTsVector>(
                name: "SearchVector",
                table: "Listings",
                type: "tsvector",
                nullable: true);

            // 2. GIN index for fast full-text lookups
            migrationBuilder.CreateIndex(
                name: "IX_Listings_SearchVector",
                table: "Listings",
                column: "SearchVector")
                .Annotation("Npgsql:IndexMethod", "GIN");

            // 3. Trigger function — weights: Title(A) City(B) Address(C) Description(D)
            migrationBuilder.Sql(@"
CREATE OR REPLACE FUNCTION listings_search_update() RETURNS trigger AS $$
BEGIN
  NEW.""SearchVector"" :=
    setweight(to_tsvector('simple', COALESCE(NEW.""Title"",       '')), 'A') ||
    setweight(to_tsvector('simple', COALESCE(NEW.""City"",        '')), 'B') ||
    setweight(to_tsvector('simple', COALESCE(NEW.""Address"",     '')), 'C') ||
    setweight(to_tsvector('simple', COALESCE(NEW.""Description"", '')), 'D');
  RETURN NEW;
END;
$$ LANGUAGE plpgsql;
");

            // 4. Trigger — fires BEFORE INSERT OR UPDATE
            migrationBuilder.Sql(@"
CREATE TRIGGER trg_listings_search
BEFORE INSERT OR UPDATE ON ""Listings""
FOR EACH ROW EXECUTE FUNCTION listings_search_update();
");

            // 5. Populate existing rows
            migrationBuilder.Sql(@"UPDATE ""Listings"" SET ""Title"" = ""Title"";");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP TRIGGER IF EXISTS trg_listings_search ON ""Listings"";");
            migrationBuilder.Sql(@"DROP FUNCTION IF EXISTS listings_search_update();");

            migrationBuilder.DropIndex(
                name: "IX_Listings_SearchVector",
                table: "Listings");

            migrationBuilder.DropColumn(
                name: "SearchVector",
                table: "Listings");
        }
    }
}
