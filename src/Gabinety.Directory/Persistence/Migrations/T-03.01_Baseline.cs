using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gabinety.Directory.Persistence.Migrations;

/// <summary>Empty baseline (T-03.01): creates only __EFMigrationsHistory. Tables arrive with T-03.02 onward.</summary>
[DbContext(typeof(DirectoryDbContext))]
[Migration("20261116000000_T-03.01_Baseline")]
public sealed class Baseline : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) { }
    protected override void Down(MigrationBuilder migrationBuilder) { }
}
