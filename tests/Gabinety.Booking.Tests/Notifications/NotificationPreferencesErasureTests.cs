using System.Net;
using Gabinety.Booking.Notifications;
using Gabinety.Booking.Notifications.Preferences;
using Microsoft.EntityFrameworkCore;

namespace Gabinety.Booking.Tests;

/// <summary>T-11.12: DELETE /internal/v1/subjects/{userId} also deletes the user's notification preferences, and only theirs.</summary>
public class NotificationPreferencesErasureTests
{
    [Fact]
    public async Task Erasing_a_user_deletes_their_preference_rows_and_keeps_every_other_users_rows()
    {
        using var h = ErasureApiTests.NewHost();
        var erased = ErasureApiTests.NewUser("client");
        var other = ErasureApiTests.NewUser("client");
        await h.Seed(db =>
        {
            db.NotificationPreferences.Add(new NotificationPreference { UserId = erased, Kind = NotificationKind.Reminder24h, Enabled = false });
            db.NotificationPreferences.Add(new NotificationPreference { UserId = erased, Kind = NotificationKind.ReviewRequest, Enabled = true });
            db.NotificationPreferences.Add(new NotificationPreference { UserId = other, Kind = NotificationKind.Reminder24h, Enabled = false });
        });

        var r = await ErasureApiTests.Erase(h, erased);

        Assert.Equal(HttpStatusCode.NoContent, r.StatusCode);
        var rows = await h.Query(db => db.NotificationPreferences.AsNoTracking().Where(p => p.UserId == erased || p.UserId == other).ToListAsync());
        var kept = Assert.Single(rows);
        Assert.Equal((other, NotificationKind.Reminder24h, false), (kept.UserId, kept.Kind, kept.Enabled));
    }
}
