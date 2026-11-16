using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gabinety.Booking.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Availability : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "availability_exceptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    practitioner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    date_local = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_availability_exceptions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "availability_rules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    practitioner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    iso_weekday = table.Column<short>(type: "smallint", nullable: false),
                    start_local = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    end_local = table.Column<TimeOnly>(type: "time without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_availability_rules", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_availability_exceptions_practitioner_id_date_local",
                table: "availability_exceptions",
                columns: new[] { "practitioner_id", "date_local" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_availability_rules_practitioner_id_iso_weekday",
                table: "availability_rules",
                columns: new[] { "practitioner_id", "iso_weekday" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "availability_exceptions");

            migrationBuilder.DropTable(
                name: "availability_rules");
        }
    }
}
