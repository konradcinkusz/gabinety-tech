using Gabinety.Web.Api;
using Gabinety.Web.Auth;
using Gabinety.Web.Components.Pages.PractitionerArea;
using Gabinety.Web.Components.Pages.Shared;
using Gabinety.Web.Text;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;

namespace Gabinety.Web.Components.Pages.Proposals;

/// <summary>
/// What the two detail pages of a proposal share (ADR proposal 0013): the client's (<c>/moje/propozycje/{id}</c>) and the programmer's (<c>/gabinet-programisty/propozycje/{id}</c>). Static server rendering with plain
/// forms: the handlers read the posted fields (the round the user saw, the new terms, the note), call the service, and either redirect with a fixed key (<c>?done=</c>, a reload never repeats it) or show what
/// failed where it belongs. Every action is offered by the server's <c>can*</c> flags only. A viewer who is the other party of the proposal is sent to the other page; a stranger gets the answer of an unknown id.
/// The texts of users are request bodies only: never logged, never put in a URL.
/// </summary>
public abstract class ProposalDetailBase : ComponentBase
{
    [Inject] protected ProposalsClient Proposals { get; set; } = default!;
    [Inject] protected NavigationManager Navigation { get; set; } = default!;
    [Inject] protected TimeProvider Time { get; set; } = default!;
    [CascadingParameter] protected HttpContext HttpContext { get; set; } = default!;

    [Parameter] public Guid Id { get; set; }
    [SupplyParameterFromQuery(Name = "action")] public string? ActionParam { get; set; }
    [SupplyParameterFromQuery(Name = "done")] public string? DoneParam { get; set; }

    /// <summary><c>Client</c> or <c>Practitioner</c>: who this page is for (the wire name of the service).</summary>
    protected abstract string ViewerRole { get; }
    protected abstract string PathOf(Guid id);
    protected abstract string OtherPathOf(Guid id);
    protected abstract string ListPath { get; }

    protected ProposalViewDto? Current { get; private set; }
    protected ApiFailure? LoadFailure { get; private set; }
    protected ApiFailure? ActionFailure { get; private set; }
    protected Dictionary<string, string> Errors { get; } = new(StringComparer.Ordinal);
    protected TermsFields CounterFields { get; private set; } = TermsFields.Empty;
    protected string CounterText { get; private set; } = "";

    protected FormValues Posted => HttpContext.Request.HasFormContentType ? new FormValues(HttpContext.Request.Form) : FormValues.Empty;
    protected DateOnly Today => PlFormat.TodayWarsaw(Time.GetUtcNow());
    protected string? Open => ActionParam is "accept" or "counter" or "reject" or "withdraw" ? ActionParam : null;

    protected string? DoneText => DoneParam switch
    {
        "sent" => ProposalsText.Done.Sent, "accepted" => ProposalsText.Done.Accepted, "countered" => ProposalsText.Done.Countered,
        "rejected" => ProposalsText.Done.Rejected, "withdrawn" => ProposalsText.Done.Withdrawn, _ => null,
    };

    protected string DetailHref(string? done = null) => PathOf(Id) + (done is null ? "" : "?done=" + done);

    protected override async Task OnInitializedAsync()
    {
        var r = await Proposals.GetAsync(Id);
        if (r.IsOk)
        {
            if (r.Value!.ViewerRole != ViewerRole) { Navigation.NavigateTo(OtherPathOf(Id)); return; } // the other party of this proposal: their own page
            Current = r.Value;
            CounterFields = Prefill(Current);
            return;
        }
        if (r.Failure!.IsUnauthenticated) { SignIn(); return; }
        if (r.Failure.Status is 404 or 403) { Navigation.NotFound(); return; } // not this user's proposal: the answer of an unknown id
        LoadFailure = r.Failure;
    }

    /// <summary>The counter form starts from the current terms; a start date that has passed is left empty so it is asked for again.</summary>
    TermsFields Prefill(ProposalViewDto p) => new(
        RateInput.ToInput(p.Terms.Rate.AmountMinor), (p.Terms.HoursPerWeek ?? p.Terms.TotalHours)?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "",
        p.Terms.DurationMonths?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "", p.Terms.StartDate >= Today ? p.Terms.StartDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) : "");

    void SignIn() => Navigation.NavigateTo(ReturnTarget.LoginUrl(ReturnTarget.Safe(DetailHref())));

    int PostedRound() => int.TryParse(Posted.Get("round"), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : 0;

    protected Task AcceptAsync() => Current is null ? Task.CompletedTask : RunAsync(() => Proposals.AcceptAsync(Id, PostedRound()), "accepted");

    protected Task RejectAsync() => Current is null ? Task.CompletedTask : RunAsync(() => Proposals.RejectAsync(Id, PostedRound()), "rejected");

    protected Task WithdrawAsync() => Posted.Get("confirm") == "yes" ? RunAsync(() => Proposals.WithdrawAsync(Id), "withdrawn") : Task.CompletedTask;

    protected async Task CounterAsync()
    {
        if (Current is not { } p) return;
        var f = Posted;
        var longTerm = p.Kind == ProposalFormat.LongTerm;
        CounterFields = new TermsFields(f.Text("rate"), f.Text(ProposalInputs.HoursField(longTerm)), f.Text("durationMonths"), f.Text("startDate"));
        CounterText = f.Text("text");
        var (terms, errors) = ProposalInputs.ParseTerms(longTerm, CounterFields, Today);
        if (ProposalInputs.TextProblem(CounterText, required: false) is { } problem) errors["text"] = problem;
        Errors.Clear();
        foreach (var (field, message) in errors) Errors[field] = message;
        if (Errors.Count > 0) return;
        await RunAsync(() => Proposals.CounterAsync(Id, new CounterProposalBody(PostedRound(), terms!, CounterText.Length == 0 ? null : CounterText)), "countered");
    }

    /// <summary>Runs one action: success is a redirect with a fixed key; a failure is kept for display next to the form; a 401 goes to sign-in.</summary>
    async Task RunAsync(Func<Task<ApiResult<ProposalViewDto>>> call, string done)
    {
        var result = await call();
        if (result.IsOk) { Navigation.NavigateTo(DetailHref(done)); return; }
        if (result.Failure!.IsUnauthenticated) { SignIn(); return; }
        ActionFailure = result.Failure;
        foreach (var (field, message) in ProposalFormat.FieldErrors(result.Failure, Current?.Kind == ProposalFormat.LongTerm)) Errors[field] = message;
        if (result.Failure.Code == "contact_details_not_allowed") Errors["text"] = ProposalsText.Errors.Contact;
    }
}
