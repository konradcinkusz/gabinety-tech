using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gabinety.Directory.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ContactEmail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "contact_email",
                table: "practitioners",
                type: "character varying(254)",
                maxLength: 254,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "contact_email",
                table: "practitioners");
        }
    }
}
