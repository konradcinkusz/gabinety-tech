using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace Gabinety.Integration.Tests.AuthContract;

/// <summary>
/// Runs the built tools/Gabinety.DevIdentity as a child process on a free port with GABINETY_FAKE=1. A process, not a project reference:
/// this project may not take a reference or package without the supervisor. The solution build produces the dll.
/// </summary>
public sealed class DevIdentityProcess : IAsyncLifetime
{
    public string BaseUrl { get; private set; } = "";
    private Process? _process;

    public static string DllPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        var configuration = dir.Parent!.Name; // .../bin/<Configuration>/<tfm>/
        var root = dir;
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "GabinetyTech.sln"))) root = root.Parent;
        if (root is null) throw new InvalidOperationException("solution root not found");
        return Path.Combine(root.FullName, "tools", "Gabinety.DevIdentity", "bin", configuration, dir.Name, "Gabinety.DevIdentity.dll");
    }

    public static Process Start(string url, string? fake, bool redirect = true)
    {
        var dll = DllPath();
        if (!File.Exists(dll)) throw new FileNotFoundException($"DevIdentity is not built ({dll}); run `dotnet build` on the solution first");
        var psi = new ProcessStartInfo("dotnet") { RedirectStandardOutput = redirect, RedirectStandardError = redirect, UseShellExecute = false };
        psi.ArgumentList.Add(dll);
        psi.Environment["ASPNETCORE_URLS"] = url;
        psi.Environment.Remove("GABINETY_FAKE");
        if (fake is not null) psi.Environment["GABINETY_FAKE"] = fake;
        return Process.Start(psi)!;
    }

    public static string FreeUrl()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return $"http://127.0.0.1:{port}";
    }

    public async Task InitializeAsync()
    {
        BaseUrl = FreeUrl();
        _process = Start(BaseUrl, "1");
        _process.OutputDataReceived += (_, _) => { };
        _process.BeginOutputReadLine();
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var deadline = DateTime.UtcNow.AddSeconds(60); // generous: waits on the condition, not on a fixed delay
        while (true)
        {
            if (_process.HasExited) throw new InvalidOperationException($"DevIdentity exited early with code {_process.ExitCode}: {await _process.StandardError.ReadToEndAsync()}");
            try { if ((await http.GetAsync(BaseUrl + "/health")).IsSuccessStatusCode) return; } catch (HttpRequestException) { } catch (TaskCanceledException) { }
            if (DateTime.UtcNow > deadline) throw new TimeoutException("DevIdentity did not answer /health within 60 s");
            await Task.Delay(100);
        }
    }

    public Task DisposeAsync()
    {
        if (_process is { HasExited: false }) { _process.Kill(entireProcessTree: true); _process.WaitForExit(10_000); }
        _process?.Dispose();
        return Task.CompletedTask;
    }
}
