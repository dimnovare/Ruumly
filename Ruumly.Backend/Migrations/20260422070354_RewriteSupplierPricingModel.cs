using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ruumly.Backend.Migrations
{
    /// <summary>
    /// WARNING: resets all suppliers to clean pricing state.
    /// Founding partner expiry flags cleared. All tiers reset to Starter.
    /// </summary>
    public partial class RewriteSupplierPricingModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Drop old columns
            migrationBuilder.DropColumn(name: "TrialEndsAt", table: "Suppliers");
            migrationBuilder.DropColumn(name: "FoundingPartnerUntil", table: "Suppliers");
            migrationBuilder.DropColumn(name: "FoundingPartnerReminderSentAt", table: "Suppliers");

            // Reset all suppliers to clean state (scorched earth)
            migrationBuilder.Sql("""
                UPDATE "Suppliers"
                SET "FoundingPartner" = false,
                    "SubscriptionEndsAt" = NULL,
                    "MonthlyFee" = 0,
                    "Tier" = 0
                WHERE true;
                """);

            // Add new columns
            migrationBuilder.AddColumn<DateTime>(
                name: "OnboardingStartedAt",
                table: "Suppliers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PriorityLevel",
                table: "Suppliers",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "IsVerified",
                table: "Suppliers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "VerifiedAt",
                table: "Suppliers",
                type: "timestamp with time zone",
                nullable: true);

            // Existing active suppliers: start their 90-day onboarding from today
            migrationBuilder.Sql("""
                UPDATE "Suppliers"
                SET "OnboardingStartedAt" = NOW()
                WHERE "IsActive" = true;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "OnboardingStartedAt", table: "Suppliers");
            migrationBuilder.DropColumn(name: "PriorityLevel", table: "Suppliers");
            migrationBuilder.DropColumn(name: "IsVerified", table: "Suppliers");
            migrationBuilder.DropColumn(name: "VerifiedAt", table: "Suppliers");

            migrationBuilder.AddColumn<DateTime>(
                name: "TrialEndsAt",
                table: "Suppliers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FoundingPartnerUntil",
                table: "Suppliers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FoundingPartnerReminderSentAt",
                table: "Suppliers",
                type: "timestamp with time zone",
                nullable: true);
        }
    }
}
