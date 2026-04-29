using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ruumly.Backend.Migrations
{
    /// <inheritdoc />
    public partial class BackfillSupplierLocations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // For each standalone listing (LocationId IS NULL), create a SupplierLocation
            // grouped by (SupplierId, City). Then point the listing at that Location.
            // Listings sharing the same supplier + city get a single shared Location.
            migrationBuilder.Sql(@"
                INSERT INTO ""SupplierLocations""
                    (""Id"", ""SupplierId"", ""Name"", ""Address"", ""City"", ""Country"",
                     ""Lat"", ""Lng"", ""IsActive"", ""Images"", ""ImagesJson"", ""Description"",
                     ""CreatedAt"", ""UpdatedAt"")
                SELECT
                    gen_random_uuid(),
                    l.""SupplierId"",
                    COALESCE(NULLIF(l.""City"", ''), 'Location') AS ""Name"",
                    MIN(l.""Address""),
                    l.""City"",
                    COALESCE(s.""Country"", 'EE'),
                    AVG(l.""Lat""),
                    AVG(l.""Lng""),
                    true,
                    '{}'::text[],
                    '[]',
                    '',
                    NOW() AT TIME ZONE 'UTC',
                    NOW() AT TIME ZONE 'UTC'
                FROM ""Listings"" l
                JOIN ""Suppliers"" s ON s.""Id"" = l.""SupplierId""
                WHERE l.""LocationId"" IS NULL AND l.""IsActive"" = true
                GROUP BY l.""SupplierId"", l.""City"", s.""Country"";

                UPDATE ""Listings"" l
                SET ""LocationId"" = sl.""Id""
                FROM ""SupplierLocations"" sl
                WHERE l.""LocationId"" IS NULL
                  AND l.""SupplierId"" = sl.""SupplierId""
                  AND l.""City"" = sl.""City"";
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Data migration — no automatic revert. SupplierLocations created here
            // can't be safely identified for deletion without risking removal of
            // a hand-created Location. Manual revert if needed:
            //   UPDATE "Listings" SET "LocationId" = NULL WHERE ...
            //   DELETE FROM "SupplierLocations" WHERE ...
        }
    }
}
