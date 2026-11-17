using Gabinety.Booking.Jobs.AutoComplete;
using Gabinety.Booking.Jobs.Base;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using BookingEntity = Gabinety.Booking.Features.Bookings.Entities.Booking;
using BookingStatus = Gabinety.Billing.BookingStatus;

namespace Gabinety.Booking.Tests;

/// <summary>The job restricted to one practitioner: the test InMemory database is shared by every host in the process.</summary>
sealed class ScopedAutoCompleteJob(IServiceScopeFactory scopes, TimeProvider time, IConfiguration config, JobMetrics metrics, ILogger<AutoCompleteJob> log, Guid practitioner)
    : AutoCompleteJob(scopes, time, config, metrics, log)
{
    protected override IQueryable<BookingEntity> Scope(IQueryable<BookingEntity> q) => q.Where(b => b.PractitionerId == practitioner);
}

static class Auto
{
    public static readonly TimeSpan Window = TimeSpan.FromHours(48);

    public static CreateHost Host(params (string Key, string Value)[] settings) => Hold.Host(null, settings);

    /// <summary>A booking in <paramref name="status"/> whose session ended <paramref name="endedAgo"/> before the host clock.</summary>
    public static Task<Guid> Seed(CreateHost h, TimeSpan endedAgo, BookingStatus status = BookingStatus.Confirmed) =>
        Life.Seed(h, status, startIn: -endedAgo - TimeSpan.FromHours(1), duration: TimeSpan.FromHours(1));

    public static ScopedAutoCompleteJob Job(CreateHost h) =>
        new(h.Services.GetRequiredService<IServiceScopeFactory>(), h.Clock, h.Services.GetRequiredService<IConfiguration>(),
            h.Services.GetRequiredService<JobMetrics>(), h.Services.GetRequiredService<ILogger<AutoCompleteJob>>(), h.Practitioner);
}
