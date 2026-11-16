using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gabinety.Reputation.Persistence.Migrations
{
    /// <summary>Single first migration: folds the empty baseline and the three tables of CONTRACTS 5.1 (reviews, rating_summaries, moderation_events).</summary>
    [DbContext(typeof(ReputationDbContext))]
    [Migration("20261116100000_T-05.01_Entities")]
    public sealed class T0501_Entities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "moderation_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    review_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    actor = table.Column<string>(type: "text", nullable: false),
                    note = table.Column<string>(type: "text", nullable: true),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_moderation_events", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "rating_summaries",
                columns: table => new
                {
                    practitioner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    count = table.Column<int>(type: "integer", nullable: false),
                    sum = table.Column<int>(type: "integer", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    pushed_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rating_summaries", x => x.practitioner_id);
                });

            migrationBuilder.CreateTable(
                name: "reviews",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    booking_id = table.Column<Guid>(type: "uuid", nullable: false),
                    practitioner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_user_id = table.Column<string>(type: "text", nullable: false),
                    rating = table.Column<short>(type: "smallint", nullable: false),
                    text = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    hold_reasons = table.Column<List<string>>(type: "text[]", nullable: false),
                    triage_flag = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    triage_model_id = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    moderated_by = table.Column<string>(type: "text", nullable: true),
                    moderation_note = table.Column<string>(type: "text", nullable: true),
                    reply_text = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    replied_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reviews", x => x.id);
                    table.CheckConstraint("ck_reviews_rating", "rating between 1 and 5");
                });

            migrationBuilder.CreateIndex(
                name: "ix_moderation_events_review_id",
                table: "moderation_events",
                column: "review_id");

            migrationBuilder.CreateIndex(
                name: "ix_reviews_practitioner_status_published",
                table: "reviews",
                columns: new[] { "practitioner_id", "status", "published_at", "id" },
                descending: new[] { false, false, true, false });

            migrationBuilder.CreateIndex(
                name: "ux_reviews_booking_id",
                table: "reviews",
                column: "booking_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "moderation_events");

            migrationBuilder.DropTable(
                name: "rating_summaries");

            migrationBuilder.DropTable(
                name: "reviews");
        }
    }
}
