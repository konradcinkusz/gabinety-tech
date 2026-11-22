using System.Security.Claims;
using Gabinety.Booking.Clients;
using Gabinety.Booking.Features.Internal.TestClock;
using Gabinety.Contracts.Booking;
using Gabinety.Booking.Payments.Abstractions;
using Gabinety.Booking.Persistence;
using Gabinety.Contracts.Common;
using Gabinety.ServiceDefaults;
using Microsoft.EntityFrameworkCore;
using ProviderEvent = Gabinety.Booking.Payments.Abstractions.WebhookEvent;

namespace Gabinety.Booking.Features.Payments;

/// <summary>
/// POST /webhooks/payments/{provider} and POST /api/v1/payments/simulate (CONTRACTS 4.2, 4.3). Both end in <see cref="PaymentEventHandler"/>.
/// The webhook is public: the signature over the RAW body is the only authentication. The brief, the meeting URL and the signature never reach a log.
/// </summary>
public sealed class PaymentWebhookFeature : IServiceFeature
{
    public const string SignatureHeader = "X-Payment-Signature";
    public const int MaxBodyBytes = 64 * 1024;
    /// <summary>Stable id of the Error log line an alert on a signature failure burst is wired to (SPEC 8).</summary>
    public const int SignatureFailedEventId = 4301;
    /// <summary>Logged in place of the provider event id when none can be read safely from the unverified body. It cannot match <see cref="SafeEventId"/>.</summary>
    public const string NoEventIdMarker = "<none>";
    static readonly System.Text.RegularExpressions.Regex SafeEventId = new(@"\A[A-Za-z0-9_-]{1,64}\z", System.Text.RegularExpressions.RegexOptions.CultureInvariant | System.Text.RegularExpressions.RegexOptions.NonBacktracking);
    public static readonly TimeSpan RetryAfter = TimeSpan.FromSeconds(30);

    public void Add(IHostApplicationBuilder builder) => builder.Services.AddScoped<PaymentEventHandler>();

