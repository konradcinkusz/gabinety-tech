using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gabinety.Assistant.Persistence.Migrations
{
    /// <summary>Single first migration: folds the empty baseline and assistant_usage (CONTRACTS 6) into one.</summary>
    [DbContext(typeof(AssistantDbContext))]
    [Migration("20261116130000_T-06.01_Usage")]
    public sealed class T0601_Usage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "assistant_usage",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    feature = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    model_id = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    provider_kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    input_tokens = table.Column<int>(type: "integer", nullable: true),
                    output_tokens = table.Column<int>(type: "integer", nullable: true),
                    ai_credits = table.Column<decimal>(type: "numeric", nullable: true),
                    usd_estimate = table.Column<decimal>(type: "numeric", nullable: true),
                    latency_ms = table.Column<int>(type: "integer", nullable: false),
                    outcome = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    user_hash = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_assistant_usage", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_assistant_usage_at",
                table: "assistant_usage",
                column: "at");

            migrationBuilder.CreateIndex(
                name: "ix_assistant_usage_feature_at",
                table: "assistant_usage",
                columns: new[] { "feature", "at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "assistant_usage");
        }
    }
}
