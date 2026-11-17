using Gabinety.ServiceDefaults;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Gabinety.Booking.Notifications;

/// <summary>Registers the notification foundation. <c>Notifications:Provider</c> is <c>Log</c> (the default); any other value stops the host at startup.</summary>
public sealed class NotificationsFeature : IServiceFeature
{
    public void Add(IHostApplicationBuilder builder)
    {
        var provider = builder.Configuration["Notifications:Provider"];
        if (!string.IsNullOrWhiteSpace(provider) && !string.Equals(provider, "Log", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Notifications:Provider must be 'Log' (the only provider registered so far).");

        builder.Services.AddSingleton<IEmailSender, LogEmailSender>();
        builder.Services.TryAddSingleton<IContactResolver, NullContactResolver>();
        builder.Services.AddScoped<INotificationWriter, NotificationWriter>();
        builder.Services.AddSingleton<NotificationDispatcher>();
        builder.Services.AddHostedService<NotificationDispatcherHostedService>();
    }

    public void Map(IEndpointRouteBuilder endpoints) { }
}
