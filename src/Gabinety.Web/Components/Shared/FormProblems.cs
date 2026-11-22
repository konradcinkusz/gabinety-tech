namespace Gabinety.Web.Components.Shared;

/// <summary>One validation problem: the id of the control it belongs to (the summary links to it) and the Polish text that says what to do.</summary>
public sealed record FormProblem(string FieldId, string Message);

/// <summary>
/// All the problems of one submit at once (DESIGN-CONTRACT 5.5, audit F-06): never "the first one". A field has at most one problem; <see cref="For"/> finds it for the inline error and for
/// <c>aria-invalid</c>, <see cref="Heading"/> is "Popraw N pól" for the summary.
/// </summary>
public sealed class FormProblems
{
    readonly List<FormProblem> _items = [];

    public IReadOnlyList<FormProblem> Items => _items;
    public int Count => _items.Count;
    public bool Any => _items.Count > 0;

    public void Add(string fieldId, string message)
    {
        if (_items.All(p => p.FieldId != fieldId)) _items.Add(new FormProblem(fieldId, message));
    }

    public FormProblem? For(string fieldId) => _items.FirstOrDefault(p => p.FieldId == fieldId);

    public string Heading => Gabinety.Web.Text.Pl.FormErrors.Summary(Count);
}
