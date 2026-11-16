using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Gabinety.Contracts.Common;
using Microsoft.Net.Http.Headers;

namespace Gabinety.Directory.Features.Search;

/// <summary>Wire format and cache headers of the public reads: camelCase, nulls omitted, enums as lower camel case (`email`, not `Email`).</summary>
public static class PublicJson
{
    public static readonly JsonSerializerOptions Options = Build();

    static JsonSerializerOptions Build()
    {
        var o = new JsonSerializerOptions(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
        o.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)); // an options converter wins over the [JsonConverter] attribute on the enum
        return o;
    }

    public static IResult Respond(HttpContext ctx, object body, string cacheControl, bool etag = false)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(body, body.GetType(), Options);
        ctx.Response.Headers.CacheControl = cacheControl;
        if (etag)
        {
            var tag = "\"" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() + "\"";
            ctx.Response.Headers.ETag = tag;
            if (Matches(ctx.Request.Headers.IfNoneMatch.ToString(), tag)) return Results.StatusCode(StatusCodes.Status304NotModified);
        }
        return Results.Bytes(bytes, "application/json");
    }

    static bool Matches(string header, string tag) =>
        header.Length > 0 && header.Split(',').Select(t => t.Trim()).Any(t => t == "*" || (t.StartsWith("W/", StringComparison.Ordinal) ? t[2..] : t) == tag);

    public static IResult NotFound(HttpContext ctx) => Problem(ctx, 404, ErrorCodes.NotFound, "Not found");

    public static IResult Invalid(HttpContext ctx, string field, string message) =>
        Problem(ctx, 400, ErrorCodes.ValidationFailed, "Validation failed", new Dictionary<string, string[]> { [field] = [message] });

    static IResult Problem(HttpContext ctx, int status, string code, string title, Dictionary<string, string[]>? errors = null)
    {
        ctx.Response.Headers.CacheControl = "no-store";
        return Results.Json(ApiProblem.Of(code, title, status, null, errors), Options, "application/problem+json", status);
    }
}
