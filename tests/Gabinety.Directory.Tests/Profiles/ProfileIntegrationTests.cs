using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Gabinety.Contracts.Directory;
using Gabinety.Directory.Features.Profiles;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace Gabinety.Directory.Tests.Profiles;

/// <summary>Real PostgreSQL: the migration creates the table, the unique indexes decide slug collisions, the per-user lookup translates.</summary>
[Trait("tier", "integration")]
public class ProfileIntegrationTests
{
    [Fact]
    public async Task Colliding_slug_gets_a_suffix_and_each_user_reads_only_their_own_row()
    {
        await using var pg = new PostgreSqlBuilder("postgres:17-alpine").Build();
        await pg.StartAsync();
        using var f = new ProfilePgFactory(pg.GetConnectionString());
        var a = f.ClientFor("demo-pg-a");
        var b = f.ClientFor("demo-pg-b");
        await f.Services.GetRequiredService<Gabinety.Directory.Persistence.MigrationState>().WaitForCompletionAsync(TimeSpan.FromSeconds(60));

        var j = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var ra = await a.PostAsJsonAsync("/api/v1/me/practitioner", new CreatePractitionerRequest("Jan Kowalski"));
        var rb = await b.PostAsJsonAsync("/api/v1/me/practitioner", new CreatePractitionerRequest("Jan Kowalski"));
        Assert.Equal(HttpStatusCode.Created, ra.StatusCode);
        Assert.Equal(HttpStatusCode.Created, rb.StatusCode);
        var pa = (await ra.Content.ReadFromJsonAsync<PractitionerPrivate>(j))!;
        var pb = (await rb.Content.ReadFromJsonAsync<PractitionerPrivate>(j))!;
        Assert.Equal("jan-kowalski", pa.Slug);
        Assert.Equal("jan-kowalski-2", pb.Slug);

        Assert.Equal(pa.Id, (await a.GetFromJsonAsync<PractitionerPrivate>("/api/v1/me/practitioner", j))!.Id);
        Assert.Equal(pb.Id, (await b.GetFromJsonAsync<PractitionerPrivate>("/api/v1/me/practitioner", j))!.Id);
    }

    sealed class ProfilePgFactory(string cs) : ProfileFactory
    {
        protected override string Provider => "PostgreSQL";
        protected override string? ConnectionString => cs;
    }
}
