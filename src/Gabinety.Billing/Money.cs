namespace Gabinety.Billing;

/// <summary>Money as integer minor units (grosz) plus a currency. Checked arithmetic; no operators, so currencies cannot mix silently.</summary>
public readonly record struct Money(long Minor, string Currency)
{
    public static Money Pln(long minor) => new(minor, "PLN");

    public Money Plus(Money other)
    {
        RequireSameCurrency(other);
        return this with { Minor = checked(Minor + other.Minor) };
    }

    public Money Minus(Money other)
    {
        RequireSameCurrency(other);
        return this with { Minor = checked(Minor - other.Minor) };
    }

    private void RequireSameCurrency(Money other)
    {
        if (!string.Equals(Currency, other.Currency, StringComparison.Ordinal))
            throw new InvalidOperationException("Money in different currencies cannot be combined.");
    }
}
