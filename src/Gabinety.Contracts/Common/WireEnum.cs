using System.Text.Json;
using System.Text.Json.Serialization;

namespace Gabinety.Contracts.Common;

/// <summary>
/// Enum wire format for the values CONTRACTS spells in lower case (<c>low</c>, <c>none</c>, <c>personal_data</c>, <c>refund_full</c>):
/// a string, snake_case lower, integers refused. Enums that CONTRACTS spells in PascalCase (statuses, ledger kinds) keep the plain
/// <see cref="JsonStringEnumConverter"/> and are not listed here.
/// </summary>
public sealed class SnakeCaseLowerEnumConverter : JsonStringEnumConverter
{
    public SnakeCaseLowerEnumConverter() : base(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false) { }
}
