using System.Diagnostics;

namespace Gabinety.Assistant.Tests.OwnerSmoke;

/// <summary>scripts/smoke-llm.sh refuses before it runs anything (COPILOT-SDK 11, T-02.05).</summary>
public class SmokeScriptTests
{
    static string Script()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "scripts", "smoke-llm.sh"))) return Path.Combine(d.FullName, "scripts", "smoke-llm.sh");
        throw new FileNotFoundException("scripts/smoke-llm.sh");
    }

    static (int Code, string Output) Run(Action<IDictionary<string, string?>> env)
    {
        var psi = new ProcessStartInfo("bash") { RedirectStandardOutput = true, RedirectStandardError = true };
        psi.ArgumentList.Add(Script());
        env(psi.Environment);
        using var p = Process.Start(psi)!;
        var all = p.StandardOutput.ReadToEndAsync().Result + p.StandardError.ReadToEndAsync().Result;
        if (!p.WaitForExit(30_000)) { p.Kill(true); throw new TimeoutException("script did not refuse"); }
        return (p.ExitCode, all);
    }

    [Fact]
    public void Under_CI_the_script_exits_2_with_the_refusal_text()
    {
        var (code, output) = Run(e => { e["CI"] = "1"; e.Remove("Llm__Provider"); });
        Assert.Equal(2, code);
        Assert.Contains("refusing to run when CI is set", output);
    }

    [Theory]
    [InlineData("Fake")]
    [InlineData("fake")]
    public void With_the_Fake_provider_the_script_exits_2(string provider)
    {
        var (code, output) = Run(e => { e.Remove("CI"); e["Llm__Provider"] = provider; });
        Assert.Equal(2, code);
        Assert.Contains("Llm:Provider is Fake", output);
    }

    [Fact]
    public void Without_dotnet_the_script_exits_2()
    {
        var empty = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var (code, output) = Run(e => { e.Remove("CI"); e.Remove("Llm__Provider"); e["PATH"] = empty; });
            Assert.Equal(2, code);
            Assert.Contains("dotnet is required", output);
        }
        finally { Directory.Delete(empty); }
    }

    [Fact]
    public void The_script_says_what_it_spends()
    {
        var text = File.ReadAllText(Script());
        Assert.Contains("AI credit", text); // one real call of the configured model is spent
    }
}
