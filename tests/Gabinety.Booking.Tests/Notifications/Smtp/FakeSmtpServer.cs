using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Gabinety.Booking.Tests.Smtp;

/// <summary>One message as the fake listener received it.</summary>
sealed record ReceivedMail(string MailFrom, List<string> RcptTo, string Data, string? AuthUser, string? AuthPassword);

/// <summary>
/// A minimal in-process SMTP listener on a free loopback port (RFC 5321 subset: EHLO, AUTH PLAIN, MAIL, RCPT, DATA, RSET, NOOP, QUIT). No TLS: tests use
/// <c>TlsMode=None</c> against it. <see cref="RejectRcpt"/> and <see cref="Hang"/> make it behave like a provider that refuses or stalls.
/// </summary>
sealed class FakeSmtpServer : IDisposable
{
    readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    readonly CancellationTokenSource _stop = new();
    readonly List<ReceivedMail> _mails = [];
    readonly Task _loop;
    public bool OfferAuth = true;
    /// <summary>Advertise 8BITMIME: the client may then send UTF-8 bodies as they are; without it a client must encode them to 7 bit.</summary>
    public bool Offer8BitMime = true;
    public string? RejectRcpt;
    public bool Hang;
    public int Connections;
    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;
    public IReadOnlyList<ReceivedMail> Mails { get { lock (_mails) return [.. _mails]; } }

    public FakeSmtpServer() { _listener.Start(); _loop = Task.Run(Accept); }

    async Task Accept()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var c = await _listener.AcceptTcpClientAsync(_stop.Token);
                Interlocked.Increment(ref Connections);
                _ = Task.Run(() => Serve(c));
            }
        }
        catch (Exception) when (_stop.IsCancellationRequested) { }
    }

    async Task Serve(TcpClient client)
    {
        using var _ = client;
        try
        {
            using var s = client.GetStream();
            using var r = new StreamReader(s, new UTF8Encoding(false));
            using var w = new StreamWriter(s, new UTF8Encoding(false)) { NewLine = "\r\n", AutoFlush = true };
            await w.WriteLineAsync("220 fake.invalid ESMTP ready");
            if (Hang) { await Task.Delay(Timeout.Infinite, _stop.Token); return; }
            string from = "", user = "", pass = ""; var rcpt = new List<string>(); string? authUser = null, authPass = null;
            while (await r.ReadLineAsync(_stop.Token) is { } line)
            {
                var up = line.ToUpperInvariant();
                if (up.StartsWith("EHLO") || up.StartsWith("HELO"))
                {
                    await w.WriteLineAsync("250-fake.invalid");
                    if (Offer8BitMime) await w.WriteLineAsync("250-8BITMIME");
                    await w.WriteLineAsync("250-SMTPUTF8");
                    if (OfferAuth) await w.WriteLineAsync("250-AUTH PLAIN");
                    await w.WriteLineAsync("250 SIZE 10485760");
                }
                else if (up.StartsWith("AUTH PLAIN"))
                {
                    var parts = Encoding.UTF8.GetString(Convert.FromBase64String(line[10..].Trim())).Split('\0');
                    (user, pass) = (parts[1], parts[2]); authUser = user; authPass = pass;
                    await w.WriteLineAsync("235 2.7.0 ok");
                }
                else if (up.StartsWith("MAIL FROM:")) { from = line[10..].Trim().Split('>')[0] + ">"; await w.WriteLineAsync("250 ok"); }
                else if (up.StartsWith("RCPT TO:"))
                {
                    var to = line[8..].Trim();
                    if (RejectRcpt != null && to.Contains(RejectRcpt, StringComparison.OrdinalIgnoreCase)) await w.WriteLineAsync($"550 5.1.1 {to}: Recipient address rejected: user unknown");
                    else { rcpt.Add(to); await w.WriteLineAsync("250 ok"); }
                }
                else if (up == "DATA")
                {
                    await w.WriteLineAsync("354 go on");
                    var sb = new StringBuilder();
                    while (await r.ReadLineAsync(_stop.Token) is { } d && d != ".") sb.Append(d.StartsWith("..") ? d[1..] : d).Append("\r\n");
                    lock (_mails) _mails.Add(new ReceivedMail(from, [.. rcpt], sb.ToString(), authUser, authPass));
                    rcpt = [];
                    await w.WriteLineAsync("250 2.0.0 queued");
                }
                else if (up == "RSET" || up == "NOOP") await w.WriteLineAsync("250 ok");
                else if (up == "QUIT") { await w.WriteLineAsync("221 bye"); return; }
                else await w.WriteLineAsync("502 not implemented");
            }
        }
        catch (Exception) { /* the client went away or the test ended */ }
    }

    public void Dispose() { _stop.Cancel(); _listener.Stop(); try { _loop.Wait(1000); } catch (AggregateException) { } _stop.Dispose(); }
}
