using Gabinety.Booking.Notifications.Smtp;
using Gabinety.ServiceDefaults;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Gabinety.Booking.Notifications;

/// <summary>
/// Registers the notification foundation. <c>Notifications:Provider</c> is <c>Log</c> (the default) or <c>Smtp</c> (CONTRACTS 10.3); any other value stops the host at
/// startup. <c>Smtp</c> with a missing or invalid <c>Notifications:Smtp:*</c> configuration does NOT stop it: the log sender is used and the <c>email</c> health check
/// reports <c>Degraded</c> (P8, SPEC 5 rule 7); notifications stay in the table and the log sender marks them sent, so fix the configuration before real use.
/// </summary>
public sealed class NotificationsFeature : IServiceFeature
{
    public void Add(IHostApplicationBuilder builder)
    {
        var provider = builder.Configuration["Notifications:Provider"];
        var smtp = string.Equals(provider, "Smtp", StringComparison.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(provider) && !smtp && !string.Equals(provider, "Log", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Notifications:Provider must be 'Log' or 'Smtp'.");

        if (smtp)
        {
            if (SmtpOptions.TryRead(builder.Configuration, out var options, out var problem))
            {
                builder.Services.AddSingleton(options!);
                builder.Services.AddSingleton<IEmailSender, SmtpEmailSender>();
                builder.Services.AddSingleton(new EmailProviderState(true, "smtp"));
            }
            else
            {
                builder.Services.AddSingleton<IEmailSender, LogEmailSender>();
                builder.Services.AddSingleton(new EmailProviderState(false, "SMTP not configured, using the log sender: " + problem));
                builder.Services.AddHostedService(sp => new EmailDegradedBanner(sp.GetRequiredService<EmailProviderState>(), sp.GetRequiredService<ILogger<EmailDegradedBanner>>()));
            }
        }
        else
        {
            builder.Services.AddSingleton<IEmailSender, LogEmailSender>();
            // Outside the fake profile the log sender is never silent: it marks notifications as sent without delivering them, so say so (one warning, a Degraded "email" check).
            if (!NonDeployedProfile(builder.Configuration))
            {
                builder.Services.AddSingleton(new EmailProviderState(false, "Notifications:Provider is Log: e-mail is NOT delivered, it is only logged (set Notifications:Provider=Smtp with Notifications:Smtp:Host and Notifications:Smtp:FromAddress)"));
                builder.Services.AddHostedService(sp => new EmailDegradedBanner(sp.GetRequiredService<EmailProviderState>(), sp.GetRequiredService<ILogger<EmailDegradedBanner>>()));
            }
        }
        if (smtp || !NonDeployedProfile(builder.Configuration)) builder.Services.AddHealthChecks().AddCheck<EmailHealthCheck>("email");

        // The practitioner's address is a Directory fact, so the real resolver needs Backend:Directory (the fake profile sets it too, and delivers to the log sender).
        if (!string.IsNullOrWhiteSpace(builder.Configuration["Backend:Directory"])) builder.Services.TryAddSingleton<IContactResolver, ContactResolver>();
        else builder.Services.TryAddSingleton<IContactResolver, NullContactResolver>();
        builder.Services.AddScoped<INotificationWriter, NotificationWriter>();
        builder.Services.AddSingleton<NotificationDispatcher>();
        builder.Services.AddHostedService<NotificationDispatcherHostedService>();
    }

    public void Map(IEndpointRouteBuilder endpoints) { }

    /// <summary>The fake or developer profile (CONTRACTS 10.4): the only place where the log sender is the expected one.</summary>
    static bool NonDeployedProfile(IConfiguration config) =>
        config["GABINETY_FAKE"] == "1" || string.Equals(config["Environment:Kind"], "Development", StringComparison.OrdinalIgnoreCase);
}

/// <summary>The startup banner line of SPEC 5 rule 7 for the e-mail dependency: one warning naming the keys at fault.</summary>
sealed class EmailDegradedBanner(EmailProviderState state, ILogger<EmailDegradedBanner> log) : IHostedService
{
    public Task StartAsync(CancellationToken ct) { log.LogWarning("Dependency email is Degraded: {Detail}", state.Detail); return Task.CompletedTask; }
    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
