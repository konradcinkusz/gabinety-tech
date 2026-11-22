using System.Text.Json;
using Gabinety.Booking.Notifications;
using Gabinety.Booking.Notifications.Smtp;
using Microsoft.Extensions.DependencyInjection;

namespace Gabinety.Booking.Tests.Smtp;

/// <summary>Notifications:Provider=Smtp through the real host: what is registered, what /health/details says, and the existing job retrying the real sender.</summary>
public class SmtpWiringTests
{
    const string Password = "demo-smtp-password-DO-NOT-LEAK";

    static void Smtp(CreateHost h, FakeSmtpServer? s, bool complete = true)
    {
        h.Settings["Notifications:Provider"] = "Smtp";
        if (s is null) return;
        h.Settings["Notifications:Smtp:Host"] = "127.0.0.1";
        h.Settings["Notifications:Smtp:Port"] = s.Port.ToString();
        h.Settings["Notifications:Smtp:TlsMode"] = "None";
        h.Settings["Notifications:Smtp:User"] = "demo-smtp-user";
        h.Settings["Notifications:Smtp:Password"] = Password;
        if (complete) h.Settings["Notifications:Smtp:FromAddress"] = "powiadomienia@demo.invalid";
    }

    static async Task<(string Status, Dictionary<string, string?> Checks, string Raw)> Health(CreateHost h)
    {
        var r = await h.CreateClient().GetAsync("/health/details");
        Assert.Equal(System.Net.HttpStatusCode.OK, r.StatusCode);
        var raw = await r.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(raw);
        var checks = doc.RootElement.GetProperty("checks").EnumerateArray().ToDictionary(c => c.GetProperty("name").GetString()!, c => c.GetProperty("status").GetString());
        return (doc.RootElement.GetProperty("status").GetString()!, checks, raw);
    }

    [Fact]
    public async Task A_configured_smtp_provider_registers_the_smtp_sender_and_reports_the_email_dependency_Healthy()
    {
        using var server = new FakeSmtpServer();
        using var host = new CreateHost();
        Smtp(host, server);
        Assert.IsType<SmtpEmailSender>(host.Services.GetRequiredService<IEmailSender>());
        var (_, checks, raw) = await Health(host);
        Assert.Equal("Healthy", checks["email"]);
        Assert.DoesNotContain(Password, raw);
        Assert.DoesNotContain("127.0.0.1", raw);
    }

    [Fact]
    public async Task A_missing_host_degrades_to_the_log_sender_and_health_reports_Degraded_not_a_startup_failure()
    {
        using var host = new CreateHost();
        Smtp(host, null); // Provider=Smtp and nothing else
        Assert.IsType<LogEmailSender>(host.Services.GetRequiredService<IEmailSender>());
        var (status, checks, _) = await Health(host);
        Assert.Equal("Degraded", checks["email"]);
        Assert.Equal("Degraded", status);
        Assert.Equal(System.Net.HttpStatusCode.OK, (await host.CreateClient().GetAsync("/health")).StatusCode); // never Unhealthy, never down
    }

    [Fact]
    public async Task A_missing_password_degrades_to_the_log_sender_and_the_startup_log_names_the_key_but_no_value()
    {
        using var host = new CreateHost();
        using var server = new FakeSmtpServer();
        Smtp(host, server, complete: true);
        host.Settings.Remove("Notifications:Smtp:Password");
        Assert.IsType<LogEmailSender>(host.Services.GetRequiredService<IEmailSender>());
        Assert.Equal("Degraded", (await Health(host)).Checks["email"]);
        var all = string.Join('\n', host.Logs.Lines);
        Assert.Contains("Notifications:Smtp:Password", all);
        Assert.DoesNotContain("demo-smtp-user", all);
    }

    [Fact]
    public async Task A_missing_from_address_degrades_too()
    {
        using var server = new FakeSmtpServer();
        using var host = new CreateHost();
        Smtp(host, server, complete: false);
        Assert.IsType<LogEmailSender>(host.Services.GetRequiredService<IEmailSender>());
        Assert.Equal("Degraded", (await Health(host)).Checks["email"]);
    }

    [Fact]
    public async Task The_log_provider_has_no_email_check_and_an_unknown_provider_still_stops_the_host()
    {
        using (var host = new CreateHost()) Assert.DoesNotContain("email", (await Health(host)).Checks.Keys);
        var builder = Microsoft.Extensions.Hosting.Host.CreateEmptyApplicationBuilder(new Microsoft.Extensions.Hosting.HostApplicationBuilderSettings());
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Notifications:Provider"] = "Sendgrid" });
        var ex = Assert.Throws<InvalidOperationException>(() => new NotificationsFeature().Add(builder)); // not via WebApplicationFactory: a refused build can surface ObjectDisposedException
        Assert.Contains("Notifications:Provider", ex.Message);
    }

