namespace Gabinety.Web.Tests;

/// <summary>Structural facts are read from disk, so a test pins them even where a rendered component cannot show them (BLAZOR-WEB-APP section 13).</summary>
static class Repo
{
    public static readonly string Root = FindRoot();
    public static readonly string Web = Path.Combine(Root, "src", "Gabinety.Web");

    static string FindRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "GabinetyTech.sln"))) d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("solution root (GabinetyTech.sln) not found");
    }

    public static string Read(params string[] parts) => File.ReadAllText(Path.Combine([Web, .. parts]));

    static bool Built(string f) => f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") || f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}");

    public static IEnumerable<string> WebFiles(string pattern) => Directory.EnumerateFiles(Web, pattern, SearchOption.AllDirectories).Where(f => !Built(f)).Order();

    public static string Rel(string f) => Path.GetRelativePath(Web, f).Replace('\\', '/');
}
