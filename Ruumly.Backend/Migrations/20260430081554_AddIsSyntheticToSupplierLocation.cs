using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ruumly.Backend.Migrations
{
    /// <inheritdoc />
    public partial class AddIsSyntheticToSupplierLocation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsSynthetic",
                table: "SupplierLocations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Step 1: Mark existing auto-created Locations as synthetic.
            // A synthetic Location has no user-curated content — empty Description
            // AND empty/missing ImagesJson. The two original hand-seeded Locations
            // have non-empty Description and survive this update with IsSynthetic = false.
            migrationBuilder.Sql(@"
                UPDATE ""SupplierLocations""
                SET ""IsSynthetic"" = true
                WHERE (""Description"" IS NULL OR ""Description"" = '')
                  AND (""ImagesJson"" IS NULL OR ""ImagesJson"" = '' OR ""ImagesJson"" = '[]');
            ");

            // Step 2: Split mixed-type synthetic Locations.
            // BackfillSupplierLocations grouped by (SupplierId, City) only, conflating
            // Moving + Warehouse + Trailer listings under one Location. For each
            // synthetic Location with multiple distinct Listing.Type values, keep the
            // lowest-numbered Type pointing at the original Location and create a new
            // synthetic Location for each other Type, re-linking those listings.
            migrationBuilder.Sql(@"
                DO $$
                DECLARE
                    loc_row      RECORD;
                    type_row     RECORD;
                    primary_type TEXT;
                    new_loc_id   UUID;
                BEGIN
                    FOR loc_row IN
                        SELECT sl.""Id"", sl.""SupplierId"", sl.""City"", sl.""Country"",
                               sl.""Address"", sl.""Lat"", sl.""Lng""
                        FROM ""SupplierLocations"" sl
                        WHERE sl.""IsSynthetic"" = true
                          AND (
                              SELECT COUNT(DISTINCT l.""Type"")
                              FROM ""Listings"" l
                              WHERE l.""LocationId"" = sl.""Id""
                          ) > 1
                    LOOP
                        SELECT MIN(l.""Type"") INTO primary_type
                        FROM ""Listings"" l
                        WHERE l.""LocationId"" = loc_row.""Id"";

                        FOR type_row IN
                            SELECT DISTINCT l.""Type"" AS listing_type
                            FROM ""Listings"" l
                            WHERE l.""LocationId"" = loc_row.""Id""
                              AND l.""Type"" <> primary_type
                        LOOP
                            new_loc_id := gen_random_uuid();

                            INSERT INTO ""SupplierLocations"" (
                                ""Id"", ""SupplierId"", ""Name"", ""Address"", ""City"", ""Country"",
                                ""Lat"", ""Lng"", ""IsActive"", ""Images"", ""ImagesJson"", ""Description"",
                                ""IsSynthetic"", ""CreatedAt"", ""UpdatedAt""
                            ) VALUES (
                                new_loc_id,
                                loc_row.""SupplierId"",
                                COALESCE(NULLIF(loc_row.""City"", ''), 'Location'),
                                loc_row.""Address"",
                                loc_row.""City"",
                                COALESCE(loc_row.""Country"", 'EE'),
                                loc_row.""Lat"",
                                loc_row.""Lng"",
                                true, '{}'::text[], '[]', '',
                                true,
                                NOW() AT TIME ZONE 'UTC',
                                NOW() AT TIME ZONE 'UTC'
                            );

                            UPDATE ""Listings""
                            SET ""LocationId"" = new_loc_id
                            WHERE ""LocationId"" = loc_row.""Id""
                              AND ""Type"" = type_row.listing_type;
                        END LOOP;
                    END LOOP;
                END $$;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsSynthetic",
                table: "SupplierLocations");
        }
    }
}
