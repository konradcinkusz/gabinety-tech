using System.Reflection;

namespace Gabinety.Architecture.Tests;

public class GuardTests
{
    static string Root()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "GabinetyTech.sln"))) d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("solution root not found");
    }

    [Fact]
    public void Only_Llm_references_the_Copilot_SDK()
    {
        var offenders = System.IO.Directory.EnumerateFiles(Root(), "*.csproj", SearchOption.AllDirectories)
            .Where(f => !f.Contains("/obj/") && !f.Contains("/bin/"))
            .Where(f => File.ReadAllText(f).Contains("GitHub.Copilot.SDK"))
            .Select(Path.GetFileName).ToList();
        Assert.Equal(new[] { "Gabinety.Llm.csproj" }, offenders);
    }

    [Fact]
    public void Kernel_is_at_most_400_lines_of_csharp()
    {
        var lines = System.IO.Directory.EnumerateFiles(Path.Combine(Root(), "src/Gabinety.ServiceDefaults"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains("/obj/") && !f.Contains("/bin/")).Sum(f => File.ReadAllLines(f).Length);
        Assert.InRange(lines, 1, 400);
    }

    [Fact]
    public void Kernel_references_no_domain_project()
    {
        var refs = typeof(Gabinety.ServiceDefaults.Extensions).Assembly.GetReferencedAssemblies()
            .Select(a => a.Name!).Where(n => n.StartsWith("Gabinety.")).ToList();
        Assert.Empty(refs);
    }

    [Fact]
    public void Contracts_references_nothing_but_the_framework()
    {
        var refs = typeof(Gabinety.Contracts.Common.MoneyDto).Assembly.GetReferencedAssemblies()
            .Select(a => a.Name!).Where(n => !n.StartsWith("System.") && n != "mscorlib" && n != "netstandard").ToList();
        Assert.Empty(refs);
    }

    [Fact]
    public void Time_is_read_from_TimeProvider_not_the_clock()
    {
        var bad = System.IO.Directory.EnumerateFiles(Path.Combine(Root(), "src"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains("/obj/") && !f.Contains("/bin/") && !f.Contains("AppHost"))
            .Where(f => { var t = File.ReadAllText(f); return t.Contains("DateTime.Now") || t.Contains("DateTime.UtcNow") || t.Contains("DateTimeOffset.UtcNow"); })
            .ToList();
        Assert.Empty(bad);
    }
}
