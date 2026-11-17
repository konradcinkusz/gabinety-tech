namespace Gabinety.DemoSeeder;

/// <summary>A practitioner the Directory step created or found, by its stable demo key (the user name, e.g. demo-practitioner-3).</summary>
public sealed class SeededPractitioner(string key, string userId, Guid id, string slug)
{
    public string Key { get; } = key;
    public string UserId { get; } = userId;
    public Guid Id { get; } = id;
    public string Slug { get; } = slug;
    public string Status { get; set; } = "Draft";
    /// <summary>Offering ids by the offering's stable demo key (its title).</summary>
    public Dictionary<string, Guid> Offerings { get; } = new();
}

/// <summary>Shared state of one run. Later steps read what earlier ones registered; each key is a demo- name, never a database id of another system.</summary>
public sealed class SeedContext(SeedOptions options, ISeedHttp http, ISeedLog log, TimeProvider time)
{
    public SeedOptions Options { get; } = options;
    public ISeedLog Log { get; } = log;
    public ISeedHttp Http { get; } = http;
    public TimeProvider Time { get; } = time;
    public DemoIdentity Identity { get; } = new(http.For(SeedService.Identity), options);

    /// <summary>Practitioners by key. Filled by the Directory step.</summary>
    public Dictionary<string, SeededPractitioner> Practitioners { get; } = new();
    /// <summary>Demo client users by key (demo-client, demo-client-2, ...). Filled by the Directory step.</summary>
    public Dictionary<string, DemoUser> Clients { get; } = new();
}
