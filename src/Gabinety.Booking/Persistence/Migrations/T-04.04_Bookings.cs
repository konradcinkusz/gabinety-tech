using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gabinety.Booking.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Bookings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "bookings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_user_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    practitioner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    practitioner_user_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    offering_id = table.Column<Guid>(type: "uuid", nullable: false),
                    offering_title = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    duration_minutes = table.Column<short>(type: "smallint", nullable: false),
                    price_minor = table.Column<long>(type: "bigint", nullable: false),
                    start_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    end_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    status_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    brief_text = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    brief_problem_slug = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    brief_ai_assisted = table.Column<bool>(type: "boolean", nullable: false),
                    meeting_url_snapshot = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    hold_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    fee_minor = table.Column<long>(type: "bigint", nullable: true),
                    payout_minor = table.Column<long>(type: "bigint", nullable: true),
                    payout_due_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    idempotency_key = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    confirmed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ended_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    dispute_opened_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    dispute_resolution = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bookings", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_bookings_client_user_id_idempotency_key",
                table: "bookings",
                columns: new[] { "client_user_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_bookings_practitioner_id_start_utc",
                table: "bookings",
                columns: new[] { "practitioner_id", "start_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_bookings_status_hold_expires_at",
                table: "bookings",
                columns: new[] { "status", "hold_expires_at" });

            // PostgreSQL only (the InMemory provider cannot enforce it, so a tier=integration test proves it). CONTRACTS 4.1: no double booking.
            // The UTC wall-clock form is required: timestamptz + interval is STABLE and PostgreSQL rejects it in an index expression (42P17).
            // The 15 minute buffer is therefore a constant here, the same as CONTRACTS 9 BufferMinutes. A PendingPayment row with an expired hold
            // still blocks until it is set to Expired (the constraint cannot use now()): see BookingSlotGuard.ExpireStaleHoldsAsync.
            if (migrationBuilder.ActiveProvider.Contains("Npgsql", StringComparison.Ordinal))
            {
                migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS btree_gist;");
                migrationBuilder.Sql("""
                    ALTER TABLE bookings ADD CONSTRAINT bookings_no_overlap
                    EXCLUDE USING gist (
                        practitioner_id WITH =,
                        tsrange(start_utc AT TIME ZONE 'UTC', (end_utc AT TIME ZONE 'UTC') + interval '15 minutes') WITH &&)
                    WHERE (status IN ('PendingPayment','Confirmed','Disputed'));
                    """);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bookings");
            // btree_gist stays installed: an extension may be shared with other schemas.
        }
    }
}
