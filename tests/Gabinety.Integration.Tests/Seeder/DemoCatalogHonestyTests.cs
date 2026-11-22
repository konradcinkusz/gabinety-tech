using Gabinety.DemoSeeder;

namespace Gabinety.Integration.Tests.Seeder;

/// <summary>
/// The catalog's rating baseline (what the Directory shows before Reputation pushes anything) must equal the reviews the seeder really writes: a profile never shows
/// more reviews than exist. The reviews are read from <see cref="ReputationPlan"/>, each on a Completed booking of <see cref="BookingPlan"/>, made with the
/// n-th Approved practitioner (the Booking step orders them by key, which is the catalog order).
/// </summary>
public class DemoCatalogHonestyTests
{
    static readonly DemoPractitioner[] Approved = DemoCatalog.Practitioners.Where(p => p.Target == DemoStatus.Approved && p.Offerings.Length > 0).OrderBy(p => p.UserName, StringComparer.Ordinal).ToArray();

    static (int Count, int Sum) Seeded(DemoPractitioner p)
    {
        var mine = ReputationPlan.All.Where(r =>
        {
            var booking = BookingPlan.All.Single(b => b.Key == r.BookingKey);
            Assert.Equal(BookingTarget.Completed, booking.Target);
            return Approved[booking.Practitioner % Approved.Length].Number == p.Number;
        }).ToList();
        return (mine.Count, mine.Sum(r => r.Rating));
    }

    [Fact]
    public void Every_catalog_review_count_and_average_is_what_the_seeder_creates()
    {
        foreach (var p in DemoCatalog.Practitioners.Where(p => p.Stats is not null))
        {
            var (count, sum) = Seeded(p);
            Assert.True(p.Stats!.Reviews == count, $"{p.Slug}: the catalog says {p.Stats.Reviews} reviews, the seeder creates {count}");
            Assert.Equal(count == 0 ? 0m : (decimal)sum / count, p.Stats.Average);
        }
    }

    [Fact]
    public void Every_seeded_review_lands_on_an_approved_practitioner_so_the_check_above_sees_all_of_them()
    {
        var total = DemoCatalog.Practitioners.Where(p => p.Stats is not null).Sum(p => Seeded(p).Count);
        Assert.Equal(ReputationPlan.All.Length, total);
    }

    [Fact]
    public void The_suspended_practitioner_claims_no_reviews_because_the_seeder_makes_no_booking_for_them()
    {
        var suspended = DemoCatalog.Practitioners.Single(p => p.Target == DemoStatus.Suspended);
        Assert.Equal(0, suspended.Stats!.Reviews);
    }

    [Fact]
    public void Journey_3_starts_from_an_approved_practitioner_with_no_review()
    {
        Assert.Contains(Approved, p => p.Stats!.Reviews == 0 && Seeded(p).Count == 0);
        Assert.Equal(0, Approved.Single(p => p.Slug == "demo-celina-przykladowska").Stats!.Reviews);
    }
}
