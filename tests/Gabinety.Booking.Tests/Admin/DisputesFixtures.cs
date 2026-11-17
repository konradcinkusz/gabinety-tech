using System.Net.Http.Json;
using Gabinety.Booking.Features.Lifecycle.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using BookingEntity = Gabinety.Booking.Features.Bookings.Entities.Booking;
using BookingStatus = Gabinety.Billing.BookingStatus;

namespace Gabinety.Booking.Tests;

/// <summary>What a registered hook saw: the status, the trigger and <c>payment.RefundedMinor</c> at the moment it ran (T-04.08c reads it to price the fee).</summary>
sealed record SeenByHook(Guid BookingId, string Trigger, long RefundedMinor);

sealed class RecordingHook : IBookingTransitionHook
{
    public List<SeenByHook> Seen { get; } = [];
    public Task OnTransitionAsync(BookingTransitionContext c, CancellationToken ct)
    {
        lock (Seen) Seen.Add(new(c.Booking.Id, c.Trigger.ToString(), c.Payment?.RefundedMinor ?? -1));
        return Task.CompletedTask;
    }
}

/// <summary>Helpers for the admin dispute tests. The in-memory database is shared by name across hosts, so every test uses its own client and practitioner user ids.</summary>
static class Adm
{
    public const string AdminUser = "demo-admin-1", Text = "demo-dispute-text: the session was not what the offer said", Note = "demo-resolution-note: reviewed both sides";

    public static string NewClient() => "demo-client-" + Guid.NewGuid().ToString("N");

    public static CreateHost Host(RecordingProvider provider, RecordingHook? hook = null) =>
        Life.Host(provider, h =>
        {
            h.OwnerUserId = "demo-pu-" + Guid.NewGuid().ToString("N");
            var configure = h.Configure;
            h.Configure = s =>
            {
                configure?.Invoke(s);
                if (hook is not null) s.AddSingleton<IBookingTransitionHook>(hook);
            };
        });

    /// <summary>A booking in <paramref name="status"/> (Disputed by default) with its dispute columns, owned by a fresh client.</summary>
    public static async Task<(Guid Id, string Client)> Seed(CreateHost h, BookingStatus status = BookingStatus.Disputed, string kind = "not_as_described",
        TimeSpan? startIn = null, string provider = "Mock", string text = Text, DateTimeOffset? openedAt = null, DateTimeOffset? resolvedAt = null, string? resolution = null)
    {
        var id = await Life.Seed(h, status, startIn ?? TimeSpan.FromHours(-3), provider: provider);
        var client = NewClient();
        await h.Seed(db =>
        {
            var b = db.Bookings.Single(x => x.Id == id);
            b.ClientUserId = client;
            if (status is BookingStatus.Disputed or BookingStatus.Resolved)
            {
                b.DisputeKind = kind;
                b.DisputeText = text;
                b.DisputeOpenedAt = openedAt ?? h.Clock.GetUtcNow();
            }
            if (status == BookingStatus.Resolved) { b.ResolvedAt = resolvedAt ?? h.Clock.GetUtcNow(); b.DisputeResolution = resolution ?? "release"; b.StatusReason = "demo-old-note-0123"; }
        });
        return (id, client);
    }

    public static HttpClient Admin(CreateHost h, string role = "Admin") => h.ClientFor(AdminUser, role);

    public static Task<HttpResponseMessage> Resolve(CreateHost h, Guid id, object body, string? as_ = AdminUser, string role = "Admin") =>
        (as_ == AdminUser ? Admin(h, role) : h.ClientFor(as_)).PostAsJsonAsync($"/api/v1/admin/bookings/{id}/resolve", body);

    public static object Body(string resolution, long? refundMinor = null, string note = Note) =>
        refundMinor is null ? new { resolution, note } : new { resolution, refundMinor, note };

    public static Task<HttpResponseMessage> List(CreateHost h, string query = "", string? as_ = AdminUser) =>
        (as_ == AdminUser ? Admin(h) : h.ClientFor(as_)).GetAsync("/api/v1/admin/disputes" + query);

    public static string[] LogLines(CreateHost h) => [.. h.Logs.Lines];
}
