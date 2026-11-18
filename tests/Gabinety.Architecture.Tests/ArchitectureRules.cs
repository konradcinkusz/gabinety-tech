using System.Reflection;
using System.Text.RegularExpressions;

namespace Gabinety.Architecture.Tests;

/// <summary>One test per rule of SPEC 3.2 / 5 / 6.0 and COPILOT-SDK R1, R2. A failure names the file or project that broke the rule.</summary>
public partial class ArchitectureRules
{
    const string Sdk = "GitHub.Copilot";
    static readonly string[] Services = ["Directory", "Booking", "Reputation", "Assistant"];

    /// <summary>SPEC 3.2: allowed project edges under src/ and tools/. Anything else is a violation.</summary>
    static readonly Dictionary<string, string[]> AllowedEdges = new()
    {
        ["Gabinety.Billing"] = [], ["Gabinety.Scheduling"] = [], ["Gabinety.Ranking"] = [], ["Gabinety.Llm"] = [], ["Gabinety.Contracts"] = [],
        ["Gabinety.ServiceDefaults"] = [],
        ["Gabinety.ServiceDefaults.Edge"] = ["Gabinety.ServiceDefaults"], // T-10.10 (F-03): the public-edge guard and key validation, outside the 400-line kernel (SPEC 6)
        ["Gabinety.Directory"] = ["Gabinety.ServiceDefaults", "Gabinety.ServiceDefaults.Edge", "Gabinety.Contracts", "Gabinety.Ranking"],
        ["Gabinety.Booking"] = ["Gabinety.ServiceDefaults", "Gabinety.ServiceDefaults.Edge", "Gabinety.Contracts", "Gabinety.Billing", "Gabinety.Scheduling"],
        ["Gabinety.Reputation"] = ["Gabinety.ServiceDefaults", "Gabinety.ServiceDefaults.Edge", "Gabinety.Contracts"],
        ["Gabinety.Assistant"] = ["Gabinety.ServiceDefaults", "Gabinety.ServiceDefaults.Edge", "Gabinety.Contracts", "Gabinety.Llm"],
        ["Gabinety.AppHost"] = [.. Services.Select(s => "Gabinety." + s)],
        ["Gabinety.DevIdentity"] = [], ["Gabinety.DemoSeeder"] = [], // tools/: no project reference at all
    };

    /// <summary>
    /// Documented exceptions as (project, referenced project) -> reason. The task text says no src/ project references DevIdentity, but
    /// the AppHost already does (Aspire orchestration: AddProject&lt;Projects.Gabinety_DevIdentity&gt;). Pending the supervisor's answer (see PR).
    /// </summary>
    static readonly Dictionary<(string Project, string Ref), string> EdgeExceptions = new()
    {
        [("Gabinety.AppHost", "Gabinety.DevIdentity")] = "AppHost orchestrates the dev identity stand-in; composition root, no code dependency",
        [("Gabinety.AppHost", "Gabinety.DemoSeeder")] = "AppHost starts the demo seeder as a resource (T-08.02); Aspire needs the project reference for Projects.*, no code dependency",
    };

    /// <summary>SPEC 3.2 dependency budget: the only packages a project may reference (absent = unchecked here).</summary>
    static readonly Dictionary<string, string[]> PackageBudget = new()
    {
        ["Gabinety.Billing"] = [], ["Gabinety.Scheduling"] = [], ["Gabinety.Ranking"] = [], ["Gabinety.Contracts"] = [],
        ["Gabinety.Llm"] = ["GitHub.Copilot.SDK"],
    };

    /// <summary>Documented exceptions as (file under src/, member name) -> reason. Must start empty.</summary>
    static readonly Dictionary<(string File, string Name), string> FreeTextLogExceptions = new();

