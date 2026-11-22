using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Gabinety.Contracts.Assistant;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AssistantState { Ready, Fake, NotAuthenticated, Unavailable, Disabled }

[JsonConverter(typeof(Gabinety.Contracts.Common.SnakeCaseLowerEnumConverter))]
public enum Confidence { Low, Medium, High }

[JsonConverter(typeof(Gabinety.Contracts.Common.SnakeCaseLowerEnumConverter))]
public enum TriageVerdict { None, Hold }

[JsonConverter(typeof(Gabinety.Contracts.Common.SnakeCaseLowerEnumConverter))]
public enum TriageReason { Spam, Abuse, PersonalData, OffTopic, Threat }

public sealed record AssistantStatus(AssistantState State, IReadOnlyList<string> Features);

public sealed record BriefRequest([property: Required, StringLength(2000, MinimumLength = 20)] string Text);

public sealed record BriefSuggestion(string ProblemSlug, IReadOnlyList<string> TechSlugs, string Summary,
    IReadOnlyList<string> ClarifyingQuestions, int SuggestedDurationMinutes, Confidence Confidence, string ModelId, string PromptVersion);

/// <summary>Input of review triage: no ids (CONTRACTS 6).</summary>
public sealed record TriageRequest(int Rating, string Text);
public sealed record TriageResponse(TriageVerdict Flag, IReadOnlyList<TriageReason> Reasons, string ModelId);
