using Gabinety.Booking.Features.Payouts;
using Gabinety.Booking.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using BookingStatus = Gabinety.Billing.BookingStatus;

namespace Gabinety.Booking.Tests;

/// <summary>The earnings queries on real PostgreSQL 17 (T-11.15): the left join to payouts, the status and enum conversions, and timestamptz bounds, which the in-memory provider only approximates.</summary>
[Trait("tier", "integration")]
public class EarningsIntegrationTests : IAsyncLifetime
{
    PostgreSqlContainer pg = null!;
    readonly Guid other = Guid.NewGuid();
    CreateHost host = null!;

    public async Task InitializeAsync()
    {
        pg = new PostgreSqlBuilder("postgres:17-alpine").Build();
        await pg.StartAsync();
        host = Earn.Host(other, h =>
        {
            h.Settings["Database:Provider"] = "PostgreSQL";
            h.Settings["ConnectionStrings:bookingdb"] = pg.GetConnectionString();
        });
        host.CreateClient();
        await host.Services.GetRequiredService<MigrationState>().WaitForCompletionAsync(TimeSpan.FromSeconds(60));
    }

    public async Task DisposeAsync() { await host.DisposeAsync(); await pg.DisposeAsync(); }

    [Fact]
    public async Task The_scenario_sums_on_PostgreSQL_are_the_hand_sums_and_another_practitioners_rows_do_not_count()
    {
        await Earn.SeedScenario(host, host.Practitioner);
        // Noise of another practitioner in the same month and a Failed payout and a refunded booking of this one.
        await Earn.SeedScenario(host, other);
        var failed = await Earn.Booking(host, host.Practitioner, BookingStatus.Completed, 10000, "2026-11-09T10:00:00Z", payoutMinor: 8500);
        await Earn.Payout(host, failed, host.Practitioner, PayoutStatus.Failed, 8500);
        await Earn.Booking(host, host.Practitioner, BookingStatus.CancelledByClient, 25000, "2026-11-10T10:00:00Z");

        var b = await Earn.Get(host.ClientFor(host.OwnerUserId));

        Assert.Equal((21250L, 5000L, 12000L, 3750L, 3), Earn.Figures(b));
    }

    [Fact]
    public async Task A_period_that_excludes_every_row_is_zeros_and_the_half_open_bounds_hold_on_timestamptz()
    {
        await Earn.SeedScenario(host, host.Practitioner);
        var id = await Earn.Booking(host, host.Practitioner, BookingStatus.Completed, 25000, "2026-12-02T10:00:00Z");
        await Earn.Fee(host, id, 1, "2026-11-30T22:59:59.9999990Z"); // last microsecond of November in Warsaw
        await Earn.Fee(host, id, 10, "2026-11-30T23:00:00Z");        // first of December

        Assert.Equal((0L, 0L, 0L, 0L, 0), Earn.Figures(await Earn.Get(host.ClientFor(host.OwnerUserId), "?from=2026-01-01&to=2026-01-31")));
        Assert.Equal(3751L, Earn.Figures(await Earn.Get(host.ClientFor(host.OwnerUserId), "?from=2026-11-01&to=2026-11-30")).Fees);
        Assert.Equal(10L, Earn.Figures(await Earn.Get(host.ClientFor(host.OwnerUserId), "?from=2026-12-01&to=2026-12-31")).Fees);
    }
}
