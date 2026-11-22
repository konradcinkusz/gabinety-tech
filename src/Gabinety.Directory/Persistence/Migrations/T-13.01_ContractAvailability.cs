using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gabinety.Directory.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ContractAvailability : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "contract_available_from",
                table: "practitioners",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "contract_hours_per_week",
                table: "practitioners",
                type: "smallint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "contract_rate_minor",
                table: "practitioners",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "contract_status",
                table: "practitioners",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "NotAvailable");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "contract_available_from",
                table: "practitioners");

            migrationBuilder.DropColumn(
                name: "contract_hours_per_week",
                table: "practitioners");

            migrationBuilder.DropColumn(
                name: "contract_rate_minor",
                table: "practitioners");

            migrationBuilder.DropColumn(
                name: "contract_status",
                table: "practitioners");
        }
    }
}
