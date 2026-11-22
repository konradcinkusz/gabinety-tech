namespace Gabinety.Web.Components.Pages.Booking;

/// <summary>What the booking form posts. Strings only: the page validates and decides; nothing here is trusted. The brief is user free text: it lives in this object for the request and goes to the Booking service as a body, never to a log.</summary>
public sealed class BookingFormModel
{
    public const string Assist = "assist", Apply = "apply", Pay = "pay";

    /// <summary>Which submit button was pressed: <see cref="Assist"/>, <see cref="Apply"/> or <see cref="Pay"/>.</summary>
    public string? Intent { get; set; }
    public string? Brief { get; set; }
    /// <summary>The assistant's proposal as the visitor left it (editable).</summary>
    public string? Proposal { get; set; }
    /// <summary>The problem slug the assistant suggested, carried between the request that showed the proposal and the one that applies it.</summary>
    public string? SuggestedSlug { get; set; }
    /// <summary>The problem slug of an applied suggestion; empty when none was used (then the booking is not AI assisted).</summary>
    public string? AppliedSlug { get; set; }
    public string? AttemptKey { get; set; }
    public string? AttemptFingerprint { get; set; }
}
