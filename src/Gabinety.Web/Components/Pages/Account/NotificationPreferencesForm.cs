using Gabinety.Web.Api;

namespace Gabinety.Web.Components.Pages.Account;

/// <summary>
/// The three switches of the notification section of <c>/konto</c> (CONTRACTS 4.2). Only the switchable kinds are here: confirmations, cancellations, disputes and payouts are always sent.
/// The defaults are <c>false</c> on purpose: static form binding keeps a property's default for a checkbox that is not posted, so an unticked switch must start off.
/// The page loads the real values into the model on its own (<see cref="From"/>) before it renders.
/// </summary>
public sealed class NotificationPreferencesForm
{
    public const string FormName = "notification-preferences";
    public const string Reminder24hKind = "Reminder24h", Reminder1hKind = "Reminder1h", ReviewRequestKind = "ReviewRequest";

    public bool Reminder24h { get; set; }
    public bool Reminder1h { get; set; }
    public bool ReviewRequest { get; set; }

    /// <summary>All three kinds, each one named, so the save sets every switch the form shows.</summary>
    public IReadOnlyList<NotificationPreferenceDto> ToItems() =>
    [
        new(Reminder24hKind, Reminder24h),
        new(Reminder1hKind, Reminder1h),
        new(ReviewRequestKind, ReviewRequest),
    ];

    public static NotificationPreferencesForm From(NotificationPreferencesDto preferences)
    {
        var form = new NotificationPreferencesForm();
        foreach (var item in preferences.Items)
            switch (item.Kind)
            {
                case Reminder24hKind: form.Reminder24h = item.Enabled; break;
                case Reminder1hKind: form.Reminder1h = item.Enabled; break;
                case ReviewRequestKind: form.ReviewRequest = item.Enabled; break;
            }
        return form;
    }
}
