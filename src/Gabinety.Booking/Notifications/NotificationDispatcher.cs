using Gabinety.Booking.Payments.Abstractions;
using Gabinety.Booking.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Gabinety.Booking.Notifications;

/// <summary>
/// One dispatcher cycle (CONTRACTS 8.1): sends the due, unsent notifications, oldest first, at most <see cref="BatchSize"/>.
/// <para>A row is CLAIMED before anything is sent: the claim adds one to <c>attempts</c> and moves <c>due_at</c> to the time of the next attempt
/// (<see cref="Backoff"/>) in a single UPDATE whose WHERE carries the old <c>attempts</c> (the concurrency token). Of two dispatchers that read the same
/// row only one UPDATE matches; the other gets a concurrency exception and skips the row. This is the same on PostgreSQL and InMemory, so it needs no lock.
/// <c>sent_at</c> is set only after the sender accepted the message, so a crash between the two costs one attempt, never a lost message
/// (a rare duplicate is possible: the delivery is at-least-once). A row at <see cref="MaxAttempts"/> attempts is never selected again.</para>
/// <para>CUT-OFF: a <c>Reminder24h</c>/<c>Reminder1h</c> is useless once the consultation has started, and a retry after backoff (up to 16 h) can land
/// after the start. When the booking start is not after <c>now</c> the reminder is GIVEN UP: <c>attempts</c> goes to <see cref="MaxAttempts"/>
/// (the existing give-up state, no new column) and nothing is sent. Other kinds (e.g. <c>ReviewRequest</c>, which is due after the end) are unaffected.</para>
/// </summary>
public sealed class NotificationDispatcher(IServiceScopeFactory scopes, TimeProvider time, ILogger<NotificationDispatcher> log)
{
    public const int MaxAttempts = 5, BatchSize = 50;

    /// <summary>The wait after the 1st, 2nd, 3rd and 4th failed attempt: all five attempts fit in 23 h 10 min (the contract allows 24 h).</summary>
    public static readonly IReadOnlyList<TimeSpan> Backoff = [TimeSpan.FromMinutes(10), TimeSpan.FromHours(1), TimeSpan.FromHours(6), TimeSpan.FromHours(16)];

    /// <summary>The number of notifications sent in this cycle. Only cancellation escapes.</summary>
    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        List<Guid> ids;
        using (var scope = scopes.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BookingDbContext>();
            var now = time.GetUtcNow();
            ids = await db.Notifications.AsNoTracking()
                .Where(n => n.SentAt == null && n.Attempts < MaxAttempts && n.DueAt <= now)
                .OrderBy(n => n.DueAt).ThenBy(n => n.Id)
                .Take(BatchSize).Select(n => n.Id).ToListAsync(ct);
        }

        var sent = 0;
        foreach (var id in ids)
        {
            ct.ThrowIfCancellationRequested();
            try { if (await SendOneAsync(id, ct)) sent++; }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError("Notification {NotificationId} could not be processed: {ExceptionType}", id, ex.GetType().Name);
            }
        }
        if (sent > 0) log.LogInformation("Notifications sent: {Count} of {Due} due", sent, ids.Count);
        return sent;
    }

    async Task<bool> SendOneAsync(Guid id, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<BookingDbContext>();
        var now = time.GetUtcNow();

        var n = await db.Notifications.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (n is null || n.SentAt != null || n.Attempts >= MaxAttempts || n.DueAt > now) return false;

        var booking = await db.Bookings.AsNoTracking().Where(b => b.Id == n.BookingId)
            .Select(b => new { b.StartUtc, b.PractitionerUserId, b.ClientEmail }).SingleOrDefaultAsync(ct);
        if (booking is not null && IsReminder(n.Kind) && booking.StartUtc <= now)
        {
            n.Attempts = MaxAttempts; // the give-up state: never selected again, sent_at stays null
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return false; } // claimed by another dispatcher, which applies the same rule
            log.LogWarning("Notification {NotificationId} ({Kind}) to user {UserId} given up: the booking start has passed", n.Id, n.Kind, n.ToUserId);
            return false;
        }

        n.Attempts++;
        if (n.Attempts < MaxAttempts) n.DueAt = now + Backoff[n.Attempts - 1];
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { return false; } // another dispatcher claimed it first, or it was cancelled meanwhile

        if (booking is null) return Failed(n, "the booking does not exist");

        var forPractitioner = n.ToUserId == booking.PractitionerUserId;
        string? address;
        try { address = await sp.GetRequiredService<IContactResolver>().ResolveAddressAsync(new ContactLookup(n.ToUserId, forPractitioner, booking.ClientEmail), ct); }
        catch (Exception ex) when (ex is not OperationCanceledException) { return Failed(n, "the address lookup failed: " + ex.GetType().Name); }
        if (string.IsNullOrWhiteSpace(address)) return Failed(n, "the user has no address");

        var mail = NotificationTemplates.Render(n.Kind, SiteUrl.From(sp.GetRequiredService<IConfiguration>()), n.BookingId, booking.StartUtc, forPractitioner);
        try { await sp.GetRequiredService<IEmailSender>().SendAsync(new EmailMessage(address, n.ToUserId, n.Kind, mail.Subject, mail.Text, mail.Html), ct); }
        catch (Exception ex) when (ex is not OperationCanceledException) { return Failed(n, "the sender failed: " + ex.GetType().Name); }

        n.SentAt = time.GetUtcNow();
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { log.LogWarning("Notification {NotificationId} was sent but its row was removed meanwhile", n.Id); }
        return true;
    }

    static bool IsReminder(NotificationKind kind) => kind is NotificationKind.Reminder24h or NotificationKind.Reminder1h;

    bool Failed(Notification n, string reason)
    {
        log.LogWarning("Notification {NotificationId} ({Kind}) to user {UserId}, attempt {Attempt} of {Max} failed: {Reason}",
            n.Id, n.Kind, n.ToUserId, n.Attempts, MaxAttempts, reason);
        return false;
    }
}

/// <summary>Runs the dispatcher every 60 s on <see cref="TimeProvider"/>. Self-contained: <c>Jobs/Base</c> (T-04.08a) is not on main yet, so move this onto it when it lands.</summary>
public sealed class NotificationDispatcherHostedService(NotificationDispatcher dispatcher, TimeProvider time, ILogger<NotificationDispatcherHostedService> log) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(60);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, time);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try { await dispatcher.RunOnceAsync(stoppingToken); }
                catch (Exception ex) when (ex is not OperationCanceledException) { log.LogError("Notification cycle failed: {ExceptionType}", ex.GetType().Name); }
            }
        }
        catch (OperationCanceledException) { }
    }
}
