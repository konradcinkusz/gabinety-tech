using System.Text.Json;
using System.Transactions;
using Gabinety.Booking.Features.Bookings.Create;
using Gabinety.Contracts.Booking;
using Gabinety.Booking.Features.Payments;
using Gabinety.Booking.Features.Payouts;
using Gabinety.Booking.Jobs.Base;
using Gabinety.Booking.Jobs.Payout;
using Gabinety.Booking.Payments.Abstractions;
using Gabinety.Booking.Payments.Mock;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using BookingEntity = Gabinety.Booking.Features.Bookings.Entities.Booking;
using BookingStatus = Gabinety.Billing.BookingStatus;

namespace Gabinety.Booking.Tests;

/// <summary>Wraps the real scope factory and counts live scopes, so a test can see that no scope (hence no DbContext and no transaction) is open during a provider call.</summary>
sealed class CountingScopeFactory(IServiceScopeFactory inner) : IServiceScopeFactory
{
    int _live;
    public int Live => Volatile.Read(ref _live);
    public IServiceScope CreateScope() { Interlocked.Increment(ref _live); return new Scope(inner.CreateScope(), this); }

    sealed class Scope(IServiceScope scope, CountingScopeFactory owner) : IServiceScope
    {
        int _disposed;
        public IServiceProvider ServiceProvider => scope.ServiceProvider;
        public void Dispose() { if (Interlocked.Exchange(ref _disposed, 1) == 0) { scope.Dispose(); Interlocked.Decrement(ref owner._live); } }
    }
}

/// <summary>The mock provider that remembers every payout request, can fail on demand, and notes any call made while a scope or an ambient transaction is open.</summary>
sealed class PayoutProvider : IPaymentProvider
{
    readonly MockPaymentProvider _inner = new("http://localhost:3000", null);
    public List<PayoutRequest> Payouts { get; } = [];
    public List<string> Violations { get; } = [];
    public List<bool> Cancellable { get; } = [];
    public CountingScopeFactory? Scopes { get; set; }
    /// <summary>The next N payout calls throw (the message carries the idempotency key, which must never reach a log).</summary>
    public int FailNext;
    public Func<PayoutRequest, Task>? Before { get; set; }

    public string Name => "Mock";
    public Task<CheckoutResult> CreateCheckoutAsync(CheckoutRequest r, CancellationToken ct) => _inner.CreateCheckoutAsync(r, ct);
    public Task<RefundResult> RefundAsync(RefundRequest r, CancellationToken ct) => _inner.RefundAsync(r, ct);
    public Gabinety.Booking.Payments.Abstractions.WebhookEvent? VerifyWebhook(ReadOnlySpan<byte> rawBody, string? sig) => _inner.VerifyWebhook(rawBody, sig);

    public async Task<PayoutResult> PayoutAsync(PayoutRequest r, CancellationToken ct)
    {
        lock (Payouts)
        {
            Payouts.Add(r);
            Cancellable.Add(ct.CanBeCanceled);
            if (Scopes is { Live: > 0 }) Violations.Add("a service scope (database context) was open during the provider call");
            if (Transaction.Current is not null) Violations.Add("an ambient transaction was open during the provider call");
        }
        if (Before is not null) await Before(r);
        lock (Payouts)
            if (FailNext > 0) { FailNext--; throw new HttpRequestException("provider down for " + r.IdempotencyKey); }
        return await _inner.PayoutAsync(r, ct);
    }
}

sealed class ScopedPayoutJob(IServiceScopeFactory scopes, IPaymentProvider provider, TimeProvider time, IConfiguration config, JobMetrics metrics, ILogger<PayoutJob> log, Guid practitioner)
    : PayoutJob(scopes, provider, time, config, metrics, log)
{
    protected override IQueryable<BookingEntity> Scope(IQueryable<BookingEntity> q) => q.Where(b => b.PractitionerId == practitioner);
}

static class PayJob
{
    public static (CreateHost Host, PayoutProvider Provider) Host(params (string Key, string Value)[] settings)
    {
        var provider = new PayoutProvider();
        var h = new CreateHost { MeetingUrl = "https://meet.demo.invalid/room-1" };
        h.Settings["Jobs:Enabled"] = "false";
        foreach (var (k, v) in settings) h.Settings[k] = v;
        h.Configure = s => { s.RemoveAll<IPaymentProvider>(); s.AddSingleton<IPaymentProvider>(provider); };
        return (h, provider);
    }

    public static ScopedPayoutJob Job(CreateHost h, PayoutProvider provider)
    {
        var scopes = new CountingScopeFactory(h.Services.GetRequiredService<IServiceScopeFactory>());
        provider.Scopes = scopes;
        return new(scopes, provider, h.Clock, h.Services.GetRequiredService<IConfiguration>(), h.Services.GetRequiredService<JobMetrics>(),
            h.Services.GetRequiredService<ILogger<PayoutJob>>(), h.Practitioner);
    }

    /// <summary>A booking in <paramref name="status"/> whose payout fell due <paramref name="dueAgo"/> before the host clock (the fee hook's result is seeded directly).</summary>
    public static async Task<Guid> Seed(CreateHost h, BookingStatus status = BookingStatus.Completed, string? resolution = null, TimeSpan? dueAgo = null,
        long? payoutMinor = 8500, bool setDue = true, int slot = 0)
    {
        var id = await Life.Seed(h, status, startIn: TimeSpan.FromDays(-3) - TimeSpan.FromHours(3 * slot), duration: TimeSpan.FromHours(1));
        await h.Seed(db =>
        {
            var b = db.Bookings.Single(x => x.Id == id);
            b.DisputeResolution = resolution;
            b.PayoutMinor = payoutMinor;
            b.FeeMinor = payoutMinor is null ? null : h.PriceMinor - payoutMinor;
            b.PayoutDueAt = setDue ? h.Clock.GetUtcNow() - (dueAgo ?? TimeSpan.FromMinutes(1)) : null;
            if (status == BookingStatus.Resolved) b.ResolvedAt = h.Clock.GetUtcNow() - TimeSpan.FromMinutes(5);
        });
        return id;
    }

    public static string Key(Guid id) => $"payout:{id:N}";
    public static Task<List<Payout>> PayoutRows(CreateHost h, Guid id) => h.Query(db => db.Payouts.AsNoTracking().Where(p => p.BookingId == id).ToListAsync());
    public static async Task<List<LedgerEntry>> PayoutLedger(CreateHost h, Guid id) => (await Life.Ledger(h, id)).Where(l => l.Kind == LedgerKind.PractitionerPayout).ToList();

    /// <summary>Every row a payout run could touch, as text, so "changes nothing" is an equality.</summary>
    public static Task<string> Snapshot(CreateHost h) => h.Query(async db =>
    {
        var ids = await db.Bookings.AsNoTracking().Where(b => b.PractitionerId == h.Practitioner).Select(b => b.Id).ToListAsync();
        var bookings = await db.Bookings.AsNoTracking().Where(b => ids.Contains(b.Id)).OrderBy(b => b.Id).ToListAsync();
        var payouts = await db.Payouts.AsNoTracking().Where(p => ids.Contains(p.BookingId)).OrderBy(p => p.Id).ToListAsync();
        var ledger = await db.LedgerEntries.AsNoTracking().Where(l => ids.Contains(l.BookingId)).OrderBy(l => l.Id).ToListAsync();
        return JsonSerializer.Serialize(new { bookings, payouts, ledger });
    });
}
