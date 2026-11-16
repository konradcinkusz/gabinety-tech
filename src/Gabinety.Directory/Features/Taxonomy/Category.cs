using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Gabinety.Directory.Features.Taxonomy;

public enum CategoryKind { Problem, Tech }

/// <summary>Row of `categories` (CONTRACTS 3.1). Rows are never deleted: a slug that leaves the seed is deactivated.</summary>
public sealed class Category
{
    public string Slug { get; set; } = "";
    public CategoryKind Kind { get; set; }
    public string NamePl { get; set; } = "";
    public int Sort { get; set; }
    public bool IsActive { get; set; }
}

/// <summary>The single row of `taxonomy_meta`: the version of the seed set that was last applied. `id` is always 1; a check constraint keeps it one row.</summary>
public sealed class TaxonomyMeta
{
    public const int SingleId = 1;
    public int Id { get; set; } = SingleId;
    public string Version { get; set; } = "";
}

public sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> b)
    {
        b.ToTable("categories");
        b.HasKey(x => x.Slug);
        b.Property(x => x.Slug).HasColumnName("slug").HasMaxLength(60).ValueGeneratedNever();
        b.Property(x => x.Kind).HasColumnName("kind").HasMaxLength(20).IsRequired()
            .HasConversion(k => k == CategoryKind.Problem ? "problem" : "tech", s => s == "problem" ? CategoryKind.Problem : CategoryKind.Tech);
        b.Property(x => x.NamePl).HasColumnName("name_pl").HasMaxLength(80).IsRequired();
        b.Property(x => x.Sort).HasColumnName("sort");
        b.Property(x => x.IsActive).HasColumnName("is_active");
        b.HasIndex(x => new { x.Kind, x.IsActive, x.Sort });
    }
}

public sealed class TaxonomyMetaConfiguration : IEntityTypeConfiguration<TaxonomyMeta>
{
    public void Configure(EntityTypeBuilder<TaxonomyMeta> b)
    {
        b.ToTable("taxonomy_meta", t => t.HasCheckConstraint("ck_taxonomy_meta_single_row", "id = 1"));
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(x => x.Version).HasColumnName("version").HasMaxLength(20).IsRequired();
    }
}
