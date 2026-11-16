using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace Gabinety.Integration.Tests.AuthContract;

/// <summary>
/// Starts the built tools/Gabinety.DevIdentity as a child process. Used only to prove the start-up refusal (exit code), which cannot be seen in process.
/// </summary>
public static class DevIdentityProcess
{
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
}
