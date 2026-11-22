using Gabinety.Contracts.Directory;
using Gabinety.Directory.Features.ContractAvailability;
using Gabinety.Directory.Features.Profiles;

namespace Gabinety.Directory.Tests.ContractAvailability;

/// <summary>The pure rules of contract availability (ADR proposal 0013, T-13.01): validation, the stored shape and the public shape.</summary>
public class ContractAvailabilityRulesTests
{
    static readonly DateOnly Today = new(2026, 11, 22);

    static ContractAvailabilitySettings S(ContractStatus status, DateOnly? from = null, int? hours = 20, long? rate = null) => new(status, from, hours, rate);

    [Fact]
    public void Not_available_needs_nothing_and_ignores_the_rest() =>
        Assert.Empty(ContractAvailabilityRules.Validate(S(ContractStatus.NotAvailable, null, null), Today));

    [Fact]
    public void Available_now_needs_only_hours() =>
        Assert.Empty(ContractAvailabilityRules.Validate(S(ContractStatus.AvailableNow), Today));

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(61)]
    [InlineData(-3)]
    public void Hours_per_week_is_required_and_between_1_and_60(int? hours)
    {
        var e = ContractAvailabilityRules.Validate(S(ContractStatus.AvailableNow, null, hours), Today);
        Assert.Contains("hoursPerWeek", e.Keys);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(40)]
    [InlineData(60)]
    public void Hours_on_the_boundaries_are_accepted(int hours) =>
        Assert.Empty(ContractAvailabilityRules.Validate(S(ContractStatus.AvailableNow, null, hours), Today));

    [Fact]
    public void Available_from_needs_a_date_after_today()
    {
        Assert.Contains("availableFrom", ContractAvailabilityRules.Validate(S(ContractStatus.AvailableFrom, null), Today).Keys);
        Assert.Contains("availableFrom", ContractAvailabilityRules.Validate(S(ContractStatus.AvailableFrom, Today), Today).Keys);
        Assert.Contains("availableFrom", ContractAvailabilityRules.Validate(S(ContractStatus.AvailableFrom, Today.AddDays(-1)), Today).Keys);
        Assert.Empty(ContractAvailabilityRules.Validate(S(ContractStatus.AvailableFrom, Today.AddDays(1)), Today));
    }

    [Fact]
    public void Available_from_is_at_most_24_months_ahead()
    {
        Assert.Empty(ContractAvailabilityRules.Validate(S(ContractStatus.AvailableFrom, Today.AddMonths(24)), Today));
        Assert.Contains("availableFrom", ContractAvailabilityRules.Validate(S(ContractStatus.AvailableFrom, Today.AddMonths(24).AddDays(1)), Today).Keys);
    }

    [Theory]
    [InlineData(1_999L, false)]
    [InlineData(2_000L, true)]
    [InlineData(110_00L, true)]
    [InlineData(100_000L, true)]
    [InlineData(100_001L, false)]
    public void The_indicative_rate_is_optional_but_bounded(long rate, bool ok)
    {
        var e = ContractAvailabilityRules.Validate(S(ContractStatus.AvailableNow, null, 10, rate), Today);
        Assert.Equal(ok, e.Count == 0);
        if (!ok) Assert.Contains("rateMinor", e.Keys);
    }

    [Fact]
    public void An_undefined_status_is_refused() =>
        Assert.Contains("status", ContractAvailabilityRules.Validate(S((ContractStatus)99), Today).Keys);

    [Fact]
    public void Switching_off_clears_hours_rate_and_date()
    {
        var p = new Practitioner();
        ContractAvailabilityRules.Apply(p, S(ContractStatus.AvailableFrom, Today.AddDays(30), 12, 15_000));
        Assert.Equal((short)12, p.ContractHoursPerWeek);
        ContractAvailabilityRules.Apply(p, S(ContractStatus.NotAvailable, Today.AddDays(30), 12, 15_000));
        Assert.Equal(ContractStatus.NotAvailable, p.ContractStatus);
        Assert.Null(p.ContractAvailableFrom); Assert.Null(p.ContractHoursPerWeek); Assert.Null(p.ContractRateMinor);
    }

    [Fact]
    public void Available_now_does_not_store_a_date()
    {
        var p = new Practitioner();
        ContractAvailabilityRules.Apply(p, S(ContractStatus.AvailableNow, Today.AddDays(30), 12));
        Assert.Null(p.ContractAvailableFrom);
    }

    [Fact]
    public void The_public_shape_is_absent_when_not_available_and_a_past_date_reads_as_now()
    {
        Assert.Null(ContractAvailabilityRules.Public(new Practitioner(), Today));

        var future = new Practitioner { ContractStatus = ContractStatus.AvailableFrom, ContractAvailableFrom = Today.AddDays(90), ContractHoursPerWeek = 20, ContractRateMinor = 11_000 };
        var f = ContractAvailabilityRules.Public(future, Today)!;
        Assert.Equal(Today.AddDays(90), f.AvailableFrom);
        Assert.Equal(20, f.HoursPerWeek);
        Assert.Equal(11_000, f.Rate!.AmountMinor);

        var passed = new Practitioner { ContractStatus = ContractStatus.AvailableFrom, ContractAvailableFrom = Today.AddDays(-1), ContractHoursPerWeek = 20 };
        Assert.Null(ContractAvailabilityRules.Public(passed, Today)!.AvailableFrom);
        Assert.Null(ContractAvailabilityRules.Public(passed, Today)!.Rate);
    }

    [Theory]
    [InlineData(PractitionerStatus.Approved, ContractStatus.AvailableNow, true)]
    [InlineData(PractitionerStatus.Approved, ContractStatus.AvailableFrom, true)]
    [InlineData(PractitionerStatus.Approved, ContractStatus.NotAvailable, false)]
    [InlineData(PractitionerStatus.Draft, ContractStatus.AvailableNow, false)]
    [InlineData(PractitionerStatus.Suspended, ContractStatus.AvailableNow, false)]
    public void Only_an_approved_practitioner_who_advertises_accepts_proposals(PractitionerStatus status, ContractStatus contract, bool expected) =>
        Assert.Equal(expected, ContractAvailabilityRules.Accepts(new Practitioner { Status = status, ContractStatus = contract }));
}