    [Fact]
    public void Only_Llm_references_the_Copilot_SDK()
    {
        var offenders = Repo.Projects().Where(p => p.PackageRefs.Any(r => r.StartsWith(Sdk)) && p.Name != "Gabinety.Llm").Select(p => p.File).ToList();
        Assert.True(offenders.Count == 0, $"only src/Gabinety.Llm may reference {Sdk}.SDK, but also: {string.Join(", ", offenders)}");
        Assert.Contains(Repo.Projects(), p => p.Name == "Gabinety.Llm" && p.PackageRefs.Contains("GitHub.Copilot.SDK"));

        var anyCsproj = Repo.Files("tests", "*.csproj").Where(f => File.ReadAllText(f).Contains("GitHub.Copilot")).Select(Repo.Rel).ToList();
        Assert.True(anyCsproj.Count == 0, $"test projects must not reference the Copilot SDK either: {string.Join(", ", anyCsproj)}");

        var llm = Path.Combine(Repo.Root, "src", "Gabinety.Llm") + Path.DirectorySeparatorChar;
        var users = Repo.Files("src", "*.cs").Concat(Repo.Files("tools", "*.cs")).Concat(Repo.Files("tests", "*.cs"))
            .Where(f => !f.StartsWith(llm) && UsingCopilot().IsMatch(File.ReadAllText(f))).Select(Repo.Rel).ToList();
        Assert.True(users.Count == 0, $"'using {Sdk}' outside src/Gabinety.Llm: {string.Join(", ", users)}");
    }

    [GeneratedRegex(@"\busing\s+(static\s+)?(\w+\s*=\s*)?GitHub\.Copilot\b")]
    private static partial Regex UsingCopilot();

    [Fact]
    public void Llm_public_surface_exposes_no_Copilot_type()
    {
        var v = PublicSurface.CopilotLeaks(typeof(Gabinety.Llm.ILlmGateway).Assembly);
        Assert.True(v.Count == 0, $"Gabinety.Llm exposes {Sdk}* types: {string.Join("; ", v)}");
    }

    /// <summary>The standard-SMTP client (T-09.06) is a vendor like any other (P11): one project, one namespace, nothing of it public.</summary>
    static readonly string[] SmtpVendor = ["MailKit", "MimeKit", "BouncyCastle"];

    [Fact]
    public void Only_the_Booking_SMTP_adapter_uses_MailKit_and_exposes_none_of_it()
    {
        var offenders = Repo.Projects().Where(p => p.PackageRefs.Any(r => r is "MailKit" or "MimeKit") && p.Name != "Gabinety.Booking").Select(p => p.File).ToList();
        Assert.True(offenders.Count == 0, $"only src/Gabinety.Booking may reference MailKit, but also: {string.Join(", ", offenders)}");

        var adapter = Path.Combine(Repo.Root, "src", "Gabinety.Booking", "Notifications", "Smtp") + Path.DirectorySeparatorChar;
        var users = Repo.Files("src", "*.cs").Concat(Repo.Files("tools", "*.cs"))
            .Where(f => !f.StartsWith(adapter) && UsingMailKit().IsMatch(File.ReadAllText(f))).Select(Repo.Rel).ToList();
        Assert.True(users.Count == 0, $"'using MailKit/MimeKit' outside src/Gabinety.Booking/Notifications/Smtp: {string.Join(", ", users)}");
        Assert.Contains(Repo.Files("src", "*.cs"), f => f.StartsWith(adapter) && UsingMailKit().IsMatch(File.ReadAllText(f))); // the rule is looking at the right place

        var leaks = PublicSurface.VendorLeaks(typeof(Gabinety.Booking.Notifications.IEmailSender).Assembly, SmtpVendor);
        Assert.True(leaks.Count == 0, $"Gabinety.Booking exposes MailKit/MimeKit types: {string.Join("; ", leaks)}");
    }

    [GeneratedRegex(@"\busing\s+(static\s+)?(\w+\s*=\s*)?(MailKit|MimeKit)\b")]
    private static partial Regex UsingMailKit();

