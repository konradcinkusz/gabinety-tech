namespace Gabinety.Llm;

/// <summary>Configuration section <c>Copilot</c> (COPILOT-SDK §6). No value here is ever logged.</summary>
public sealed class CopilotOptions
{
    /// <summary>Path to a Copilot CLI executable. Null uses the runtime bundled with the SDK.</summary>
    public string? CliPath { get; set; }

    /// <summary>URL of an already running Copilot runtime (<c>port</c>, <c>host:port</c> or a full URL). Takes precedence over <see cref="CliPath"/>.</summary>
    public string? CliUrl { get; set; }

    /// <summary>Optional GitHub token (SECRET). When set it replaces the logged-in user. Never logged, never in a message.</summary>
    public string? GitHubToken { get; set; }

    /// <summary>How long the runtime may take to start, authenticate and list models before the gateway reports Unavailable.</summary>
    public TimeSpan StartupTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Never prints the token or the paths; says only which keys are set.</summary>
    public override string ToString() =>
        $"Copilot (CliPath set: {CliPath is not null}, CliUrl set: {CliUrl is not null}, GitHubToken set: {GitHubToken is not null}, StartupTimeout {StartupTimeout})";
}
