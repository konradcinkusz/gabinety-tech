using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Gabinety.Contracts.Common;

/// <summary>Instants on the wire are ISO 8601 UTC with a trailing Z, e.g. 2026-12-01T09:30:00Z (CONTRACTS 0).</summary>
public sealed class UtcInstantConverter : JsonConverter<DateTimeOffset>
{
    public override DateTimeOffset Read(ref Utf8JsonReader r, Type t, JsonSerializerOptions o) =>
        DateTimeOffset.Parse(r.GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal).ToUniversalTime();

    public override void Write(Utf8JsonWriter w, DateTimeOffset v, JsonSerializerOptions o) =>
        w.WriteStringValue(v.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'", CultureInfo.InvariantCulture));
}

public sealed class UtcInstantNullableConverter : JsonConverter<DateTimeOffset?>
{
    private static readonly UtcInstantConverter Inner = new();

    public override DateTimeOffset? Read(ref Utf8JsonReader r, Type t, JsonSerializerOptions o) =>
        r.TokenType == JsonTokenType.Null ? null : Inner.Read(ref r, t, o);

    public override void Write(Utf8JsonWriter w, DateTimeOffset? v, JsonSerializerOptions o)
    {
        if (v is null) w.WriteNullValue(); else Inner.Write(w, v.Value, o);
    }
}
