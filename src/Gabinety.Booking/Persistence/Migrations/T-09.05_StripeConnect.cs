using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gabinety.Booking.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class T0905_StripeConnect : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "attempt_count",
                table: "payouts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "next_attempt_at",
                table: "payouts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "practitioner_payment_accounts",
                columns: table => new
                {
                    practitioner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    provider_account_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ready = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_practitioner_payment_accounts", x => x.practitioner_id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_practitioner_payment_accounts_provider_account_id",
                table: "practitioner_payment_accounts",
                column: "provider_account_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "practitioner_payment_accounts");

            migrationBuilder.DropColumn(
                name: "attempt_count",
                table: "payouts");

            migrationBuilder.DropColumn(
                name: "next_attempt_at",
                table: "payouts");
        }
    }
}
