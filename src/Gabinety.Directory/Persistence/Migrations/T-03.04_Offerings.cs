using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gabinety.Directory.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class T0304_Offerings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "offerings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    practitioner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    description = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: false),
                    duration_minutes = table.Column<short>(type: "smallint", nullable: false),
                    price_minor = table.Column<long>(type: "bigint", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    problem_slug = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    tech_slugs = table.Column<string[]>(type: "text[]", nullable: false),
                    meeting_url = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_offerings", x => x.id);
                    table.ForeignKey(
                        name: "FK_offerings_categories_problem_slug",
                        column: x => x.problem_slug,
                        principalTable: "categories",
                        principalColumn: "slug",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_offerings_practitioners_practitioner_id",
                        column: x => x.practitioner_id,
                        principalTable: "practitioners",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_offerings_practitioner_id",
                table: "offerings",
                column: "practitioner_id");

            migrationBuilder.CreateIndex(
                name: "IX_offerings_problem_slug_is_active",
                table: "offerings",
                columns: new[] { "problem_slug", "is_active" });

            migrationBuilder.CreateIndex(
                name: "IX_offerings_tech_slugs",
                table: "offerings",
                column: "tech_slugs")
                .Annotation("Npgsql:IndexMethod", "gin");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "offerings");
        }
    }
}
