using Gabinety.Web.Components.Shared;
using Gabinety.Web.Text;

namespace Gabinety.Web.Components.Pages.Admin;

public enum ResolveStep { Edit, Confirm }

/// <summary>
/// The state and rules of resolving one dispute (CONTRACTS 4.3, 9.2): a choice, an amount for a partial refund, a note of 10 to 500 characters. Plain C#: the page posts the fields, this class
/// validates them and words the consequence, and the same instance is rebuilt from the posted fields at the confirm step (nothing is trusted from the first step).
/// </summary>
public sealed class DisputeResolveForm
{
    public const string Release = "release", RefundFull = "refund_full", RefundPartial = "refund_partial";
    public static readonly string[] Resolutions = [Release, RefundFull, RefundPartial];

    public string? Resolution { get; init; }
    public string Amount { get; init; } = "";
    public string Note { get; init; } = "";
    /// <summary>The booking's price in grosz when the page could read it; null: the server decides the upper bound.</summary>
    public long? PriceGrosz { get; init; }

    public static DisputeResolveForm From(string? resolution, string? amount, string? note, long? price) =>
        new() { Resolution = Resolutions.Contains(resolution) ? resolution : null, Amount = amount?.Trim() ?? "", Note = note ?? "", PriceGrosz = price };

    public bool IsPartial => Resolution == RefundPartial;

    /// <summary>The refund in grosz (0 for a release, the price for a full refund when known, the typed amount for a partial one).</summary>
    public long? RefundGrosz => Resolution switch { RefundFull => PriceGrosz, RefundPartial when PlnAmount.Parse(Amount) is { Problem: AmountProblem.None } a => a.Grosz, _ => 0 };

    public sealed record Problem(string Field, string Message);

    /// <summary>Every problem of the form, in field order. Empty when the form may go to the confirm step.</summary>
    public IReadOnlyList<Problem> Validate()
    {
        var problems = new List<Problem>();
        if (Resolution is null) problems.Add(new("resolution", PlAdmin.Disputes.Pick));
        if (IsPartial)
        {
            var (problem, grosz) = PlnAmount.Parse(Amount);
            if (problem == AmountProblem.Format) problems.Add(new("amount", PlAdmin.Disputes.AmountFormat));
            else if (problem == AmountProblem.Decimals) problems.Add(new("amount", PlAdmin.Disputes.AmountDecimals));
            else if (PlnAmount.Range(grosz, PriceGrosz) == AmountProblem.Range)
                problems.Add(new("amount", PriceGrosz is { } price ? string.Format(PlAdmin.Disputes.AmountRangeWith, AdminFormat.Pln(price)) : PlAdmin.Disputes.AmountRange));
        }
        if (!TextRule.IsValid(Note)) problems.Add(new("note", PlAdmin.Disputes.NoteInvalid));
        return problems;
    }

    /// <summary>What the money does, in words (CONTRACTS 9.2), for the confirm step. Only called for a valid form.</summary>
    public string Consequence()
    {
        var price = PriceGrosz is { } p ? AdminFormat.Pln(p) : null;
        return Resolution switch
        {
            Release => price is null ? PlAdmin.Disputes.ConfirmReleaseNoPrice : string.Format(PlAdmin.Disputes.ConfirmRelease, price),
            RefundFull => price is null ? PlAdmin.Disputes.ConfirmRefundFullNoPrice : string.Format(PlAdmin.Disputes.ConfirmRefundFull, price),
            RefundPartial when RefundGrosz is { } refund =>
                PriceGrosz is { } total ? string.Format(PlAdmin.Disputes.ConfirmPartial, AdminFormat.Pln(refund), AdminFormat.Pln(total - refund)) : string.Format(PlAdmin.Disputes.ConfirmPartialNoPrice, AdminFormat.Pln(refund)),
            _ => "",
        };
    }

    /// <summary>The refund to send: only a partial refund carries an amount (<c>refundMinor</c>).</summary>
    public long? RefundToSend => IsPartial ? PlnAmount.Parse(Amount).Grosz : null;
}
