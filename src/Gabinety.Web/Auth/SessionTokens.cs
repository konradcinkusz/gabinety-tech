namespace Gabinety.Web.Auth;

/// <summary>The identity service's tokens of one signed-in user. They live in server memory (<see cref="ServerSessionStore"/>) and nowhere else: not in the cookie, not in a component field, not in persisted component state.</summary>
public sealed record SessionTokens(string AccessToken, string RefreshToken, DateTimeOffset AccessExpiresAt);
