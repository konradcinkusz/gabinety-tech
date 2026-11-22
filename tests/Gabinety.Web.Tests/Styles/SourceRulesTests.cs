using System.Text.RegularExpressions;

namespace Gabinety.Web.Tests.Stylesheets;

/// <summary>Source-level rules of the web project that a rendered test cannot see.</summary>
public partial class SourceRulesTests
{
    [GeneratedRegex(@"[ąćęłńóśźżĄĆĘŁŃÓŚŹŻ„”]")] private static partial Regex Polish();

    static string WithoutComments(string source, bool razor) =>
        Regex.Replace(razor ? Regex.Replace(source, @"@\*.*?\*@|<!--.*?-->", "", RegexOptions.Singleline) : source, @"(?m)^\s*//.*$|/\*.*?\*/|(?<=\s)//[^\n]*$", "", RegexOptions.Singleline);

    [Fact]
    public void User_facing_Polish_text_is_under_Text_and_nowhere_else()
    {
        // SPEC 5 rule 1: no Polish string literal in a component or a class outside the one file of the surface.
        var offenders = Repo.WebFiles("*.razor").Concat(Repo.WebFiles("*.cs")).Where(f => !Repo.Rel(f).StartsWith("Text/", StringComparison.Ordinal))
            .SelectMany(f => WithoutComments(File.ReadAllText(f), f.EndsWith(".razor")).Split('\n').Select((line, i) => (f, i, line)).Where(t => Polish().IsMatch(t.line)).Select(t => $"{Repo.Rel(t.f)}:{t.i + 1}"))
            .ToList();
        Assert.True(offenders.Count == 0, "Polish text outside Text/: " + string.Join(", ", offenders));
        Assert.Contains("Przejdź do treści", Repo.Read("Text", "Pl.cs")); // the rule looks at the right file
    }

    [Fact]
    public void The_web_project_has_no_package_no_node_toolchain_and_exactly_one_script_file()
    {
        var csproj = Repo.Read("Gabinety.Web.csproj");
        Assert.DoesNotContain("<PackageReference", csproj); // the ASP.NET Core shared framework only (BLAZOR-WEB-APP section 4)
        Assert.Contains("Microsoft.NET.Sdk.Web", csproj);
        Assert.Empty(Repo.WebFiles("package.json"));
        // UX-005 (owner decision D-1): the one first-party script is wwwroot/enhance.js; no other script, no TypeScript, no bundler output
        Assert.Equal(["wwwroot/enhance.js"], Repo.WebFiles("*.js").Select(Repo.Rel).ToList());
        Assert.Empty(Repo.WebFiles("*.ts"));
        Assert.True(new FileInfo(Path.Combine(Repo.Web, "wwwroot", "enhance.js")).Length < 8 * 1024, "enhance.js stays under 8 KB (UX-005)");
        Assert.DoesNotContain(Directory.GetDirectories(Repo.Web), d => Path.GetFileName(d) == "node_modules");
    }

    [Fact]
    public void The_web_project_references_the_kernel_and_its_edge_project_and_nothing_else()
    {
        var refs = Regex.Matches(Repo.Read("Gabinety.Web.csproj"), @"<ProjectReference Include=""([^""]+)""").Select(m => Path.GetFileNameWithoutExtension(m.Groups[1].Value)).Order().ToList();
        Assert.Equal(["Gabinety.ServiceDefaults", "Gabinety.ServiceDefaults.Edge"], refs);
    }

    [Fact]
    public void Components_do_not_read_HttpContext_through_the_accessor_a_circuit_has_none()
    {
        // BLAZOR-WEB-APP section 7, trap 1: only the host code that runs per request (cascading HttpContext in a static page, the circuit handler) may touch it.
        var offenders = Repo.WebFiles("*.razor").Where(f => File.ReadAllText(f).Contains("IHttpContextAccessor")).Select(Repo.Rel).ToList();
        Assert.True(offenders.Count == 0, "components must not inject IHttpContextAccessor: " + string.Join(", ", offenders));
    }

    [Fact]
    public void A_presentational_component_does_not_inject_a_backend_client()
    {
        // BLAZOR-WEB-APP section 10: pages inject clients; components that a page composes take parameters and raise callbacks.
        var pages = Repo.WebFiles("*.razor").Where(f => File.ReadAllText(f).Contains("@page ")).Select(Repo.Rel).ToHashSet();
        var offenders = Repo.WebFiles("*.razor").Where(f => !pages.Contains(Repo.Rel(f)) && Regex.IsMatch(File.ReadAllText(f), @"@inject\s+(DirectoryClient|BookingClient|ReputationClient|AssistantClient|IdentityClient)")).Select(Repo.Rel).ToList();
        Assert.True(offenders.Count == 0, "inject a client in a page, not in a component: " + string.Join(", ", offenders));
    }

    [Fact]
    public void No_token_or_backend_address_reaches_persisted_component_state_or_the_browser()
    {
        var all = string.Join("\n", Repo.WebFiles("*.razor").Concat(Repo.WebFiles("*.cs")).Where(f => !Repo.Rel(f).StartsWith("Auth/")).Select(File.ReadAllText));
        Assert.DoesNotContain("PersistentComponentState", all);
        Assert.DoesNotContain("PersistentState", all);
        Assert.DoesNotContain("[SupplyParameterFromPersistentComponentState]", all);
    }
}
