using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gabinety.Booking.Persistence.Migrations;

/// <summary>Empty baseline (T-04.03): creates only __EFMigrationsHistory. The availability tables follow in T-04.03_Availability.</summary>
[DbContext(typeof(BookingDbContext))]
[Migration("20261116000000_T-04.03_Baseline")]
public sealed class Baseline : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) { }
    protected override void Down(MigrationBuilder migrationBuilder) { }
}
