using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gabinety.Booking.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class T1201_ClientDisplayName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "client_display_name",
                table: "bookings",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "client_display_name",
                table: "bookings");
        }
    }
}
