using Gabinety.Booking.Notifications.Smtp;
using Microsoft.Extensions.Configuration;

namespace Gabinety.Booking.Tests.Smtp;

public class SmtpOptionsTests
{
    static IConfiguration Config(params (string Key, string? Value)[] kv) =>
        new ConfigurationBuilder().AddInMemoryCollection(kv.Select(p => new KeyValuePair<string, string?>("Notifications:Smtp:" + p.Key, p.Value))).Build();

    static readonly (string, string?)[] Minimal = [("Host", "smtp.demo.invalid"), ("FromAddress", "powiadomienia@demo.invalid")];

    [Fact]
    public void A_minimal_configuration_gets_the_documented_defaults()
    {
        Assert.True(SmtpOptions.TryRead(Config(Minimal), out var o, out var problem), problem);
        Assert.Equal("smtp.demo.invalid", o!.Host);
        Assert.Equal(587, o.Port);
        Assert.Equal(SmtpTlsMode.StartTls, o.TlsMode); // secure by default
        Assert.Equal(10, o.TimeoutSeconds); // SPEC 5.1: Booking -> e-mail provider 10 s
        Assert.Null(o.User); Assert.Null(o.Password);
        Assert.Equal("Gabinety Tech", o.FromName);
    }

    [Theory]
    [InlineData("None", SmtpTlsMode.None, 25)]
    [InlineData("StartTls", SmtpTlsMode.StartTls, 587)]
    [InlineData("starttls", SmtpTlsMode.StartTls, 587)]
    [InlineData("Implicit", SmtpTlsMode.Implicit, 465)]
    public void The_tls_mode_is_parsed_and_the_port_can_be_given(string mode, SmtpTlsMode expected, int port)
    {
        Assert.True(SmtpOptions.TryRead(Config([.. Minimal, ("TlsMode", mode), ("Port", port.ToString())]), out var o, out var problem), problem);
        Assert.Equal(expected, o!.TlsMode);
        Assert.Equal(port, o.Port);
    }

    [Fact]
    public void All_keys_are_read()
    {
        Assert.True(SmtpOptions.TryRead(Config([.. Minimal, ("User", "u"), ("Password", "p"), ("FromName", "Zespół"), ("TimeoutSeconds", "3")]), out var o, out _));
        Assert.Equal(("u", "p", "Zespół", 3), (o!.User, o.Password, o.FromName, o.TimeoutSeconds));
    }

    [Theory]
    [InlineData("Host", "missing", "Host")]
    [InlineData("FromAddress", "missing", "FromAddress")]
    public void A_missing_required_key_is_a_problem_that_names_the_key(string key, string how, string named)
    {
        _ = how;
        var kv = Minimal.Where(p => p.Item1 != key).ToArray();
        Assert.False(SmtpOptions.TryRead(Config(kv), out var o, out var problem));
        Assert.Null(o);
        Assert.Contains("Notifications:Smtp:" + named, problem);
    }

    [Theory]
    [InlineData("Host", " ")]
    [InlineData("FromAddress", "not-an-address")]
    [InlineData("FromAddress", "a@b.invalid\r\nBcc: x@y.invalid")]
    [InlineData("TlsMode", "Maybe")]
    [InlineData("Port", "0")]
    [InlineData("Port", "70000")]
    [InlineData("Port", "abc")]
    [InlineData("TimeoutSeconds", "0")]
    [InlineData("TimeoutSeconds", "-1")]
    public void An_invalid_value_is_a_problem_not_an_exception(string key, string value)
    {
        var kv = Minimal.Where(p => p.Item1 != key).Append((key, (string?)value)).ToArray();
        Assert.False(SmtpOptions.TryRead(Config(kv), out _, out var problem));
        Assert.Contains("Notifications:Smtp:" + key, problem);
    }

    [Fact]
    public void A_user_without_a_password_is_a_problem_the_missing_secret_of_P8()
    {
        Assert.False(SmtpOptions.TryRead(Config([.. Minimal, ("User", "u")]), out _, out var problem));
        Assert.Contains("Notifications:Smtp:Password", problem);
    }

    [Fact]
    public void A_user_with_an_empty_password_is_a_problem()
    {
        Assert.False(SmtpOptions.TryRead(Config([.. Minimal, ("User", "u"), ("Password", "")]), out _, out var problem));
        Assert.Contains("Notifications:Smtp:Password", problem);
    }

    [Fact]
    public void A_password_without_a_user_is_a_problem_too()
    {
        Assert.False(SmtpOptions.TryRead(Config([.. Minimal, ("Password", "p")]), out _, out var problem));
        Assert.Contains("Notifications:Smtp:User", problem);
    }

    [Fact]
    public void A_problem_never_repeats_a_secret()
    {
        SmtpOptions.TryRead(Config([("Host", "smtp.demo.invalid"), ("FromAddress", "broken"), ("User", "demo-user"), ("Password", "demo-pass-123")]), out _, out var problem);
        Assert.DoesNotContain("demo-pass-123", problem);
        Assert.DoesNotContain("demo-user", problem);
    }
}
