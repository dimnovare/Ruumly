using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ruumly.Backend.Migrations
{
    /// <inheritdoc />
    public partial class NormalizeListingTypeCasing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Listing.Type is a string-converted enum. EF reads case-insensitively
            // (so legacy lowercase rows materialise fine), but it WRITES and QUERIES
            // with the canonical enum-name casing ("Warehouse"/"Moving"/"Trailer").
            // Any row stored lowercase therefore silently fails every type filter
            // (search verticals, the homepage map, locations-by-type), returning
            // zero rows. Normalise legacy casing to the canonical form. Idempotent.
            migrationBuilder.Sql("UPDATE \"Listings\" SET \"Type\" = 'Warehouse' WHERE \"Type\" = 'warehouse';");
            migrationBuilder.Sql("UPDATE \"Listings\" SET \"Type\" = 'Moving'    WHERE \"Type\" = 'moving';");
            migrationBuilder.Sql("UPDATE \"Listings\" SET \"Type\" = 'Trailer'   WHERE \"Type\" = 'trailer';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No-op: canonical casing is always the correct state; there is no
            // meaningful inverse to "fix the data".
        }
    }
}
