namespace Gabinety.Web.Api;

/// <summary>The logical names of the backends. A typed client's base address is <c>https+http://&lt;name&gt;</c>, resolved by service discovery.</summary>
public static class Backend
{
    public const string Directory = "directory", Booking = "booking", Reputation = "reputation", Assistant = "assistant", Auth = "auth";
    public static string BaseAddress(string name) => $"https+http://{name}";
}
