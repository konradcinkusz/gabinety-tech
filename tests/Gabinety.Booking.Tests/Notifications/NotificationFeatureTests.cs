using Gabinety.Booking.Notifications;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Gabinety.Booking.Tests;

public class NotificationFeatureTests
{
    [Theory]
    [InlineData("Sendgrid")]
    public void An_unknown_provider_stops_the_host_and_names_the_key(string provider)
    {
        // Asserted on the feature, not through a WebApplicationFactory: a refused host build can surface ObjectDisposedException instead of this message.
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Notifications:Provider"] = provider });
        var ex = Assert.Throws<InvalidOperationException>(() => new NotificationsFeature().Add(builder));
        Assert.Contains("Notifications:Provider", ex.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Log")]
    [InlineData("log")]
    public void The_default_is_the_log_sender_and_without_a_Directory_the_null_resolver(string? provider)
    {
        using var host = new CreateHost();
        host.Settings["Backend:Directory"] = ""; // no Directory: no practitioner address can be found, so the null resolver
        if (provider != null) host.Settings["Notifications:Provider"] = provider;
        Assert.IsType<LogEmailSender>(host.Services.GetRequiredService<IEmailSender>());
        Assert.IsType<NullContactResolver>(host.Services.GetRequiredService<IContactResolver>());
        using var scope = host.Services.CreateScope();
        Assert.IsType<NotificationWriter>(scope.ServiceProvider.GetRequiredService<INotificationWriter>());
        Assert.Contains(host.Services.GetServices<IHostedService>(), s => s is NotificationDispatcherHostedService);
    }

    [Fact]
    public async Task The_log_sender_logs_the_user_id_kind_and_subject_length_but_never_the_address_or_the_body()
    {
        const string address = "demo-secret-person@demo.invalid";
        using var t = new NotificationHost(services: s => s.AddSingleton<IEmailSender, LogEmailSender>()); // the real default, over the test resolver
        t.Contacts.Addresses["demo-client"] = address;
        var booking = await t.AddBooking();
        var n = t.Add(booking.Id, NotificationKind.BookingConfirmed, "demo-client");
        var mail = NotificationTemplates.Render(NotificationKind.BookingConfirmed, "http://localhost:3000", booking.Id, booking.StartUtc);

        Assert.Equal(1, await t.Run());

        var lines = t.Host.Logs.Lines.Where(l => l.StartsWith("Gabinety.Booking.Notifications.LogEmailSender")).ToList();
        var line = Assert.Single(lines);
        Assert.Contains("demo-client", line);
        Assert.Contains("BookingConfirmed", line);
        Assert.Contains($"SubjectLength={mail.Subject.Length}", line);
        var everything = string.Join('\n', t.Host.Logs.Lines);
        Assert.DoesNotContain(address, everything);
        Assert.DoesNotContain("demo-secret-person", everything);
        Assert.DoesNotContain(mail.Subject, everything);
        Assert.DoesNotContain("http://localhost:3000", everything);
        Assert.NotNull((await t.Row(n.Id)).SentAt);
    }

    [Fact]
    public async Task A_failing_attempt_logs_the_exception_type_but_no_address()
    {
        const string address = "demo-failing@demo.invalid";
        using var t = new NotificationHost();
        t.Sender.AlwaysFail = true;
        t.Contacts.Addresses["demo-client"] = address;
        var booking = await t.AddBooking();
        t.Add(booking.Id, NotificationKind.Cancelled, "demo-client");
        await t.Run();
        var everything = string.Join('\n', t.Host.Logs.Lines);
        Assert.DoesNotContain(address, everything);
        Assert.Contains(nameof(InvalidOperationException), everything);
    }
}