    public void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/webhooks/payments/{provider}", WebhookAsync).AllowAnonymous();
        // The mock checkout's simulate exists only in the fake profile (F-01): a deployed host has no such route, whatever its provider.
        if (NonDeployedProfile.IsActive(endpoints.ServiceProvider.GetRequiredService<IConfiguration>()))
            endpoints.MapPost("/api/v1/payments/simulate", SimulateAsync).RequireAuthorization("Authenticated");
    }

    static async Task<IResult> WebhookAsync(string provider, HttpContext http, IPaymentProvider payments, IConfiguration config,
        PaymentEventHandler handler, ILogger<PaymentWebhookFeature> log, CancellationToken ct)
    {
        if (!string.Equals(provider, payments.Name, StringComparison.OrdinalIgnoreCase)) return Problem(ErrorCodes.NotFound, "Not found", 404);

        var raw = await ReadRawAsync(http.Request, ct);
        if (raw is null) return Problem(ErrorCodes.ValidationFailed, "Validation failed", 413, "The body is larger than 64 KB.");

        // No secret configured: accepted in Mock mode, rejected in every other mode, before the adapter is asked.
        if (string.IsNullOrEmpty(config["Payments:WebhookSecret"]) && !payments.WebhookSecretConfigured && !IsMock(payments))
        {
            log.LogWarning("Webhook for {Provider} rejected: no webhook secret is configured", payments.Name);
            return Problem(ErrorCodes.Unauthenticated, "Unauthenticated", 401);
        }
        var verified = payments.VerifyWebhook(raw, http.Request.Headers[payments.SignatureHeader].FirstOrDefault());
        if (verified is null)
        {
            log.LogError(new EventId(SignatureFailedEventId, "PaymentWebhookSignatureFailed"),
                "ALERT payment webhook signature failure for {Provider}: event {ProviderEventId}", payments.Name, UnverifiedEventId(raw));
            return Problem(ErrorCodes.Unauthenticated, "Unauthenticated", 401);
        }
        return await RunAsync(handler, payments.Name, verified, http, ct);
    }

    static async Task<IResult> SimulateAsync(SimulatePaymentRequest? req, IPaymentProvider payments, BookingDbContext db, PaymentEventHandler handler,
        ClaimsPrincipal user, HttpContext http, CancellationToken ct)
    {
        if (!IsMock(payments)) return Problem(ErrorCodes.Forbidden, "Forbidden", 403, "Payments are not simulated outside Mock mode.");
        var outcome = req?.Outcome?.ToLowerInvariant();
        if (req is null || req.BookingId == Guid.Empty || outcome is not ("succeeded" or "failed"))
            return Problem(ErrorCodes.ValidationFailed, "Validation failed", 422, null,
                new() { ["outcome"] = ["outcome must be succeeded or failed"] });

        var payment = await db.Payments.AsNoTracking().SingleOrDefaultAsync(p => p.BookingId == req.BookingId, ct);
        var booking = payment is null ? null : await db.Bookings.AsNoTracking().SingleOrDefaultAsync(b => b.Id == req.BookingId, ct);
        // Only the client of the booking drives its mock checkout; anybody else gets the answer of an unknown id (F-01).
        var sub = user.FindFirst("sub")?.Value ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (payment is null || booking is null || sub is null || booking.ClientUserId != sub) return Problem(ErrorCodes.NotFound, "Not found", 404);

        var kind = outcome == "succeeded" ? WebhookEventKind.PaymentSucceeded : WebhookEventKind.PaymentFailed;
        var e = new ProviderEvent($"sim_{req.BookingId:N}_{outcome}", kind, payment.ProviderPaymentId, booking.PriceMinor, PaymentEventHandler.Currency);
        return await RunAsync(handler, payments.Name, e, http, ct);
    }

    static async Task<IResult> RunAsync(PaymentEventHandler handler, string provider, ProviderEvent e, HttpContext http, CancellationToken ct)
    {
        try
        {
            var result = await handler.HandleAsync(provider, e, ct);
            if (result.Outcome is not { } outcome)
            {
                http.Response.Headers.RetryAfter = ((int)RetryAfter.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture);
                return Results.Json(new { outcome = "RetryLater" }, statusCode: 202);
            }
            return Results.Ok(new { outcome = outcome.ToString() });
        }
        catch (DirectoryUnavailableException)
        {
            http.Response.Headers.RetryAfter = ((int)RetryAfter.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture);
            return Problem(ErrorCodes.DependencyUnavailable, "Dependency unavailable", 503, "A required service is temporarily unavailable.");
        }
    }

    /// <summary>
    /// The event id of a body whose signature did NOT verify, so it is attacker-controlled: it is read from a top-level string <c>eventId</c> (Mock) or <c>id</c> (Stripe) and returned only when it
    /// is 1-64 characters of letters, digits, underscore or hyphen (no way to forge a log line or smuggle data). Anything else, and every parse failure, gives
    /// <see cref="NoEventIdMarker"/>. Nothing else of the body, and not the signature, is ever read into a log.
    /// </summary>
    internal static string UnverifiedEventId(byte[] raw)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(raw);
            if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object
                && (doc.RootElement.TryGetProperty("eventId", out var v) || doc.RootElement.TryGetProperty("id", out v)) && v.ValueKind == System.Text.Json.JsonValueKind.String
                && v.GetString() is { } id && SafeEventId.IsMatch(id))
                return id;
        }
        catch (System.Text.Json.JsonException) { }
        return NoEventIdMarker;
    }

    /// <summary>The Mock provider is the only one that may run without a secret and the only one that may be simulated.</summary>
    static bool IsMock(IPaymentProvider payments) => string.Equals(payments.Name, "Mock", StringComparison.OrdinalIgnoreCase);

    static async Task<byte[]?> ReadRawAsync(HttpRequest request, CancellationToken ct)
    {
        if (request.ContentLength > MaxBodyBytes) return null;
        using var ms = new MemoryStream();
        var buffer = new byte[8192];
        int n;
        while ((n = await request.Body.ReadAsync(buffer, ct)) > 0)
        {
            if (ms.Length + n > MaxBodyBytes) return null;
            ms.Write(buffer, 0, n);
        }
        return ms.ToArray();
    }

    internal static IResult Problem(string code, string title, int status, string? detail = null, Dictionary<string, string[]>? errors = null) =>
        Results.Json(ApiProblem.Of(code, title, status, detail, errors), statusCode: status, contentType: "application/problem+json");
}
