using System.Globalization;
using System.Net.Mail;

namespace Gabinety.Booking.Notifications.Smtp;

/// <summary>How the connection to the SMTP server is secured.</summary>
public enum SmtpTlsMode
{
    /// <summary>Plain text. Only for a loopback relay or a local test listener.</summary>
    None,
    /// <summary>Plain connection upgraded by STARTTLS, which is REQUIRED (no silent downgrade). Usual port 587.</summary>
    StartTls,
    /// <summary>TLS from the first byte (SMTPS). Usual port 465.</summary>
    Implicit,
}

/// <summary>
/// The <c>Notifications:Smtp:*</c> keys. <see cref="Password"/> is a platform secret (SPEC section 11): environment variable
/// <c>Notifications__Smtp__Password</c>, never a file in the repository. Required: <c>Host</c>, <c>FromAddress</c>, and <c>User</c> together with <c>Password</c>
/// (or neither, for a relay that needs no login).
/// </summary>
public sealed class SmtpOptions
{
    public const string Section = "Notifications:Smtp";
    public string Host { get; set; } = "";
    public int Port { get; set; } = 587;
    public SmtpTlsMode TlsMode { get; set; } = SmtpTlsMode.StartTls;
    public string? User { get; set; }
    public string? Password { get; set; }
    public string FromAddress { get; set; } = "";
    public string FromName { get; set; } = "Gabinety Tech";
    /// <summary>Connect, each command and the whole send are bounded by this (SPEC 5.1: 10 s).</summary>
    public int TimeoutSeconds { get; set; } = 10;

    /// <summary>
    /// Reads and validates the section. <paramref name="problem"/> names the KEYS at fault and never a value (a value can be a secret). Does not throw:
    /// a bad configuration degrades the e-mail dependency (P8), it does not stop the host.
    /// </summary>
    public static bool TryRead(IConfiguration config, out SmtpOptions? options, out string? problem)
    {
        options = null; problem = null;
        var s = config.GetSection(Section);
        string Key(string k) => $"{Section}:{k}";
        string? Get(string k) => string.IsNullOrWhiteSpace(s[k]) ? null : s[k]!.Trim();

        var host = Get("Host");
        if (host is null) { problem = $"{Key("Host")} is not set"; return false; }
        if (host.Any(char.IsWhiteSpace) || host.Any(char.IsControl)) { problem = $"{Key("Host")} is not a host name"; return false; }

        var from = Get("FromAddress");
        if (from is null) { problem = $"{Key("FromAddress")} is not set"; return false; }
        if (!IsPlainAddress(from)) { problem = $"{Key("FromAddress")} is not a valid address"; return false; }

        var tls = SmtpTlsMode.StartTls;
        if (Get("TlsMode") is { } t && !Enum.TryParse(t, ignoreCase: true, out tls)) { problem = $"{Key("TlsMode")} must be None, StartTls or Implicit"; return false; }
        if (!Enum.IsDefined(tls)) { problem = $"{Key("TlsMode")} must be None, StartTls or Implicit"; return false; }

        var port = 587;
        if (Get("Port") is { } p && (!int.TryParse(p, NumberStyles.None, CultureInfo.InvariantCulture, out port) || port is < 1 or > 65535)) { problem = $"{Key("Port")} must be 1-65535"; return false; }

        var timeout = 10;
        if (Get("TimeoutSeconds") is { } to && (!int.TryParse(to, NumberStyles.None, CultureInfo.InvariantCulture, out timeout) || timeout < 1)) { problem = $"{Key("TimeoutSeconds")} must be a positive number of seconds"; return false; }

        var user = Get("User"); var password = s["Password"];
        if (user is not null && string.IsNullOrEmpty(password)) { problem = $"{Key("Password")} is not set (it is a secret; {Key("User")} is)"; return false; }
        if (user is null && !string.IsNullOrEmpty(password)) { problem = $"{Key("User")} is not set (the password is)"; return false; }

        options = new SmtpOptions { Host = host, Port = port, TlsMode = tls, User = user, Password = password, FromAddress = from, FromName = Get("FromName") ?? "Gabinety Tech", TimeoutSeconds = timeout };
        return true;
    }

    /// <summary>A bare <c>local@domain</c>: no display name, no list, no control characters (header injection).</summary>
    internal static bool IsPlainAddress(string value) =>
        value.All(c => !char.IsControl(c) && !char.IsWhiteSpace(c) && c is not ('<' or '>' or ',' or ';'))
        && MailAddress.TryCreate(value, out var a) && a.Address == value && a.Host.Contains('.');
}
