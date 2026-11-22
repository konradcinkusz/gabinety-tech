namespace Gabinety.Web.Api;

/// <summary>One consent as the account page shows it: the version accepted (or, when pending, the version now required). The version is never printed as primary text (DESIGN-CONTRACT 8): it goes to a <c>title</c>.</summary>
public sealed record ConsentDto(bool Accepted, string Version);

/// <summary>What the account page may show. Built field by field from the identity service's answers (<see cref="AccountClient"/>), so a token or any other field can never ride along.</summary>
public sealed record AccountDto(string Email, bool EmailConfirmed, bool HasPassword, ConsentDto Terms, ConsentDto Privacy)
{
    public bool ConsentPending => !Terms.Accepted || !Privacy.Accepted;
}

public enum DeleteOutcome { Deleted, Rejected, Unavailable, Unauthenticated }
