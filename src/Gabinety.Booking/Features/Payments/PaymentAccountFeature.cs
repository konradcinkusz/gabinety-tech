using System.Security.Claims;
using Gabinety.Booking.Clients;
using Gabinety.Booking.Features.Availability;
using Gabinety.Booking.Payments.Abstractions;
using Gabinety.Booking.Persistence;
using Gabinety.Contracts.Common;
using Gabinety.ServiceDefaults;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Gabinety.Booking.Features.Payments;

/// <summary>
/// The practitioner's onboarding to the payment provider (T-09.05, ADR proposal 0011), practitioner only:
/// <c>GET /api/v1/me/payments/account</c> answers <c>{ provider, state }</c> with state <c>none</c>, <c>pending</c> or <c>ready</c> (a pending account is asked about at the provider, so the
/// answer is fresh); <c>POST /api/v1/me/payments/account/onboarding</c> creates the connected account when there is none and answers a single-use <c>onboardingUrl</c> of the provider's hosted flow.
/// Only the provider's account id is stored (<see cref="PaymentAccount"/>). A provider with no connected accounts (the Mock) answers 404. The links are never logged.
/// </summary>
public sealed class PaymentAccountFeature : IServiceFeature
{
    public sealed record AccountStateDto(string Provider, string State);
    public sealed record OnboardingDto(string Provider, string State, string? OnboardingUrl);
    const string UniqueViolation = "23505";

    public void Add(IHostApplicationBuilder builder) { }

    public void Map(IEndpointRouteBuilder endpoints)
    {
        var g = endpoints.MapGroup("/api/v1/me/payments/account").RequireAuthorization("Authenticated");
        g.MapGet("", (ClaimsPrincipal user, PractitionerResolver who, BookingDbContext db, IPaymentProvider payments, TimeProvider time, ILogger<PaymentAccountFeature> log, CancellationToken ct) =>
            AsPractitioner(user, who, payments, log, async (id, accounts) =>
            {
                var row = await db.PaymentAccounts.SingleOrDefaultAsync(a => a.PractitionerId == id && a.Provider == payments.Name, ct);
                if (row is null) return Results.Ok(new AccountStateDto(payments.Name, "none"));
                if (!row.Ready) await RefreshAsync(db, accounts, row, time.GetUtcNow(), ct);
                return Results.Ok(new AccountStateDto(payments.Name, row.Ready ? "ready" : "pending"));
            }, ct));

        g.MapPost("/onboarding", (ClaimsPrincipal user, PractitionerResolver who, BookingDbContext db, IPaymentProvider payments, IConfiguration config, TimeProvider time,
            ILogger<PaymentAccountFeature> log, CancellationToken ct) =>
            AsPractitioner(user, who, payments, log, async (id, accounts) =>
            {
                var now = time.GetUtcNow();
                var row = await db.PaymentAccounts.SingleOrDefaultAsync(a => a.PractitionerId == id && a.Provider == payments.Name, ct);
                if (row is null)
                {
                    var accountId = await accounts.CreateAccountAsync(id, ct); // idempotent at the provider: the key is the practitioner
                    db.PaymentAccounts.Add(row = new PaymentAccount { PractitionerId = id, Provider = payments.Name, ProviderAccountId = accountId, Ready = false, CreatedAt = now, UpdatedAt = now });
                    try { await db.SaveChangesAsync(ct); }
                    catch (DbUpdateException e) when (IsUnique(e))
                    {
                        db.ChangeTracker.Clear(); // a concurrent request stored the same account first (same id: the provider call was idempotent)
                        row = await db.PaymentAccounts.SingleAsync(a => a.PractitionerId == id && a.Provider == payments.Name, ct);
                    }
                    log.LogInformation("Payment account created for practitioner {PractitionerId}", id);
                }
                if (!row.Ready) await RefreshAsync(db, accounts, row, now, ct);
                if (row.Ready) return Results.Ok(new OnboardingDto(payments.Name, "ready", null));
                var site = SiteUrl.From(config);
                var url = await accounts.CreateOnboardingLinkAsync(row.ProviderAccountId, $"{site}/practitioner/payments?return=1", $"{site}/practitioner/payments?refresh=1", ct);
                return Results.Ok(new OnboardingDto(payments.Name, "pending", url));
            }, ct));
    }

    static async Task RefreshAsync(BookingDbContext db, IConnectedAccountProvider accounts, PaymentAccount row, DateTimeOffset now, CancellationToken ct)
    {
        var status = await accounts.GetAccountStatusAsync(row.ProviderAccountId, ct);
        var ready = status.State == ConnectedAccountState.Ready;
        if (ready == row.Ready) return;
        row.Ready = ready;
        row.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
    }

    static async Task<IResult> AsPractitioner(ClaimsPrincipal user, PractitionerResolver who, IPaymentProvider payments, ILogger log,
        Func<Guid, IConnectedAccountProvider, Task<IResult>> run, CancellationToken ct)
    {
        if (payments is not IConnectedAccountProvider accounts) return Problem(ErrorCodes.NotFound, "Not found", 404);
        var userId = user.FindFirst("sub")?.Value ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (userId is null) return Results.Unauthorized();
        try
        {
            if (await who.ResolveAsync(userId, ct) is not { } practitioner) return Problem(ErrorCodes.Forbidden, "Forbidden", 403, "The caller is not a practitioner.");
            return await run(practitioner, accounts);
        }
        catch (DirectoryUnavailableException) { return Unavailable(); }
        catch (PaymentProviderException e)
        {
            log.LogError("Payment provider refused an onboarding call: HTTP {Status} {Code}", e.Status, e.Code); // the provider's answer is not passed on to the caller
            return Unavailable();
        }
        catch (HttpRequestException) { return Unavailable(); }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested) { return Unavailable(); }
    }

    static IResult Unavailable() => Problem(ErrorCodes.DependencyUnavailable, "Dependency unavailable", 503, "A required service is temporarily unavailable.");

    static IResult Problem(string code, string title, int status, string? detail = null) =>
        Results.Json(ApiProblem.Of(code, title, status, detail), statusCode: status, contentType: "application/problem+json");

    static bool IsUnique(Exception e)
    {
        for (var x = e; x is not null; x = x.InnerException) if (x is PostgresException { SqlState: UniqueViolation }) return true;
        return false;
    }
}
