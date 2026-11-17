namespace Gabinety.DemoSeeder;

/// <summary>CONTRACTS 10.4: every id, slug and user of demo data starts with <c>demo-</c>, because the reset removes by that prefix.</summary>
public static class DemoNames
{
    public const string Prefix = "demo-";

    public static bool IsDemo(string? value) => value is not null && value.StartsWith(Prefix, StringComparison.Ordinal);

    public static string Require(string value) =>
        IsDemo(value) ? value : throw new InvalidOperationException($"'{value}' is outside the {Prefix} namespace; the seeder never writes such a name.");

    public static string Email(string userName) => $"{Require(userName)}@demo.example"; // the DevIdentity domain (tools/Gabinety.DevIdentity/README.md)
}
