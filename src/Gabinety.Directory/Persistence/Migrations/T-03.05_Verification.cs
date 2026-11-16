using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gabinety.Directory.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class T0305_Verification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "github_challenge_code",
                table: "practitioners",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "github_challenge_expires_at",
                table: "practitioners",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "github_challenge_login",
                table: "practitioners",
                type: "character varying(39)",
                maxLength: 39,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "moderators",
                columns: table => new
                {
                    user_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_moderators", x => x.user_id);
                });

            migrationBuilder.CreateTable(
                name: "verification_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    practitioner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    actor_user_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_verification_events", x => x.id);
                    table.ForeignKey(
                        name: "FK_verification_events_practitioners_practitioner_id",
                        column: x => x.practitioner_id,
                        principalTable: "practitioners",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_verification_events_practitioner_id_at",
                table: "verification_events",
                columns: new[] { "practitioner_id", "at" });

            // One linked practitioner per GitHub handle, whatever the case. EF cannot model an expression index, so it is SQL.
            migrationBuilder.Sql("""CREATE UNIQUE INDEX ux_practitioners_github_login_linked ON practitioners (lower(github_login)) WHERE github_linked;""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS ux_practitioners_github_login_linked");

            migrationBuilder.DropTable(
                name: "moderators");

            migrationBuilder.DropTable(
                name: "verification_events");

            migrationBuilder.DropColumn(
                name: "github_challenge_code",
                table: "practitioners");

            migrationBuilder.DropColumn(
                name: "github_challenge_expires_at",
                table: "practitioners");

            migrationBuilder.DropColumn(
                name: "github_challenge_login",
                table: "practitioners");
        }
    }
}
