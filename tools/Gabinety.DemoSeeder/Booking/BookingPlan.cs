using System.Security.Cryptography;
using System.Text;

namespace Gabinety.DemoSeeder;

/// <summary>The state a demo booking is brought to. Names are the API's booking statuses.</summary>
public enum BookingTarget { Confirmed, PendingPayment, Expired, CancelledByClient, LateCancelledByClient, Completed, ClientNoShow, Disputed, Resolved }

/// <summary>
/// One demo booking. <see cref="Key"/> is its stable name (starts with demo-); it is written into the brief as <c>[key] ...</c>, which is how a later run finds the
/// booking again, and the Idempotency-Key derives from it. Practitioner and client are indexes (wrapping) into the Approved practitioners and the demo clients.
/// <see cref="Days"/> and <see cref="Hour"/>/<see cref="Minute"/> ask for the first offered slot on or after that Warsaw day and time, counted from the target system's "now".
/// </summary>
public sealed record DemoBooking(string Key, BookingTarget Target, int Practitioner, int Client, int Days, int Hour, int Minute = 0)
{
    // the text is picked by a stable sum (string.GetHashCode is randomised per process)
    static readonly string[] BriefTexts =
    [
        "Chcę omówić architekturę modułu płatności i kolejność refaktoryzacji.",
        "Potrzebuję przeglądu pull requesta i wskazówek do testów integracyjnych.",
        "Proszę o pomoc w planie migracji aplikacji do nowszej wersji .NET.",
        "Szukam wsparcia przy analizie wolnych zapytań w bazie danych.",
    ];

    public string BriefText => $"[{DemoNames.Require(Key)}] {BriefTexts[Stable(Key) % BriefTexts.Length]}";

    static int Stable(string s) => s.Aggregate(17, (h, c) => unchecked(h * 31 + c)) & 0x7fffffff;

    /// <summary>The Idempotency-Key of the create call: the first 16 bytes of SHA-256 over a fixed prefix and the demo key. The same demo booking always sends the same key.</summary>
    public Guid IdempotencyKey => new(SHA256.HashData(Encoding.UTF8.GetBytes("gabinety-demo-booking:" + Key))[..16]);
}

public static class BookingPlan
{
    /// <summary>
    /// Twelve bookings, one or more per state. The ones that need time to pass start 2 to 3 days out, the ones that stay as they are start two weeks or more out,
    /// so moving the clock forward for the first never reaches the second (no job may complete them on the way). Bookings that stay PendingPayment are created
    /// last, after the clock is back, so their hold is live when the run ends.
    /// </summary>
    public static readonly DemoBooking[] All =
    [
        new("demo-booking-confirmed-1", BookingTarget.Confirmed, 0, 0, 14, 10),
        new("demo-booking-confirmed-2", BookingTarget.Confirmed, 1, 1, 15, 11),
        new("demo-booking-cancelled-1", BookingTarget.CancelledByClient, 2, 0, 16, 10),
        new("demo-booking-expired-1", BookingTarget.Expired, 5, 2, 17, 10),
        new("demo-booking-latecancelled-1", BookingTarget.LateCancelledByClient, 3, 2, 3, 14),
        new("demo-booking-completed-1", BookingTarget.Completed, 0, 1, 2, 10),
        new("demo-booking-completed-2", BookingTarget.Completed, 1, 2, 2, 12),
        new("demo-booking-completed-3", BookingTarget.Completed, 4, 0, 2, 14),
        new("demo-booking-noshow-1", BookingTarget.ClientNoShow, 2, 3, 2, 16),
        new("demo-booking-disputed-1", BookingTarget.Disputed, 3, 1, 2, 11),
        new("demo-booking-resolved-1", BookingTarget.Resolved, 4, 3, 2, 9, 30),
        new("demo-booking-pending-1", BookingTarget.PendingPayment, 5, 1, 18, 10),
    ];

    /// <summary>Mon to Fri in Europe/Warsaw, on :00 and :30, three shapes so the practitioners are not identical.</summary>
    public static IReadOnlyList<(int Day, string Start, string End)> Availability(int index) => (index % 3) switch
    {
        0 => Enumerable.Range(1, 5).Select(d => (d, "09:00", "17:00")).ToList(),
        1 => Enumerable.Range(1, 5).Select(d => (d, "10:00", "18:00")).ToList(),
        _ => new (int, string, string)[] { (1, "09:30", "13:30"), (1, "15:00", "18:30"), (2, "12:00", "20:00"), (3, "09:30", "13:30"), (3, "15:00", "18:30"), (4, "12:00", "20:00"), (5, "09:30", "13:30"), (5, "15:00", "18:30") },
    };
}
