using System.Collections.Concurrent;
using Gabinety.Booking.Notifications;
using Gabinety.Booking.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using BookingEntity = Gabinety.Booking.Features.Bookings.Entities.Booking;
using BookingStatus = Gabinety.Billing.BookingStatus;

namespace Gabinety.Booking.Tests;

/// <summary>Records every message; the first <see cref="FailFirst"/> calls (or all, with <see cref="AlwaysFail"/>) throw like a provider that is down.</summary>
sealed class RecordingEmailSender : IEmailSender
{
    public readonly ConcurrentQueue<EmailMessage> Sent = new();
    int _calls;
    public int Calls => Volatile.Read(ref _calls);
    public int FailFirst;
    public bool AlwaysFail;
    public bool Yield;

    public async Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        var n = Interlocked.Increment(ref _calls);
        if (Yield) await Task.Delay(5, ct); // widens the window in which a second dispatcher could grab the same row
        if (AlwaysFail || n <= FailFirst) throw new InvalidOperationException("demo provider is down");
        Sent.Enqueue(message);
    }

    public List<EmailMessage> For(string userId) => Sent.Where(m => m.ToUserId == userId).ToList();
}

/// <summary>The test double of <see cref="IContactResolver"/>: user id to address; an unknown user has none.</summary>
sealed class FakeContacts : IContactResolver
{
    public readonly ConcurrentDictionary<string, string?> Addresses = new();
    public string? Default;
    public Task<string?> ResolveAddressAsync(ContactLookup recipient, CancellationToken ct) =>
        Task.FromResult(Addresses.TryGetValue(recipient.UserId, out var a) ? a : Default);
}

/// <summary>A Booking host with its OWN in-memory database (the shared one would let another test's dispatcher claim these rows) and the doubles above.</summary>
sealed class NotificationHost : IDisposable
{
    public CreateHost Host { get; }
    public RecordingEmailSender Sender { get; } = new();
    public FakeContacts Contacts { get; } = new() { Default = "demo-person@demo.invalid" };
    public NotificationDispatcher Dispatcher => Host.Services.GetRequiredService<NotificationDispatcher>();
    public DateTimeOffset Now => Host.Clock.GetUtcNow();

    public NotificationHost(string? dbName = null, Action<CreateHost>? setup = null, Action<IServiceCollection>? services = null, bool keepSender = false)
    {
        var name = dbName ?? "notif-" + Guid.NewGuid().ToString("N");
        Host = new CreateHost();
        setup?.Invoke(Host);
        Host.Configure = s =>
        {
            s.AddDbContext<BookingDbContext>(o => o.UseInMemoryDatabase(name)); // applied after the feature's own configuration, so it wins
            if (!keepSender) { s.RemoveAll<IEmailSender>(); s.AddSingleton<IEmailSender>(Sender); } // keepSender: the one the feature registered
            s.RemoveAll<IContactResolver>();
            s.AddSingleton<IContactResolver>(Contacts);
            services?.Invoke(s);
        };
    }

    public Task<int> Run() => Dispatcher.RunOnceAsync(CancellationToken.None);

    public Notification Add(Guid bookingId, NotificationKind kind, string userId, DateTimeOffset? due = null, int attempts = 0, DateTimeOffset? sentAt = null)
    {
        var n = new Notification { Id = Guid.NewGuid(), BookingId = bookingId, Kind = kind, ToUserId = userId, DueAt = due ?? Now, Attempts = attempts, SentAt = sentAt };
        Host.Seed(db => db.Notifications.Add(n)).GetAwaiter().GetResult();
        return n;
    }

    public Task<BookingEntity> AddBooking(Guid? id = null, Action<BookingEntity>? edit = null)
    {
        var b = new BookingEntity
        {
            Id = id ?? Guid.NewGuid(), ClientUserId = "demo-client", PractitionerUserId = "demo-practitioner", PractitionerId = Guid.NewGuid(), OfferingId = Guid.NewGuid(),
            OfferingTitle = "demo-offering", DurationMinutes = 60, PriceMinor = 10000, StartUtc = Now.AddDays(2), EndUtc = Now.AddDays(2).AddHours(1),
            Status = BookingStatus.Confirmed, IdempotencyKey = Guid.NewGuid(), CreatedAt = Now,
        };
        edit?.Invoke(b);
        return Host.Seed(db => db.Bookings.Add(b)).ContinueWith(_ => b);
    }

    public Task<Notification> Row(Guid id) => Host.Query(db => db.Notifications.AsNoTracking().SingleAsync(n => n.Id == id));

    public void Dispose() => Host.Dispose();
}