    [Fact]
    public async Task The_log_provider_in_a_deployed_profile_warns_once_at_startup_and_health_reports_email_Degraded_never_claiming_delivery()
    {
        using var host = new CreateHost().AsDeployed(); // not fake, Notifications:Provider unset = Log
        Assert.IsType<LogEmailSender>(host.Services.GetRequiredService<IEmailSender>());
        var (status, checks, _) = await Health(host);
        Assert.Equal("Degraded", checks["email"]);
        Assert.Equal("Degraded", status);
        Assert.Equal(1, host.Logs.Lines.Count(l => l.Contains("Dependency email is Degraded") && l.Contains("Notifications:Provider is Log") && l.Contains("NOT delivered")));
        Assert.Equal(System.Net.HttpStatusCode.OK, (await host.CreateClient().GetAsync("/health")).StatusCode);
    }

    [Fact]
    public async Task The_log_provider_in_the_fake_profile_stays_quiet()
    {
        using var host = new CreateHost();
        Assert.DoesNotContain("email", (await Health(host)).Checks.Keys);
        Assert.DoesNotContain(host.Logs.Lines, l => l.Contains("Dependency email"));
    }

    [Fact]
    public async Task The_degraded_log_sender_still_lets_the_job_run_and_logs_no_address()
    {
        using var t = new NotificationHost(setup: h => Smtp(h, null), keepSender: true);
        t.Contacts.Addresses["demo-client"] = "demo-degraded@demo.invalid";
        var booking = await t.AddBooking();
        var n = t.Add(booking.Id, NotificationKind.BookingConfirmed, "demo-client");
        Assert.Equal(1, await t.Run());
        Assert.NotNull((await t.Row(n.Id)).SentAt);
        Assert.DoesNotContain("demo-degraded", string.Join('\n', t.Host.Logs.Lines));
    }

    [Fact]
    public async Task The_existing_job_delivers_through_the_real_sender_and_retries_a_refusal_without_a_change_to_the_job()
    {
        using var server = new FakeSmtpServer { RejectRcpt = "demo-person" };
        using var t = new NotificationHost(setup: h => Smtp(h, server), keepSender: true);
        t.Contacts.Addresses["demo-client"] = "demo-person@demo.invalid";
        var booking = await t.AddBooking();
        var n = t.Add(booking.Id, NotificationKind.BookingConfirmed, "demo-client");

        Assert.Equal(0, await t.Run()); // the provider refuses: counted as a failed attempt
        var after1 = await t.Row(n.Id);
        Assert.Null(after1.SentAt);
        Assert.Equal(1, after1.Attempts);
        Assert.Empty(server.Mails);

        server.RejectRcpt = null; // the provider recovers
        t.Host.Clock.Advance(NotificationDispatcher.Backoff[0]);
        Assert.Equal(1, await t.Run());

        var row = await t.Row(n.Id);
        Assert.NotNull(row.SentAt);
        Assert.Equal(2, row.Attempts);
        var mail = Assert.Single(server.Mails);
        Assert.Equal(["<demo-person@demo.invalid>"], mail.RcptTo);
        var all = string.Join('\n', t.Host.Logs.Lines);
        Assert.DoesNotContain("demo-person@demo.invalid", all);
        Assert.DoesNotContain(Password, all);
    }

    [Fact]
    public async Task The_message_the_job_sends_is_the_polish_template_with_a_link_and_no_brief()
    {
        using var server = new FakeSmtpServer();
        using var t = new NotificationHost(setup: h => { Smtp(h, server); h.Settings["Site:BaseUrl"] = "https://gabinety.demo.invalid"; }, keepSender: true);
        t.Contacts.Addresses["demo-client"] = "demo-person@demo.invalid";
        var booking = await t.AddBooking(edit: b => b.BriefText = "MARKER-BRIEF-ZAŻÓŁĆ");
        t.Add(booking.Id, NotificationKind.Reminder24h, "demo-client");
        Assert.Equal(1, await t.Run());

        using var ms = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(Assert.Single(server.Mails).Data));
        var parsed = MimeKit.MimeMessage.Load(ms);
        Assert.Equal("Przypomnienie: konsultacja już jutro", parsed.Subject);
        Assert.Contains($"https://gabinety.demo.invalid/moje/rezerwacje/{booking.Id}", parsed.TextBody);
        Assert.DoesNotContain("MARKER-BRIEF", parsed.TextBody + parsed.HtmlBody);
    }
}
