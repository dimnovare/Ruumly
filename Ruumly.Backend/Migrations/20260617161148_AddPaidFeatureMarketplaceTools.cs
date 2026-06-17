using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ruumly.Backend.Migrations
{
    /// <inheritdoc />
    public partial class AddPaidFeatureMarketplaceTools : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PaidFeatures",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Category = table.Column<string>(type: "text", nullable: false),
                    Scope = table.Column<string>(type: "text", nullable: false),
                    PriceAmount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    PriceCurrency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    BillingInterval = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaidFeatures", x => x.Id);
                });

            var now = new DateTime(2026, 6, 17, 0, 0, 0, DateTimeKind.Utc);
            migrationBuilder.InsertData(
                table: "PaidFeatures",
                columns: new[]
                {
                    "Id", "Code", "Name", "Description", "Category", "Scope",
                    "PriceAmount", "PriceCurrency", "BillingInterval",
                    "IsActive", "SortOrder", "CreatedAt", "UpdatedAt"
                },
                values: new object[,]
                {
                    {
                        new Guid("0eaa82e0-0c4a-43de-b7ea-8c5e798f0d01"),
                        "promoted_search",
                        "Promoted search placement",
                        "Boost partner listings higher in relevant search results for a fixed period.",
                        "Visibility", "Supplier", 49m, "EUR", "monthly", true, 10, now, now
                    },
                    {
                        new Guid("e4b0b176-1f2e-4d1a-a7f5-d8794ab40b02"),
                        "map_pin_highlight",
                        "Highlighted map pin",
                        "Make a partner location stand out on the public map.",
                        "Visibility", "Location", 29m, "EUR", "monthly", true, 20, now, now
                    },
                    {
                        new Guid("b9f6dcb5-4ab3-497d-8318-55f366e79b03"),
                        "homepage_city_slot",
                        "Homepage or city slot",
                        "Reserve a curated placement on the homepage or a city/category landing page.",
                        "Visibility", "Supplier", 99m, "EUR", "monthly", true, 30, now, now
                    },
                    {
                        new Guid("6c379745-3a79-4c8d-9e12-c2c978aa8b04"),
                        "listing_bump",
                        "Listing refresh bump",
                        "Refresh one listing's ranking freshness for a short promotional window.",
                        "Visibility", "Listing", 9m, "EUR", "one_time", true, 40, now, now
                    },
                    {
                        new Guid("fbc89975-32fd-42c2-9919-fca15a46b605"),
                        "verified_badge",
                        "Verified partner badge",
                        "Manual verification badge after admin checks company details and trust signals.",
                        "Trust", "Supplier", 29m, "EUR", "monthly", true, 50, now, now
                    },
                    {
                        new Guid("947b8c49-431a-4d08-a839-58f528c1dd06"),
                        "profile_seo_page",
                        "Enhanced SEO partner profile",
                        "Extra help building a stronger partner profile page for search visibility.",
                        "Trust", "Supplier", 19m, "EUR", "monthly", true, 60, now, now
                    },
                    {
                        new Guid("a30f256c-0a42-42c8-90b0-c42636536907"),
                        "photo_video_assist",
                        "Photo and video assistance",
                        "One-off assistance preparing better public listing media.",
                        "Trust", "Supplier", 99m, "EUR", "one_time", true, 70, now, now
                    },
                    {
                        new Guid("90a39845-338c-4af8-8dd5-cb03a34d8e08"),
                        "calendar_sync",
                        "Calendar sync",
                        "Enable calendar import/export support and occupancy visibility tools.",
                        "Operations", "Supplier", 19m, "EUR", "monthly", true, 80, now, now
                    },
                    {
                        new Guid("5e25fefe-a645-4f89-9158-5bd1610d2f09"),
                        "bulk_import_assist",
                        "Bulk import assistance",
                        "One-off help importing locations, units, pricing, and photos.",
                        "Operations", "Supplier", 99m, "EUR", "one_time", true, 90, now, now
                    },
                    {
                        new Guid("434148d5-9e2e-4530-b1c1-53b71f6bbd0a"),
                        "analytics_export",
                        "Analytics and lead export",
                        "Give partners reporting and export support once traffic starts flowing.",
                        "Operations", "Supplier", 29m, "EUR", "monthly", true, 100, now, now
                    },
                    {
                        new Guid("f96bb42b-0a5c-4b91-982a-4d744625150b"),
                        "booking_tools",
                        "Ruumly booking tools",
                        "Enable Ruumly-managed booking flow for partners who want structured reservations.",
                        "Commerce", "Supplier", 29m, "EUR", "monthly", true, 110, now, now
                    },
                    {
                        new Guid("4870f661-c831-4a83-a309-dd894509b60c"),
                        "contract_tools",
                        "Ruumly contract signing",
                        "Enable Ruumly's lightweight contract signing flow for storage bookings.",
                        "Commerce", "Supplier", 29m, "EUR", "monthly", true, 120, now, now
                    },
                    {
                        new Guid("f9dc1617-6310-4828-bf5e-9f7c2140710d"),
                        "ruumly_payment_collection",
                        "Ruumly payment collection",
                        "Let Ruumly collect customer payments and settle with the partner when enabled.",
                        "Commerce", "Supplier", 0m, "EUR", "manual", true, 130, now, now
                    }
                });

            migrationBuilder.CreateTable(
                name: "PaidFeatureRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: false),
                    PaidFeatureId = table.Column<Guid>(type: "uuid", nullable: false),
                    ListingId = table.Column<Guid>(type: "uuid", nullable: true),
                    LocationId = table.Column<Guid>(type: "uuid", nullable: true),
                    RequestedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReviewedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<string>(type: "text", nullable: false),
                    Message = table.Column<string>(type: "text", nullable: true),
                    AdminNotes = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaidFeatureRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PaidFeatureRequests_Listings_ListingId",
                        column: x => x.ListingId,
                        principalTable: "Listings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_PaidFeatureRequests_PaidFeatures_PaidFeatureId",
                        column: x => x.PaidFeatureId,
                        principalTable: "PaidFeatures",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PaidFeatureRequests_SupplierLocations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "SupplierLocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_PaidFeatureRequests_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "Suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SupplierPaidFeatures",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: false),
                    PaidFeatureId = table.Column<Guid>(type: "uuid", nullable: false),
                    ListingId = table.Column<Guid>(type: "uuid", nullable: true),
                    LocationId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    StartsAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndsAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AdminNotes = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupplierPaidFeatures", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupplierPaidFeatures_Listings_ListingId",
                        column: x => x.ListingId,
                        principalTable: "Listings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_SupplierPaidFeatures_PaidFeatures_PaidFeatureId",
                        column: x => x.PaidFeatureId,
                        principalTable: "PaidFeatures",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierPaidFeatures_SupplierLocations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "SupplierLocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_SupplierPaidFeatures_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "Suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PaidFeatureRequests_ListingId",
                table: "PaidFeatureRequests",
                column: "ListingId");

            migrationBuilder.CreateIndex(
                name: "IX_PaidFeatureRequests_LocationId",
                table: "PaidFeatureRequests",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_PaidFeatureRequests_PaidFeatureId",
                table: "PaidFeatureRequests",
                column: "PaidFeatureId");

            migrationBuilder.CreateIndex(
                name: "IX_PaidFeatureRequests_SupplierId_Status_CreatedAt",
                table: "PaidFeatureRequests",
                columns: new[] { "SupplierId", "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PaidFeatures_Code",
                table: "PaidFeatures",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SupplierPaidFeatures_ListingId",
                table: "SupplierPaidFeatures",
                column: "ListingId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierPaidFeatures_LocationId",
                table: "SupplierPaidFeatures",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierPaidFeatures_PaidFeatureId_SupplierId_ListingId_Loc~",
                table: "SupplierPaidFeatures",
                columns: new[] { "PaidFeatureId", "SupplierId", "ListingId", "LocationId" });

            migrationBuilder.CreateIndex(
                name: "IX_SupplierPaidFeatures_SupplierId_IsActive_EndsAt",
                table: "SupplierPaidFeatures",
                columns: new[] { "SupplierId", "IsActive", "EndsAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PaidFeatureRequests");

            migrationBuilder.DropTable(
                name: "SupplierPaidFeatures");

            migrationBuilder.DropTable(
                name: "PaidFeatures");
        }
    }
}
