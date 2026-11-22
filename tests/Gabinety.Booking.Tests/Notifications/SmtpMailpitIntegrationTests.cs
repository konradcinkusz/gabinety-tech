using System.Net;
using System.Text.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Gabinety.Booking.Notifications;
using Gabinety.Booking.Notifications.Smtp;
using Microsoft.Extensions.DependencyInjection;

namespace Gabinety.Booking.Tests;

/// <summary>
/// T-04.09c, CI `integration` job (Testcontainers needs Docker): the SMTP path against a REAL SMTP server, Mailpit, asserted through its HTTP API.
/// A booking is created through the API, confirmed through the payment simulation, and the real dispatcher runs with <c>Notifications:Provider=Smtp</c>.
/// Deterministic: one container per test, the message is read back through the API (polled up to 30 s, but SMTP accepts only after the server stored it),
/// no real clock or random value takes part in an assertion.
/// </summary>
[Trait("tier", "integration")]
public class SmtpMailpitIntegrationTests
{
    const string Image = "axllent/mailpit:v1.29.7";
    const string ClientAddress = "demo-klient@demo.invalid", PractitionerAddress = "demo-praktyk@demo.invalid", Client = "demo-client-mailpit";
    const string Secret = "TAJNY-OPIS-PROBLEMU-NIE-DO-MAILA", SmtpPassword = "demo-smtp-password-DO-NOT-LEAK";

    static async Task<IContainer> Mailpit()
    {
        var c = new ContainerBuilder(Image)
            .WithPortBinding(1025, true)
            .WithPortBinding(8025, true)
            .WithEnvironment("MP_SMTP_AUTH_ACCEPT_ANY", "true")      // any login is accepted: the sender's AUTH path runs, the credentials are not checked
            .WithEnvironment("MP_SMTP_AUTH_ALLOW_INSECURE", "true")  // plain-text AUTH without TLS, test listener only
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(8025).ForPath("/livez")))
            .Build();
        await c.StartAsync();
        return c;
    }

    static async Task<List<JsonElement>> Messages(HttpClient api, int expected)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (true)
        {
            using var doc = JsonDocument.Parse(await api.GetStringAsync("/api/v1/messages"));
            var list = doc.RootElement.GetProperty("messages").EnumerateArray().Select(m => m.Clone()).ToList();
            if (list.Count >= expected || DateTime.UtcNow > deadline) return list;
            await Task.Delay(100);
        }
    }

    static async Task<JsonElement> Detail(HttpClient api, JsonElement summary) =>
        JsonDocument.Parse(await api.GetStringAsync($"/api/v1/message/{summary.GetProperty("ID").GetString()}")).RootElement.Clone();

    [Fact]
    public async Task The_confirmation_of_a_created_and_confirmed_booking_arrives_at_both_parties_in_correct_Polish_without_secrets_or_brief_text()
    {
        await using var mailpit = await Mailpit();
        using var api = new HttpClient { BaseAddress = new Uri($"http://{mailpit.Hostname}:{mailpit.GetMappedPublicPort(8025)}") };

        using var host = new CreateHost();
        host.Emails[Client] = ClientAddress;
        var db = "mailpit-" + Guid.NewGuid().ToString("N"); // its own in-memory database: no other test's dispatcher may claim these rows
        host.Configure = s => s.AddDbContext<Gabinety.Booking.Persistence.BookingDbContext>(o => Microsoft.EntityFrameworkCore.InMemoryDbContextOptionsExtensions.UseInMemoryDatabase(o, db));
        host.Settings["Notifications:Provider"] = "Smtp";
        host.Settings["Notifications:Smtp:Host"] = mailpit.Hostname;
        host.Settings["Notifications:Smtp:Port"] = mailpit.GetMappedPublicPort(1025).ToString();
        host.Settings["Notifications:Smtp:TlsMode"] = "None";
        host.Settings["Notifications:Smtp:User"] = "demo-smtp-user";
        host.Settings["Notifications:Smtp:Password"] = SmtpPassword;
        host.Settings["Notifications:Smtp:FromAddress"] = "powiadomienia@demo.invalid";
        var offering = host.Directory.Override!;
        host.Directory.Override = (r, n) => r.RequestUri!.AbsolutePath == $"/internal/v1/practitioners/{host.OwnerUserId}/contact"
            ? FakeDirectory.Json(new { email = PractitionerAddress })
            : offering(r, n);
        Assert.IsType<SmtpEmailSender>(host.Services.GetRequiredService<IEmailSender>());
        Assert.IsType<ContactResolver>(host.Services.GetRequiredService<IContactResolver>());

        await host.WorkWeek();
        var created = await CreateHost.Post(host.ClientFor(Client), host.Req(text: "demo-brief: " + Secret), CreateHost.Key());
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Body()).GetProperty("bookingId").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await Pay.Simulate(host.ClientFor(Client), id, "succeeded")).StatusCode);

        Assert.Equal(2, await host.Services.GetRequiredService<NotificationDispatcher>().RunOnceAsync(default));

        var messages = await Messages(api, 2);
        Assert.Equal(2, messages.Count);
        string Recipient(JsonElement m) => m.GetProperty("To").EnumerateArray().Single().GetProperty("Address").GetString()!;
        Assert.Equal(new[] { ClientAddress, PractitionerAddress }.Order(), messages.Select(Recipient).Order());

        var toClient = await Detail(api, messages.Single(m => Recipient(m) == ClientAddress));
        var toPractitioner = await Detail(api, messages.Single(m => Recipient(m) == PractitionerAddress));
        Assert.Equal("Rezerwacja potwierdzona", toClient.GetProperty("Subject").GetString());
        Assert.Equal("Masz nową rezerwację", toPractitioner.GetProperty("Subject").GetString()); // ą and ę survive the wire
        Assert.Contains("Twoja rezerwacja została potwierdzona.", toClient.GetProperty("Text").GetString());
        Assert.Contains("Masz nową rezerwację.", toPractitioner.GetProperty("Text").GetString());
        Assert.Contains("Szczegóły rezerwacji", toClient.GetProperty("HTML").GetString());
        Assert.DoesNotContain('�', toClient.GetProperty("Text").GetString()!); // no replacement character: the encoding is right end to end
        Assert.Equal("powiadomienia@demo.invalid", toClient.GetProperty("From").GetProperty("Address").GetString());

        foreach (var m in new[] { toClient, toPractitioner })
        {
            var all = m.GetRawText();
            Assert.DoesNotContain(Secret, all);
            Assert.DoesNotContain("demo-brief", all);
            Assert.DoesNotContain(SmtpPassword, all);
            // Mailpit's own metadata ("Username") records the SMTP login name: that is the server's record of the session, not the message. The message carries none of it.
            Assert.DoesNotContain("demo-smtp-user", m.GetProperty("Subject").GetString() + m.GetProperty("Text").GetString() + m.GetProperty("HTML").GetString());
            Assert.Contains($"/moje/rezerwacje/{id}", m.GetProperty("Text").GetString()); // the link to the page, nothing from the brief
        }

        Assert.All(host.Logs.Lines, l =>
        {
            Assert.DoesNotContain(ClientAddress, l); Assert.DoesNotContain(PractitionerAddress, l);
            Assert.DoesNotContain(SmtpPassword, l); Assert.DoesNotContain(Secret, l);
        });
    }
}
