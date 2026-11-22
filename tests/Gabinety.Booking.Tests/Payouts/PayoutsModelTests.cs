using Gabinety.Booking.Features.Payouts;
using Gabinety.Booking.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Gabinety.Booking.Tests;

public class PayoutsModelTests
{
    [Fact]
    public void Payouts_maps_the_columns_of_CONTRACTS_4_1_with_a_unique_booking_id_and_a_string_status()
    {
        using var db = new BookingDbContextFactory().CreateDbContext([]);
        var e = db.Model.FindEntityType(typeof(Payout))!;
        Assert.Equal("payouts", e.GetTableName());
        Assert.Equal(new[] { "amount_minor", "attempt_count", "booking_id", "created_at", "id", "next_attempt_at", "practitioner_id", "provider_ref", "sent_at", "status" },
            e.GetProperties().Select(p => p.GetColumnName()).Order().ToArray());
        Assert.Contains(e.GetIndexes(), i => i.IsUnique && i.Properties.Select(p => p.Name).SequenceEqual(["BookingId"]));
        Assert.Equal(typeof(string), e.FindProperty(nameof(Payout.Status))!.GetProviderClrType());
        Assert.True(e.FindProperty(nameof(Payout.ProviderRef))!.IsNullable);
        Assert.True(e.FindProperty(nameof(Payout.SentAt))!.IsNullable);
    }
}
