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
        ["Gabinety.AppHost"] = [.. Services.Select(s => "Gabinety." + s), "Gabinety.Web"],
        // ADR-0010 (proposal): the Blazor web host. The kernel and its Edge project, nothing else: the DTOs it reads are its own copies and the contract is HTTP (BLAZOR-WEB-APP section 4).
        // The owner adds this edge to SPEC 3.2 (listed in the PR).
        ["Gabinety.Web"] = ["Gabinety.ServiceDefaults", "Gabinety.ServiceDefaults.Edge"],
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
        ["Gabinety.Web"] = [], // the ASP.NET Core shared framework only: no component library, no JS interop package, no CSS framework (BLAZOR-WEB-APP section 4)
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

    /// <summary>T-09.05 (ADR proposal 0011): the Stripe adapter is plain HttpClient + System.Text.Json inside <c>Payments/Stripe</c>; nothing of it is public and nothing else names it.</summary>
    [Fact]
    public void The_Stripe_adapter_is_confined_to_its_namespace_and_exposes_no_vendor_type()
    {
        const string Ns = "Gabinety.Booking.Payments.Stripe";
        var asm = typeof(Gabinety.Booking.Payments.Abstractions.IPaymentProvider).Assembly;
        var inside = asm.GetTypes().Where(t => t.Namespace == Ns).ToList();
        Assert.NotEmpty(inside); // the rule is looking at the right place
        var exposed = inside.Where(t => t.IsPublic || t.IsNestedPublic).Select(t => t.FullName).ToList();
        Assert.True(exposed.Count == 0, $"public types in {Ns}: {string.Join(", ", exposed)}");
        var leaks = asm.GetTypes().Where(t => t.Namespace != Ns && (t.IsPublic || t.IsNestedPublic))
            .SelectMany(t => t.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Select(m => (t, m, Type: m switch { MethodInfo mi => mi.ReturnType, PropertyInfo pi => pi.PropertyType, FieldInfo fi => fi.FieldType, _ => null })))
            .Where(x => x.Type is { Namespace: Ns }).Select(x => $"{x.t.FullName}.{x.m.Name}").ToList();
        Assert.True(leaks.Count == 0, $"public members that expose {Ns}: {string.Join(", ", leaks)}");

        var dir = Path.Combine(Repo.Root, "src", "Gabinety.Booking", "Payments", "Stripe") + Path.DirectorySeparatorChar;
        var composition = Path.Combine(Repo.Root, "src", "Gabinety.Booking", "Payments", "Mock", "MockPaymentProvider.cs"); // PaymentsFeature: the one registration
        var named = Repo.Files("src", "*.cs").Concat(Repo.Files("tools", "*.cs")).Where(f => !f.StartsWith(dir) && f != composition)
            .Where(f => StripeAdapterUse().IsMatch(File.ReadAllText(f))).Select(Repo.Rel).ToList();
        Assert.True(named.Count == 0, $"the Stripe adapter is named outside Payments/Stripe and PaymentsFeature: {string.Join(", ", named)}");
        Assert.Contains(Repo.Files("src", "*.cs"), f => f == composition && StripeAdapterUse().IsMatch(File.ReadAllText(f)));

        var packages = Repo.Projects().Where(p => p.PackageRefs.Any(r => r.StartsWith("Stripe", StringComparison.OrdinalIgnoreCase))).Select(p => p.File).ToList();
        Assert.True(packages.Count == 0, $"no vendor package for Stripe (ADR proposal 0011): {string.Join(", ", packages)}");
    }

    [GeneratedRegex(@"Payments\.Stripe\b|\bStripePaymentProvider\b|\bStripeApi\b|api\.stripe\.com")]
    private static partial Regex StripeAdapterUse();

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
    public void The_web_host_does_not_reach_a_service_or_the_contracts_or_the_SDK()
    {
        var web = Repo.Projects().Single(p => p.Name == "Gabinety.Web");
        string[] forbidden = ["Gabinety.Contracts", "Gabinety.Llm", "Gabinety.Directory", "Gabinety.Booking", "Gabinety.Reputation", "Gabinety.Assistant", "Gabinety.Billing", "Gabinety.Scheduling", "Gabinety.Ranking", "Gabinety.DevIdentity"];
        Assert.True(!web.ProjectRefs.Intersect(forbidden).Any(), $"{web.File} references {string.Join(", ", web.ProjectRefs.Intersect(forbidden))}: the web host talks to the services over HTTP (P3)");
        Assert.Empty(web.PackageRefs);
        Assert.Contains("Gabinety.ServiceDefaults.Edge", AllowedEdges["Gabinety.Web"]); // the origin guard lives there, outside the 400-line kernel
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

    /// <summary>
    /// F-04 re-review: the rate limiter keys a signed-in caller by user id only when it runs AFTER authentication (CONTRACTS 10.2). Booking and Reputation prove it by behaviour;
    /// this holds all four services to the same order, and requires the service to register the limiter at all.
    /// </summary>
    [Theory]
    [InlineData("Directory")] [InlineData("Booking")] [InlineData("Reputation")] [InlineData("Assistant")]
    public void The_rate_limiter_runs_after_authentication_in_every_service(string service)
    {
        var code = CodeScan.Sanitize(File.ReadAllText(Path.Combine(Repo.Root, $"src/Gabinety.{service}/Program.cs")));
        var auth = code.IndexOf("app.UseAuthentication()", StringComparison.Ordinal);
        var limiter = code.IndexOf("app.UseRateLimiter()", StringComparison.Ordinal);
        Assert.True(auth >= 0 && limiter >= 0, $"{service}/Program.cs must call app.UseAuthentication() and app.UseRateLimiter()");
        Assert.True(auth < limiter, $"{service}/Program.cs: UseRateLimiter runs before UseAuthentication, so every caller is keyed by address (CONTRACTS 10.2)");
        Assert.Contains("AddRateLimiting(", code);
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
