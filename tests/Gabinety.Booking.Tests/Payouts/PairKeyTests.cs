using System.Security.Cryptography;
using System.Text;
using Gabinety.Billing;
using Gabinety.Booking.Features.Lifecycle.Core;
using Gabinety.Booking.Features.Payouts;
using BookingStatus = Gabinety.Billing.BookingStatus;
using BookingTrigger = Gabinety.Billing.BookingTrigger;

namespace Gabinety.Booking.Tests;

public class PairKeyTests
{
    static readonly Guid P = Guid.Parse("11111111-2222-3333-4444-555555555555");
    static string Long(char c = 'c') => "demo-" + new string(c, 59); // 64 characters

    [Fact]
    public void Ids_that_fit_give_exactly_the_spec_format()
    {
        var client = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee").ToString();
        Assert.Equal($"{client}:{P}", PairKey.For(client, P));
        Assert.Equal(73, PairKey.For(client, P).Length);
        var edge = new string('x', 80 - 37); // 43 + ':' + 36 = 80
        Assert.Equal($"{edge}:{P}", PairKey.For(edge, P));
        Assert.Equal(80, PairKey.For(edge, P).Length);
    }

    [Fact]
    public void A_64_character_client_id_gives_the_hashed_form_that_is_stable_and_fits()
    {
        var client = Long();
        Assert.Equal(64, client.Length);
        var key = PairKey.For(client, P);
        var expected = "h:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{client}:{P}"))).ToLowerInvariant();
        Assert.Equal(expected, key);
        Assert.Equal(66, key.Length);
        Assert.Equal(key, PairKey.For(client, P));
        Assert.Equal(key.ToLowerInvariant(), key);
        Assert.Equal(81, $"{new string('x', 44)}:{P}".Length); // one over the edge is hashed
        Assert.StartsWith("h:", PairKey.For(new string('x', 44), P));
    }

    [Fact]
    public void Different_long_pairs_get_different_keys()
    {
        Assert.NotEqual(PairKey.For(Long('a'), P), PairKey.For(Long('b'), P));
        Assert.NotEqual(PairKey.For(Long('a'), P), PairKey.For(Long('a'), Guid.NewGuid()));
    }

    [Fact]
    public async Task A_second_booking_of_a_long_id_pair_gets_fee_zero_and_the_first_the_fee()
    {
        await using var h = Life.Host(new RecordingProvider());
        var client = "demo-" + Guid.NewGuid().ToString("N") + new string('z', 27); // 64 characters
        var a = await Fee.Seed(h, BookingStatus.Confirmed, client, h.Practitioner, 25000, TimeSpan.FromDays(2));
        var b = await Fee.Seed(h, BookingStatus.Confirmed, client, h.Practitioner, 25000, TimeSpan.FromDays(3));
        await Fee.Apply(h, a, BookingTrigger.CompletionWindowElapsed);
        await Fee.Apply(h, b, BookingTrigger.CompletionWindowElapsed);
        Assert.Equal((3750L, 21250L), await Fee.Amounts(h, a));
        Assert.Equal((0L, 25000L), await Fee.Amounts(h, b));
        var rows = await h.Query(db => Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(
            db.LedgerEntries.Where(l => l.PairKey == PairKey.For(client, h.Practitioner))));
        Assert.Equal(a, Assert.Single(rows).BookingId);
    }

    [Fact]
    public async Task An_admin_resolve_release_with_a_long_client_id_is_200_and_takes_the_fee()
    {
        await using var h = Adm.Host(new RecordingProvider());
        var (id, _) = await Adm.Seed(h);
        var client = "demo-" + Guid.NewGuid().ToString("N") + new string('z', 27);
        await h.Seed(db => db.Bookings.Single(x => x.Id == id).ClientUserId = client);
        var r = await Adm.Resolve(h, id, Adm.Body("release"));
        Assert.Equal(System.Net.HttpStatusCode.OK, r.StatusCode);
        var row = await Life.Row(h, id);
        Assert.Equal((BookingStatus.Resolved, 1500L, 8500L), (row.Status, row.FeeMinor, row.PayoutMinor));
    }
}
