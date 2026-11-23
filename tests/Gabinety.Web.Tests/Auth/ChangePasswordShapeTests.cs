using System.Net;
using Gabinety.Web.Api;
using Gabinety.Web.Auth;
using Microsoft.AspNetCore.Http;

namespace Gabinety.Web.Tests.Sessions;

/// <summary>
/// T-11.08: the answers of <c>change-password</c> (authservice v0.3.4 shapes). Identity's own failure is <c>errors</c> as an ARRAY, the model validation <c>errors</c> as an OBJECT keyed by field;
/// the 200 carries a pair under <c>tokens</c> when the service reissues it. Only the classification is tested here, never the wording of the service.
/// </summary>
public class ChangePasswordShapeTests
{
    static (IdentityClient Client, FakeBackend Backend) Rig(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var backend = new FakeBackend { Respond = respond };
        return (new IdentityClient(new HttpClient(backend) { BaseAddress = new Uri("http://auth.test") }, new VisitorAddressAccessor(new HttpContextAccessor())), backend);
    }

    [Fact]
    public async Task A_200_with_a_pair_changes_the_password_and_keeps_the_pair()
    {
        var (c, b) = Rig(_ => FakeBackend.Raw("{\"message\":\"ok\",\"sessionsRevoked\":true,\"tokens\":{\"accessToken\":\"a.b.c\",\"refreshToken\":\"r-2\",\"expiresIn\":3600,\"tokenType\":\"Bearer\"}}"));
        var r = await c.ChangePasswordAsync("a.b.c", "Current-Pass-1", "Nowe-Haslo-123!");
        Assert.Equal((ChangeOutcome.Changed, "a.b.c", "r-2"), (r.Outcome, r.AccessToken, r.RefreshToken));
        var sent = b.Requests[0];
        Assert.Equal(("POST", "/api/v1/auth/change-password"), (sent.Method.Method, sent.PathAndQuery));
        Assert.Contains("\"currentPassword\":\"Current-Pass-1\"", sent.Body);
    }

    [Theory]
    [InlineData("{\"message\":\"ok\",\"sessionsRevoked\":true,\"tokens\":null}")]
    [InlineData("{\"message\":\"ok\",\"sessionsRevoked\":true}")]
    [InlineData("{\"tokens\":{\"accessToken\":\"a.b.c\"}}")]
    [InlineData("not json")]
    public async Task A_200_without_a_usable_pair_says_the_session_must_end(string body)
    {
        var (c, _) = Rig(_ => FakeBackend.Raw(body));
        Assert.Equal(ChangeOutcome.ChangedSignedOut, (await c.ChangePasswordAsync("a.b.c", "Current-Pass-1", "Nowe-Haslo-123!")).Outcome);
    }

    [Theory]
    [InlineData("{\"errors\":[\"Incorrect password.\"]}", ChangeOutcome.CurrentPasswordWrong)]
    [InlineData("{\"errors\":{\"CurrentPassword\":[\"Current password is required\"]}}", ChangeOutcome.CurrentPasswordWrong)]
    [InlineData("{\"errors\":[\"Passwords must have at least one digit ('0'-'9').\"]}", ChangeOutcome.PasswordRejected)]
    [InlineData("{\"errors\":{\"NewPassword\":[\"New password must be between 8 and 100 characters\"]}}", ChangeOutcome.PasswordRejected)]
    [InlineData("{\"errors\":[\"Something unexpected.\"]}", ChangeOutcome.Unavailable)]
    [InlineData("{\"errors\":{\"Token\":[\"Required\"]}}", ChangeOutcome.Unavailable)]
    [InlineData("{\"message\":\"no errors key\"}", ChangeOutcome.Unavailable)]
    public async Task A_400_is_classified_by_its_shape_and_its_field(string body, ChangeOutcome expected)
    {
        var (c, _) = Rig(_ => FakeBackend.Raw(body, HttpStatusCode.BadRequest));
        Assert.Equal(expected, (await c.ChangePasswordAsync("a.b.c", "Current-Pass-1", "Nowe-Haslo-123!")).Outcome);
    }

    [Theory]
    [InlineData(401, ChangeOutcome.Unauthenticated)]
    [InlineData(429, ChangeOutcome.RateLimited)]
    [InlineData(500, ChangeOutcome.Unavailable)]
    [InlineData(404, ChangeOutcome.Unavailable)]
    public async Task Other_refusals_are_states_of_the_call(int status, ChangeOutcome expected)
    {
        var (c, _) = Rig(_ => new HttpResponseMessage((HttpStatusCode)status));
        Assert.Equal(expected, (await c.ChangePasswordAsync("a.b.c", "Current-Pass-1", "Nowe-Haslo-123!")).Outcome);
    }

    [Fact]
    public async Task An_unreachable_service_is_unavailable()
    {
        var (c, _) = Rig(_ => throw new HttpRequestException("down"));
        Assert.Equal(ChangeOutcome.Unavailable, (await c.ChangePasswordAsync("a.b.c", "Current-Pass-1", "Nowe-Haslo-123!")).Outcome);
    }
}