    [Fact]
    public void Project_references_follow_SPEC_3_2()
    {
        var bad = new List<string>();
        foreach (var p in Repo.Projects())
        {
            if (!AllowedEdges.TryGetValue(p.Name, out var allowed)) { bad.Add($"{p.File}: project is not in the SPEC 3.2 table"); continue; }
            bad.AddRange(p.ProjectRefs.Except(allowed).Where(r => !EdgeExceptions.ContainsKey((p.Name, r))).Select(r => $"{p.File} -> {r}"));
            if (PackageBudget.TryGetValue(p.Name, out var pk)) bad.AddRange(p.PackageRefs.Except(pk).Select(r => $"{p.File} package {r} is over budget"));
        }
        Assert.True(bad.Count == 0, "edges outside SPEC 3.2: " + string.Join("; ", bad));
    }

    [Fact]
    public void Kernel_references_no_domain_project()
    {
        var csproj = Repo.Projects().Single(p => p.Name == "Gabinety.ServiceDefaults");
        Assert.True(csproj.ProjectRefs.Count == 0, $"{csproj.File} references {string.Join(", ", csproj.ProjectRefs)}");
        var asm = typeof(Gabinety.ServiceDefaults.Extensions).Assembly.GetReferencedAssemblies().Select(a => a.Name!).Where(n => n.StartsWith("Gabinety.")).ToList();
        Assert.True(asm.Count == 0, $"Gabinety.ServiceDefaults assembly references {string.Join(", ", asm)}");
    }

    [Fact]
    public void Kernel_is_at_most_400_lines_of_csharp()
    {
        var lines = Repo.Files("src/Gabinety.ServiceDefaults", "*.cs").Sum(f => File.ReadAllLines(f).Length);
        Assert.InRange(lines, 1, 400);
    }

    [Fact]
    public void DevIdentity_is_isolated()
    {
        var all = Repo.Projects();
        var dev = all.Single(p => p.Name == "Gabinety.DevIdentity");
        var bad = dev.ProjectRefs.Select(r => $"{dev.File} -> {r}").ToList();
        bad.AddRange(all.Where(p => p.File.StartsWith("src/") && p.ProjectRefs.Contains(dev.Name) && !EdgeExceptions.ContainsKey((p.Name, dev.Name))).Select(p => $"{p.File} -> {dev.Name}"));

        var docker = Repo.Files(".", "*").Where(f => !f.Contains("/.git/") && !f.Contains("/node_modules/"))
            .Where(f => Path.GetFileName(f).StartsWith("Dockerfile", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".dockerfile", StringComparison.OrdinalIgnoreCase))
            .Where(f => File.ReadAllText(f).Contains("DevIdentity", StringComparison.OrdinalIgnoreCase)).Select(Repo.Rel);
        bad.AddRange(docker.Select(f => $"{f} mentions DevIdentity"));
        Assert.True(bad.Count == 0, "DevIdentity must stay out of src/ references and Dockerfiles: " + string.Join("; ", bad));
    }

    [Fact]
    public void No_clock_reads_outside_the_AppHost()
    {
        var bad = Repo.SourceFiles().Where(f => !f.Rel.StartsWith("src/Gabinety.AppHost/"))
            .SelectMany(f => ClockRead().Matches(f.Code).Select(m => $"{f.Rel}: {Regex.Replace(m.Value, @"\s+", "")}")).ToList();
        Assert.True(bad.Count == 0, "read time from TimeProvider (SPEC 5 rule 4): " + string.Join("; ", bad));
    }

    [GeneratedRegex(@"\bDateTime(Offset)?\s*\.\s*(Now|UtcNow)\b")]
    private static partial Regex ClockRead();

    [Fact]
    public void No_free_text_in_log_calls()
    {
        var bad = Repo.SourceFiles()
            .SelectMany(f => CodeScan.FreeTextInLogCalls(f.Code).Where(h => !FreeTextLogExceptions.ContainsKey((f.Rel, h.Name))).Select(h => $"{f.Rel}:{h.Line} logs '{h.Name}'"))
            .ToList();
        Assert.True(bad.Count == 0, "free text must not reach a log (SPEC 5 rule 2); log lengths, ids, hashes: " + string.Join("; ", bad));
        Assert.All(FreeTextLogExceptions, e => Assert.False(string.IsNullOrWhiteSpace(e.Value), $"exception {e.Key} needs a reason"));
    }

