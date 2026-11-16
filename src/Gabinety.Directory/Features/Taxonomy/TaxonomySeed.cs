using System.Text.Json;

namespace Gabinety.Directory.Features.Taxonomy;

public sealed record SeedCategory(string Slug, string Kind, string NamePl, int Sort);
public sealed record TaxonomySeed(string Version, IReadOnlyList<SeedCategory> Categories);

/// <summary>Where the seed set comes from; tests supply a modified one.</summary>
public interface ITaxonomySeedSource
{
    TaxonomySeed Load();
}

public sealed class EmbeddedTaxonomySeedSource : ITaxonomySeedSource
{
    const string Resource = "Persistence.Seeds.taxonomy.json";
    static readonly JsonSerializerOptions J = new(JsonSerializerDefaults.Web);

    public TaxonomySeed Load()
    {
        var asm = typeof(EmbeddedTaxonomySeedSource).Assembly;
        var name = asm.GetManifestResourceNames().Single(n => n.EndsWith(Resource, StringComparison.Ordinal));
        using var s = asm.GetManifestResourceStream(name)!;
        return JsonSerializer.Deserialize<TaxonomySeed>(s, J) ?? throw new InvalidOperationException("taxonomy.json is empty");
    }
}
