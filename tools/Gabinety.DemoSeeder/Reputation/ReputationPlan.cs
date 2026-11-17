namespace Gabinety.DemoSeeder;

/// <summary>
/// One demo review. <see cref="Key"/> is its stable name (starts with demo-); <see cref="BookingKey"/> names the Completed demo booking it is written on
/// (the booking step's <c>demo-booking-completed-N</c>). The reviewer is whoever made that booking and the practitioner is whoever it was with, so neither is repeated here.
/// </summary>
public sealed record DemoReview(string Key, string BookingKey, int Rating, string Text);

public static class ReputationPlan
{
    /// <summary>
    /// Three reviews, one per Completed demo booking, with three different ratings. Texts are Polish, 20 to 2000 characters, and trip none of the publication rules
    /// (CONTRACTS 5.3: no link, no contact detail, no long digit run, no repetition), so each is Published. They carry no machine name: a visitor reads them.
    /// </summary>
    public static readonly DemoReview[] All =
    [
        new("demo-review-1", "demo-booking-completed-1", 5,
            "Rzeczowa konsultacja. Dostałem konkretną listę ryzyk w kodzie i kolejność poprawek, od razu wiedziałem, od czego zacząć w zespole."),
        new("demo-review-2", "demo-booking-completed-2", 4,
            "Dobry plan migracji i jasne wyjaśnienia. Zabrakło trochę czasu na pytania o testy, ale i tak warto było."),
        new("demo-review-3", "demo-booking-completed-3", 3,
            "Konsultacja w porządku, choć spodziewałem się więcej szczegółów o granicach modułów. Notatka po spotkaniu pomogła uporządkować decyzje."),
    ];
}
