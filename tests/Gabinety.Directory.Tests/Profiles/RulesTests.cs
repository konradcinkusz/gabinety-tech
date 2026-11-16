using Gabinety.Contracts.Directory;
using Gabinety.Directory.Features.Profiles;

namespace Gabinety.Directory.Tests.Profiles;

public class NipTests
{
    [Theory]
    [InlineData("5260250995", true)]
    [InlineData("5260250996", false)]
    [InlineData("526025099", false)]
    [InlineData("52602509955", false)]
    [InlineData("52602A0995", false)]
    [InlineData("", false)]
    public void Checksum_vectors(string nip, bool valid) => Assert.Equal(valid, new NipChecksumValidator().IsValid(nip));
}

public class ContactDetectorTests
{
    readonly ContactDetailDetector _d = new();

    [Theory]
    [InlineData("pisz na jan.kowalski@example.test")]
    [InlineData("dzwon 600 100 200")]
    [InlineData("tel +48 600-100-200")]
    [InlineData("600100200")]
    [InlineData("znajdz mnie @jan_dev")]
    [InlineData("2015-2023 oraz dzwon 600 100 200")] // a year range does not hide a real number
    [InlineData("w 2020 2021 dzwon 600-100-200")]
    public void Rejects(string text) => Assert.True(_d.ContainsContactDetails(text));

    [Theory]
    [InlineData("dotnet 8 i EF Core 9")]
    [InlineData(".NET 8.0.1 oraz C# 13")]
    [InlineData("10 lat doswiadczenia, PostgreSQL 17.2")]
    [InlineData("Backend 2015-2023, potem 2019 - obecnie")]
    [InlineData("projekty 2018–2021 oraz 2020 2021")]
    [InlineData("")]
    public void Accepts(string text) => Assert.False(_d.ContainsContactDetails(text));
}

public class SlugTests
{
    [Theory]
    [InlineData("Zażółć Gęślą Jaźń", "zazolc-gesla-jazn")]
    [InlineData("  Jan   Kowalski!! ", "jan-kowalski")]
    [InlineData("a", "programista")]
    [InlineData("!!", "programista")]
    public void Generates(string name, string slug) => Assert.Equal(slug, SlugGenerator.FromName(name));

    [Theory]
    [InlineData("jan-kowalski", true)]
    [InlineData("ab", false)]
    [InlineData("Jan", false)]
    [InlineData("jan--k", false)]
    [InlineData("-jan", false)]
    public void Validates(string slug, bool ok) => Assert.Equal(ok, SlugGenerator.IsValid(slug));

    [Fact]
    public void Suffix_keeps_within_60_chars() => Assert.Equal(60, SlugGenerator.WithSuffix(new string('a', 60), 12).Length);
}

public class CompletenessTests
{
    [Fact] public void All_false_is_zero() => Assert.Equal(0.00m, ProfileCompleteness.Compute(false, false, false, false, false, false, false));
    [Fact] public void All_true_is_one() => Assert.Equal(1.00m, ProfileCompleteness.Compute(true, true, true, true, true, true, true));
    [Fact] public void Mixed_headline_bio_nip() => Assert.Equal(0.45m, ProfileCompleteness.Compute(true, true, false, false, false, true, false));
    [Fact] public void Mixed_offering_github_slots() => Assert.Equal(0.45m, ProfileCompleteness.Compute(false, false, true, true, false, false, true));
}

public class StateMachineTests
{
    static readonly HashSet<(PractitionerStatus, PractitionerTrigger, PractitionerStatus)> Legal =
    [
        (PractitionerStatus.Draft, PractitionerTrigger.Submit, PractitionerStatus.Submitted),
        (PractitionerStatus.Rejected, PractitionerTrigger.Submit, PractitionerStatus.Submitted),
        (PractitionerStatus.Submitted, PractitionerTrigger.Approve, PractitionerStatus.Approved),
        (PractitionerStatus.Submitted, PractitionerTrigger.Reject, PractitionerStatus.Rejected),
        (PractitionerStatus.Approved, PractitionerTrigger.Suspend, PractitionerStatus.Suspended),
        (PractitionerStatus.Suspended, PractitionerTrigger.Reinstate, PractitionerStatus.Approved),
        (PractitionerStatus.Approved, PractitionerTrigger.NipChanged, PractitionerStatus.Submitted),
    ];

    [Fact]
    public void Every_status_times_trigger_is_legal_exactly_when_in_the_table()
    {
        foreach (var s in Enum.GetValues<PractitionerStatus>())
            foreach (var t in Enum.GetValues<PractitionerTrigger>())
            {
                var ok = PractitionerStateMachine.TryTransition(s, t, out var to);
                var expected = Legal.SingleOrDefault(l => l.Item1 == s && l.Item2 == t);
                Assert.Equal(expected != default, ok);
                if (ok) Assert.Equal(expected.Item3, to);
            }
    }

    [Fact] public void Only_the_seven_pairs_exist() =>
        Assert.Equal(Legal.Count, Enum.GetValues<PractitionerStatus>().SelectMany(s => Enum.GetValues<PractitionerTrigger>()
            .Where(t => PractitionerStateMachine.TryTransition(s, t, out _))).Count());
}
