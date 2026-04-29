using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ruumly.Backend.Migrations
{
    /// <inheritdoc />
    public partial class EnableUnaccentInSearch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Enable unaccent so the trigger can fold diacritics. Idempotent.
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS unaccent;");

            // Drop existing trigger + function before recreating with unaccent applied
            // to each indexed column. Weights match the original AddFullTextSearch
            // migration: Title(A), City(B), Address(C), Description(D).
            migrationBuilder.Sql(@"DROP TRIGGER IF EXISTS trg_listings_search ON ""Listings"";");
            migrationBuilder.Sql(@"DROP FUNCTION IF EXISTS listings_search_update();");

            migrationBuilder.Sql(@"
CREATE OR REPLACE FUNCTION listings_search_update() RETURNS trigger AS $$
BEGIN
  NEW.""SearchVector"" :=
    setweight(to_tsvector('simple', unaccent(COALESCE(NEW.""Title"",       ''))), 'A') ||
    setweight(to_tsvector('simple', unaccent(COALESCE(NEW.""City"",        ''))), 'B') ||
    setweight(to_tsvector('simple', unaccent(COALESCE(NEW.""Address"",     ''))), 'C') ||
    setweight(to_tsvector('simple', unaccent(COALESCE(NEW.""Description"", ''))), 'D');
  RETURN NEW;
END;
$$ LANGUAGE plpgsql;
");

            migrationBuilder.Sql(@"
CREATE TRIGGER trg_listings_search
BEFORE INSERT OR UPDATE ON ""Listings""
FOR EACH ROW EXECUTE FUNCTION listings_search_update();
");

            // Force re-trigger on existing rows so historical data gets re-indexed
            // through the new unaccented function.
            migrationBuilder.Sql(@"UPDATE ""Listings"" SET ""Title"" = ""Title"";");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Restore the pre-unaccent trigger function. Extension itself is left
            // installed — dropping it would force-cascade other dependents.
            migrationBuilder.Sql(@"DROP TRIGGER IF EXISTS trg_listings_search ON ""Listings"";");
            migrationBuilder.Sql(@"DROP FUNCTION IF EXISTS listings_search_update();");

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

            migrationBuilder.Sql(@"
CREATE TRIGGER trg_listings_search
BEFORE INSERT OR UPDATE ON ""Listings""
FOR EACH ROW EXECUTE FUNCTION listings_search_update();
");

            migrationBuilder.Sql(@"UPDATE ""Listings"" SET ""Title"" = ""Title"";");
        }
    }
}
