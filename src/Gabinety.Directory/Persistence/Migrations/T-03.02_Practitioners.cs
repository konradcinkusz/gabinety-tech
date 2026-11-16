using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gabinety.Directory.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class T0302_Practitioners : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "practitioners",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    slug = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    display_name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    headline = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    bio = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    github_login = table.Column<string>(type: "character varying(39)", maxLength: 39, nullable: true),
                    github_linked = table.Column<bool>(type: "boolean", nullable: false),
                    nip = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    nip_valid = table.Column<bool>(type: "boolean", nullable: false),
                    portfolio_url = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    meeting_url_default = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    rating_count = table.Column<int>(type: "integer", nullable: false),
                    rating_avg = table.Column<decimal>(type: "numeric(3,2)", precision: 3, scale: 2, nullable: false),
                    reputation_version = table.Column<long>(type: "bigint", nullable: false),
                    completed_count = table.Column<int>(type: "integer", nullable: false),
                    practitioner_cancel_count = table.Column<int>(type: "integer", nullable: false),
                    no_show_upheld_count = table.Column<int>(type: "integer", nullable: false),
                    open_slots_14d = table.Column<int>(type: "integer", nullable: false),
                    stats_version = table.Column<long>(type: "bigint", nullable: false),
                    profile_completeness = table.Column<decimal>(type: "numeric(3,2)", precision: 3, scale: 2, nullable: false),
                    ranking_score = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    ranking_version = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_practitioners", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_practitioners_slug",
                table: "practitioners",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_practitioners_status_ranking_score_approved_at_id",
                table: "practitioners",
                columns: new[] { "status", "ranking_score", "approved_at", "id" },
                descending: new[] { false, true, false, false });

            migrationBuilder.CreateIndex(
                name: "IX_practitioners_user_id",
                table: "practitioners",
                column: "user_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "practitioners");
        }
    }
}
