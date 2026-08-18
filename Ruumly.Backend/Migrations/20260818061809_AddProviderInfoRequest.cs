using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ruumly.Backend.Migrations
{
    /// <inheritdoc />
    public partial class AddProviderInfoRequest : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProviderInfoRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DemandLeadId = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProviderOutreachId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReasonsJson = table.Column<string>(type: "text", nullable: true),
                    Note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ResolvedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProviderInfoRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProviderInfoRequests_DemandLeads_DemandLeadId",
                        column: x => x.DemandLeadId,
                        principalTable: "DemandLeads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProviderInfoRequests_ProviderOutreaches_ProviderOutreachId",
                        column: x => x.ProviderOutreachId,
                        principalTable: "ProviderOutreaches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProviderInfoRequests_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "Suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProviderInfoRequests_DemandLeadId",
                table: "ProviderInfoRequests",
                column: "DemandLeadId");

            migrationBuilder.CreateIndex(
                name: "IX_ProviderInfoRequests_ProviderOutreachId",
                table: "ProviderInfoRequests",
                column: "ProviderOutreachId");

            migrationBuilder.CreateIndex(
                name: "IX_ProviderInfoRequests_SupplierId",
                table: "ProviderInfoRequests",
                column: "SupplierId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProviderInfoRequests");
        }
    }
}