    [Fact]
    public void Contracts_has_no_package_reference()
    {
        var p = Repo.Projects().Single(x => x.Name == "Gabinety.Contracts");
        Assert.True(p.PackageRefs.Count == 0 && p.ProjectRefs.Count == 0, $"{p.File} references {string.Join(", ", p.PackageRefs.Concat(p.ProjectRefs))}");
        var refs = typeof(Gabinety.Contracts.Common.MoneyDto).Assembly.GetReferencedAssemblies()
            .Select(a => a.Name!).Where(n => !n.StartsWith("System.") && n != "mscorlib" && n != "netstandard").ToList();
        Assert.True(refs.Count == 0, $"Gabinety.Contracts assembly references {string.Join(", ", refs)}");
    }
}

/// <summary>Reflection over every public type and member, including generic arguments, base types and interfaces.</summary>
static class PublicSurface
{
    public static List<string> CopilotLeaks(Assembly asm) => VendorLeaks(asm, "GitHub.Copilot");

    /// <summary>Public (or protected) members of <paramref name="asm"/> whose types come from an assembly named <paramref name="prefixes"/>*.</summary>
    public static List<string> VendorLeaks(Assembly asm, params string[] prefixes)
    {
        bool Leaks(Type t) => PublicSurface.Leaks(t, prefixes);
        var found = new List<string>();
        const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        foreach (var t in asm.GetTypes().Where(IsExposed))
        {
            void Check(Type? x, string where) { if (x is not null && Leaks(x)) found.Add($"{t.FullName}.{where} uses {x}"); }
            Check(t.BaseType, "<base>");
            foreach (var i in t.GetInterfaces()) Check(i, "<interface>");
            foreach (var a in t.GetGenericArguments().SelectMany(g => g.GetGenericParameterConstraints())) Check(a, "<constraint>");
            foreach (var m in t.GetMembers(All))
            {
                var visible = m switch
                {
                    MethodBase mb => mb.IsPublic || mb.IsFamily || mb.IsFamilyOrAssembly,
                    FieldInfo f => f.IsPublic || f.IsFamily || f.IsFamilyOrAssembly,
                    PropertyInfo p => p.GetAccessors(true).Any(a => a.IsPublic || a.IsFamily || a.IsFamilyOrAssembly),
                    EventInfo e => (e.AddMethod?.IsPublic ?? false) || (e.AddMethod?.IsFamily ?? false),
                    Type n => n.IsNestedPublic || n.IsNestedFamily,
                    _ => false,
                };
                if (!visible) continue;
                switch (m)
                {
                    case MethodBase mb:
                        foreach (var p in mb.GetParameters()) Check(p.ParameterType, $"{m.Name}({p.Name})");
                        if (mb is MethodInfo mi) { Check(mi.ReturnType, $"{m.Name}()"); foreach (var g in mi.GetGenericArguments().SelectMany(g => g.GetGenericParameterConstraints())) Check(g, m.Name); }
                        break;
                    case FieldInfo f: Check(f.FieldType, f.Name); break;
                    case PropertyInfo p: Check(p.PropertyType, p.Name); foreach (var ip in p.GetIndexParameters()) Check(ip.ParameterType, p.Name); break;
                    case EventInfo e: Check(e.EventHandlerType, e.Name); break;
                }
            }
        }
        return found;
    }

    static bool IsExposed(Type t) => t.IsPublic || (t.IsNestedPublic && IsExposed(t.DeclaringType!)) || (t.IsNestedFamily && IsExposed(t.DeclaringType!));

    /// <summary>True if the type, its element, or any generic argument belongs to an assembly named GitHub.Copilot*.</summary>
    static bool Leaks(Type t, string[] prefixes)
    {
        if (t.HasElementType) return Leaks(t.GetElementType()!, prefixes);
        if (t.IsGenericType && t.GetGenericArguments().Any(a => Leaks(a, prefixes))) return true;
        var name = t.Assembly.GetName().Name;
        return name is not null && prefixes.Any(p => name.StartsWith(p, StringComparison.Ordinal));
    }
}
