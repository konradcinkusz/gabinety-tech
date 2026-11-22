using Gabinety.Web.Api;
using Gabinety.Web.Auth;
using Gabinety.Web.Components.Pages.PractitionerArea;
using Gabinety.Web.Components.Pages.Shared;
using Gabinety.Web.Text;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;

namespace Gabinety.Web.Components.Pages.Proposals;

/// <summary>
/// The state and the handler of the client's proposal form (ADR proposal 0013). Static server rendering: the posted fields are read from the request, our own check runs first so every problem is shown at
/// once, the service is the authority, and a success is a redirect to the new proposal (post, redirect, get: a refresh never sends it twice). The same Idempotency-Key is kept for the same body.
/// </summary>
public abstract class ProposalFormBase : ComponentBase
{
    [Inject] protected DirectoryClient DirectoryApi { get; set; } = default!;
    [Inject] protected ProposalsClient ProposalsApi { get; set; } = default!;
    [Inject] protected NavigationManager Nav { get; set; } = default!;
    [Inject] protected TimeProvider Clock { get; set; } = default!;
    [CascadingParameter] protected HttpContext Http { get; set; } = default!;

    [Parameter] public string Slug { get; set; } = "";
    [SupplyParameterFromQuery(Name = "kind")] public string? KindParam { get; set; }

    protected PractitionerProfileDto? _profile;
    protected bool _unavailable, _long = true;
    protected ApiFailure? _failure;
    protected TermsFields _fields = TermsFields.Empty;
    protected string _text = "", _company = "";
    protected string? _attemptKey, _attemptFingerprint;
    protected readonly Dictionary<string, string> _errors = new(StringComparer.Ordinal);

    protected DateOnly Today => PlFormat.TodayWarsaw(Clock.GetUtcNow());
    protected string CurrentPath => $"/propozycja/{Uri.EscapeDataString(Slug)}";
    protected FormValues Posted => Http.Request.HasFormContentType ? new FormValues(Http.Request.Form) : FormValues.Empty;
    protected string? Error(string field) => _errors.GetValueOrDefault(field);
    protected string KindHref(bool longTerm) => CurrentPath + (longTerm ? "" : "?kind=short");

    /// <summary>The errors in the order of the form, each pointing at its field's id.</summary>
    protected IReadOnlyList<KeyValuePair<string, string>> Summary =>
        [.. new[] { "rate", "hoursPerWeek", "totalHours", "durationMonths", "startDate", "text", "companyName" }.Where(_errors.ContainsKey).Select(f => KeyValuePair.Create(f, _errors[f]))];

    protected override async Task OnInitializedAsync()
    {
        _long = KindParam != "short";
        var result = await DirectoryApi.ProfileAsync(Slug);
        if (result.IsOk) { _profile = result.Value; return; }
        if (result.Failure!.Status == 404) { Nav.NotFound(); return; }
        _unavailable = true;
    }

    protected async Task SubmitAsync()
    {
        if (_profile is not { } profile) return;
        var f = Posted;
        _long = f.Text("kind") != "short";
        _fields = new TermsFields(f.Text("rate"), f.Text(ProposalInputs.HoursField(_long)), f.Text("durationMonths"), f.Text("startDate"));
        (_text, _company) = (f.Text("text"), f.Text("companyName"));
        _failure = null;
        _errors.Clear();

        var (terms, errors) = ProposalInputs.ParseTerms(_long, _fields, Today);
        foreach (var (field, message) in errors) _errors[field] = message;
        if (ProposalInputs.TextProblem(_text, required: true) is { } textProblem) _errors["text"] = textProblem;
        if (ProposalInputs.CompanyProblem(_company) is { } companyProblem) _errors["companyName"] = companyProblem;
        if (_errors.Count > 0 || terms is null) return;

        var kind = _long ? ProposalFormat.LongTerm : ProposalFormat.ShortTerm;
        var company = _company.Length == 0 ? null : _company;
        var fingerprint = ProposalAttempt.Fingerprint(profile.Id, kind, terms, _text, company);
        var key = ProposalAttempt.KeyFor(f.Get("attemptKey"), f.Get("attemptFingerprint"), fingerprint, Guid.NewGuid);
        (_attemptKey, _attemptFingerprint) = (key.ToString("D"), fingerprint);

        var result = await ProposalsApi.CreateAsync(key, new CreateProposalBody(profile.Id, kind, terms, _text, company));
        if (result.IsOk) { Nav.NavigateTo($"/moje/propozycje/{result.Value!.Id}?done=sent"); return; }
        if (result.Failure!.IsUnauthenticated) { Nav.NavigateTo(ReturnTarget.LoginUrl(ReturnTarget.Safe(CurrentPath))); return; }
        _failure = result.Failure;
        foreach (var (field, message) in ProposalFormat.FieldErrors(result.Failure, _long)) _errors[field] = message;
        if (result.Failure.Code == "contact_details_not_allowed")
            foreach (var field in ProposalInputs.FreeTextFields(_company.Length > 0)) _errors[field] = ProposalsText.Errors.Contact;
    }
}
