using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ruumly.Backend.Migrations
{
    /// <inheritdoc />
    public partial class AddOfferLoop : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Offers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DemandLeadId = table.Column<Guid>(type: "uuid", nullable: false),
                    Token = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    Language = table.Column<string>(type: "text", nullable: false),
                    CustomerNote = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ViewedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ChosenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ChosenOptionId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Offers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Offers_DemandLeads_DemandLeadId",
                        column: x => x.DemandLeadId,
                        principalTable: "DemandLeads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProviderOutreaches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DemandLeadId = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: false),
                    SentTo = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    Note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProviderOutreaches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProviderOutreaches_DemandLeads_DemandLeadId",
                        column: x => x.DemandLeadId,
                        principalTable: "DemandLeads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProviderOutreaches_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "Suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OfferOptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OfferId = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: true),
                    SupplierLocationId = table.Column<Guid>(type: "uuid", nullable: true),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    PriceAmount = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    PriceUnit = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OfferOptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OfferOptions_Offers_OfferId",
                        column: x => x.OfferId,
                        principalTable: "Offers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OfferOptions_SupplierLocations_SupplierLocationId",
                        column: x => x.SupplierLocationId,
                        principalTable: "SupplierLocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_OfferOptions_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "Suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OfferOptions_OfferId",
                table: "OfferOptions",
                column: "OfferId");

            migrationBuilder.CreateIndex(
                name: "IX_OfferOptions_SupplierId",
                table: "OfferOptions",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_OfferOptions_SupplierLocationId",
                table: "OfferOptions",
                column: "SupplierLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_Offers_DemandLeadId",
                table: "Offers",
                column: "DemandLeadId");

            migrationBuilder.CreateIndex(
                name: "IX_Offers_Token",
                table: "Offers",
                column: "Token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProviderOutreaches_DemandLeadId",
                table: "ProviderOutreaches",
                column: "DemandLeadId");

            migrationBuilder.CreateIndex(
                name: "IX_ProviderOutreaches_SupplierId",
                table: "ProviderOutreaches",
                column: "SupplierId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OfferOptions");

            migrationBuilder.DropTable(
                name: "ProviderOutreaches");

            migrationBuilder.DropTable(
                name: "Offers");
        }
    }
}
