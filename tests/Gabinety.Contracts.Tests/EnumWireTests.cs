using System.Text.Json;
using Gabinety.Contracts.Assistant;
using Gabinety.Contracts.Booking;
using Gabinety.Contracts.Directory;
using Gabinety.Contracts.Reputation;

namespace Gabinety.Contracts.Tests;

/// <summary>CONTRACTS 3.2, 4.2, 5.1 and 6 spell these values in lower case or snake_case. The literal strings are the contract.</summary>
public sealed class EnumWireTests
{
    static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    public static TheoryData<Type, object, string> Lower => new()
    {
        { typeof(Badge), Badge.Email, "email" }, { typeof(Badge), Badge.Github, "github" }, { typeof(Badge), Badge.Nip, "nip" }, { typeof(Badge), Badge.Approved, "approved" },
        { typeof(Confidence), Confidence.Low, "low" }, { typeof(Confidence), Confidence.Medium, "medium" }, { typeof(Confidence), Confidence.High, "high" },
        { typeof(TriageVerdict), TriageVerdict.None, "none" }, { typeof(TriageVerdict), TriageVerdict.Hold, "hold" },
        { typeof(TriageReason), TriageReason.Spam, "spam" }, { typeof(TriageReason), TriageReason.Abuse, "abuse" },
        { typeof(TriageReason), TriageReason.PersonalData, "personal_data" }, { typeof(TriageReason), TriageReason.OffTopic, "off_topic" },
        { typeof(TriageReason), TriageReason.Threat, "threat" },
        { typeof(TriageFlag), TriageFlag.None, "none" }, { typeof(TriageFlag), TriageFlag.Hold, "hold" }, { typeof(TriageFlag), TriageFlag.Unavailable, "unavailable" },
        { typeof(DisputeKind), DisputeKind.NotAsDescribed, "not_as_described" }, { typeof(DisputeKind), DisputeKind.PractitionerNoShow, "practitioner_no_show" },
        { typeof(DisputeKind), DisputeKind.Other, "other" },
        { typeof(Resolution), Resolution.Release, "release" }, { typeof(Resolution), Resolution.RefundFull, "refund_full" }, { typeof(Resolution), Resolution.RefundPartial, "refund_partial" },
    };

    // CONTRACTS 3.1, 4.1, 5.1 and 6 spell these in PascalCase.
    public static TheoryData<Type, object, string> Pascal => new()
    {
        { typeof(PractitionerStatus), PractitionerStatus.Suspended, "Suspended" },
        { typeof(BookingStatus), BookingStatus.LateCancelledByClient, "LateCancelledByClient" },
        { typeof(PaymentStatus), PaymentStatus.PartiallyRefunded, "PartiallyRefunded" },
        { typeof(LedgerKind), LedgerKind.PractitionerPayout, "PractitionerPayout" },
        { typeof(ReviewStatus), ReviewStatus.Held, "Held" },
        { typeof(AssistantState), AssistantState.NotAuthenticated, "NotAuthenticated" },
    };

    [Theory, MemberData(nameof(Lower))]
    public void Lower_case_values_have_the_contract_literal_and_round_trip(Type type, object value, string wire) => Check(type, value, wire);

    [Theory, MemberData(nameof(Pascal))]
    public void Pascal_case_values_keep_the_contract_literal_and_round_trip(Type type, object value, string wire) => Check(type, value, wire);

    static void Check(Type type, object value, string wire)
    {
        Assert.Equal($"\"{wire}\"", JsonSerializer.Serialize(value, type, Web));
        Assert.Equal(value, JsonSerializer.Deserialize($"\"{wire}\"", type, Web));
    }

    [Fact]
    public void Every_member_of_every_lower_case_enum_is_covered_by_the_table()
    {
        foreach (var type in Lower.Select(r => (Type)r[0]).Distinct())
        {
            var covered = Lower.Where(r => (Type)r[0] == type).Select(r => r[1]).ToHashSet();
            Assert.True(Enum.GetValues(type).Cast<object>().All(covered.Contains), $"{type.Name} has a member without a wire literal in this table");
        }
    }

    [Theory]
    [InlineData(typeof(Confidence), "1")]
    [InlineData(typeof(DisputeKind), "0")]
    [InlineData(typeof(Resolution), "2")]
    public void An_integer_is_not_a_wire_value(Type type, string json) =>
        Assert.ThrowsAny<JsonException>(() => JsonSerializer.Deserialize(json, type, Web));

    [Fact]
    public void A_dto_carries_the_wire_values()
    {
        Assert.Equal("""{"flag":"hold","reasons":["personal_data","off_topic"],"modelId":"m"}""",
            JsonSerializer.Serialize(new TriageResponse(TriageVerdict.Hold, [TriageReason.PersonalData, TriageReason.OffTopic], "m"), Web));
        Assert.Contains("\"confidence\":\"low\"", JsonSerializer.Serialize(
            new BriefSuggestion("p", [], "s", [], 30, Confidence.Low, "m", "v"), Web));
    }
}
