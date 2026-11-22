using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gabinety.Booking.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Proposals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_notifications_booking_id_kind_to_user_id",
                table: "notifications");

            migrationBuilder.AlterColumn<Guid>(
                name: "booking_id",
                table: "notifications",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "proposal_id",
                table: "notifications",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "round",
                table: "notifications",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "proposals",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_user_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    client_display_name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    client_email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    company_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    practitioner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    practitioner_user_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    round_count = table.Column<int>(type: "integer", nullable: false),
                    rate_minor = table.Column<long>(type: "bigint", nullable: false),
                    hours_per_week = table.Column<int>(type: "integer", nullable: true),
                    total_hours = table.Column<int>(type: "integer", nullable: true),
                    duration_months = table.Column<int>(type: "integer", nullable: true),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    idempotency_key = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_action_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_proposals", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "proposal_rounds",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    proposal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<int>(type: "integer", nullable: false),
                    by = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    action = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    rate_minor = table.Column<long>(type: "bigint", nullable: true),
                    hours_per_week = table.Column<int>(type: "integer", nullable: true),
                    total_hours = table.Column<int>(type: "integer", nullable: true),
                    duration_months = table.Column<int>(type: "integer", nullable: true),
                    start_date = table.Column<DateOnly>(type: "date", nullable: true),
                    text = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_proposal_rounds", x => x.id);
                    table.ForeignKey(
                        name: "FK_proposal_rounds_proposals_proposal_id",
                        column: x => x.proposal_id,
                        principalTable: "proposals",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_notifications_booking_id_kind_to_user_id",
                table: "notifications",
                columns: new[] { "booking_id", "kind", "to_user_id" },
                unique: true,
                filter: "booking_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_notifications_proposal_id_kind_to_user_id_round",
                table: "notifications",
                columns: new[] { "proposal_id", "kind", "to_user_id", "round" },
                unique: true,
                filter: "proposal_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_proposal_rounds_proposal_id_number",
                table: "proposal_rounds",
                columns: new[] { "proposal_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_proposals_client_user_id_created_at_id",
                table: "proposals",
                columns: new[] { "client_user_id", "created_at", "id" });

            migrationBuilder.CreateIndex(
                name: "IX_proposals_client_user_id_idempotency_key",
                table: "proposals",
                columns: new[] { "client_user_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_proposals_practitioner_user_id_created_at_id",
                table: "proposals",
                columns: new[] { "practitioner_user_id", "created_at", "id" });

            migrationBuilder.CreateIndex(
                name: "IX_proposals_status_expires_at",
                table: "proposals",
                columns: new[] { "status", "expires_at" });

            migrationBuilder.CreateIndex(
                name: "ux_proposals_open_pair",
                table: "proposals",
                columns: new[] { "client_user_id", "practitioner_id" },
                unique: true,
                filter: "status IN ('AwaitingPractitioner','AwaitingClient')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "proposal_rounds");

            migrationBuilder.DropTable(
                name: "proposals");

            migrationBuilder.DropIndex(
                name: "IX_notifications_booking_id_kind_to_user_id",
                table: "notifications");

            migrationBuilder.DropIndex(
                name: "IX_notifications_proposal_id_kind_to_user_id_round",
                table: "notifications");

            migrationBuilder.DropColumn(
                name: "proposal_id",
                table: "notifications");

            migrationBuilder.DropColumn(
                name: "round",
                table: "notifications");

            migrationBuilder.AlterColumn<Guid>(
                name: "booking_id",
                table: "notifications",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_notifications_booking_id_kind_to_user_id",
                table: "notifications",
                columns: new[] { "booking_id", "kind", "to_user_id" },
                unique: true);
        }
    }
}
