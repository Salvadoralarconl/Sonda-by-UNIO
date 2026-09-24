using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sonda.Access;
using Sonda.Access.Tests;
using Sonda.Infrastructure.Persistence;
using Sonda.Application.Simulation;
using Sonda.Domain.Profiles;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Http.Metadata;
using Xunit;

namespace Sonda.Api.Tests;

public sealed class ServerFactory(string connection) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "global.json"))) root = root.Parent;
        var workspace = root!.FullName;
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
        { ["SONDA_DATABASE"] = connection, ["Monitoring:AllowedRoots:0"] = @"C:\synthetic-logs", ["Monitoring:AllowedRoots:1"] = @"\\fixture-server\logs",
            ["Simulation:DotnetExecutable"] = Path.Combine(workspace, ".tools", "dotnet", "dotnet.exe"),
            ["Simulation:Assembly"] = Path.Combine(workspace, "tools", "Sonda.Simulator", "bin", typeof(SecurityTests).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyConfigurationAttribute), false).Cast<System.Reflection.AssemblyConfigurationAttribute>().Single().Configuration, "net10.0", "Sonda.Simulator.dll") }));
    }
}

[Collection("AccessPostgres")]
public sealed class SecurityTests(AccessDatabase database)
{
    private const string Password = "Synthetic long fixture passphrase 123";
    private async Task<(HttpClient Client, Guid Account, string Team)> Admin(ServerFactory factory)
    {
        var team = Guid.NewGuid().ToString("N");
        await using (var core = SondaDbContext.Open(database.Connection)) { core.Add(new TeamRow { TeamId = team, Name = "HTTP synthetic" }); await core.SaveChangesAsync(); }
        GrantResult grant;
        var login = Guid.NewGuid().ToString("N");
        using (var scope = factory.Services.CreateScope()) grant = await scope.ServiceProvider.GetRequiredService<AccountService>().BootstrapAsync(team, login);
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        await Csrf(client);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/v1/account-grants/redeem", new { token = grant.Token, password = Password })).StatusCode);
        var response = await client.PostAsJsonAsync("/api/v1/session/login", new { login, password = Password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), x => x.StartsWith("__Host-Sonda=", StringComparison.Ordinal) && x.Contains("secure", StringComparison.OrdinalIgnoreCase) && x.Contains("httponly", StringComparison.OrdinalIgnoreCase));
        await Csrf(client); return (client, grant.AccountId, team);
    }
    private static async Task Csrf(HttpClient client)
    {
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/v1/session/csrf");
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN"); client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
    }

    [Fact]
    public async Task Anonymous_provisioning_is_denied_and_login_requires_csrf()
    {
        await using var factory = new ServerFactory(database.Connection);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/v1/applications", new { operationId = Guid.NewGuid(), name = "Denied" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/session/login", new { login = "unknown", password = Password })).StatusCode);
    }

    [Fact]
    public async Task Credential_rate_limit_is_enforced_with_generic_failures()
    {
        await using var factory = new ServerFactory(database.Connection);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        await Csrf(client);
        for (var i = 0; i < 10; i++)
        {
            var response = await client.PostAsJsonAsync("/api/v1/session/login", new { login = Guid.NewGuid().ToString(), password = Password });
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Contains("invalid_credentials", await response.Content.ReadAsStringAsync());
        }
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.PostAsJsonAsync("/api/v1/session/login", new { login = "unknown", password = Password })).StatusCode);
    }

    [Fact]
    public async Task Every_protected_route_denies_anonymous_access_before_resource_binding()
    {
        await using var factory = new ServerFactory(database.Connection);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().ToArray();
        Assert.True(endpoints.Length >= 30);
        foreach (var endpoint in endpoints)
        {
            if (endpoint.Metadata.GetMetadata<Microsoft.AspNetCore.Authorization.IAllowAnonymous>() is not null) continue;
            var pattern = endpoint.RoutePattern.RawText!;
            var path = System.Text.RegularExpressions.Regex.Replace(pattern, @"\{([^}:]+)(?::[^}]+)?\}", m =>
                m.Groups[1].Value == "version" ? "1" : m.Groups[1].Value == "documentName" ? "v1" : Guid.NewGuid().ToString());
            foreach (var method in endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()!.HttpMethods)
            {
                using var request = new HttpRequestMessage(new HttpMethod(method), path);
                if (method != "GET") request.Content = JsonContent.Create(new { });
                var response = await client.SendAsync(request);
                Assert.True(response.StatusCode == HttpStatusCode.Unauthorized, method + " " + path + " returned " + response.StatusCode);
            }
        }
    }

    [Fact]
    public async Task Missing_expected_revision_is_not_interpreted_as_zero()
    {
        await using var factory = new ServerFactory(database.Connection); var (client, id, _) = await Admin(factory); using var owned = client;
        Assert.Equal((HttpStatusCode)428, (await client.PatchAsJsonAsync($"/api/v1/team/members/{id}", new { operationId = Guid.NewGuid(), role = "Admin", enabled = true })).StatusCode);
        Assert.Equal((HttpStatusCode)428, (await client.PostAsJsonAsync("/api/v1/profiles/unknown/activate", new { operationId = Guid.NewGuid(), version = 1, expectedVersion = 0 })).StatusCode);
    }

    [Fact]
    public async Task Openapi_documents_cookie_csrf_and_admin_provisioning()
    {
        await using var factory = new ServerFactory(database.Connection); var (client, _, _) = await Admin(factory); using var owned = client;
        var response = await client.GetAsync("/openapi/v1.json"); Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var document = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("__Host-Sonda", document.GetProperty("components").GetProperty("securitySchemes").GetProperty("sessionCookie").GetProperty("name").GetString());
        var create = document.GetProperty("paths").GetProperty("/api/v1/applications").GetProperty("post");
        Assert.Contains("Admin only", create.GetProperty("description").GetString());
        Assert.Contains(create.GetProperty("parameters").EnumerateArray(), x => x.GetProperty("name").GetString() == "X-CSRF-TOKEN");
        var root = new DirectoryInfo(AppContext.BaseDirectory); while (root is not null && !File.Exists(Path.Combine(root.FullName, "global.json"))) root = root.Parent;
        await File.WriteAllTextAsync(Path.Combine(root!.FullName, "artifacts", "phase5", "openapi.json"), document.GetRawText());
    }

    [Fact]
    public async Task Durable_idle_expiry_is_enforced_on_next_request()
    {
        await using var factory = new ServerFactory(database.Connection); var (client, id, _) = await Admin(factory); using var owned = client;
        await using (var db = AccessDbContext.Open(database.Connection))
            await db.Set<WebSession>().Where(x => x.AccountId == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.IdleExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-1)));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/session")).StatusCode);
    }

    [Fact]
    public async Task Admin_provisions_through_real_authenticated_pipeline_with_replay()
    {
        await using var factory = new ServerFactory(database.Connection); var (client, _, team) = await Admin(factory); using var owned = client;
        var request = new { operationId = Guid.NewGuid(), name = "HTTP CAM", description = "Synthetic" };
        var response = await client.PostAsJsonAsync("/api/v1/applications", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var first = await response.Content.ReadFromJsonAsync<ProvisionedResource>();
        var retry = await (await client.PostAsJsonAsync("/api/v1/applications", request)).Content.ReadFromJsonAsync<ProvisionedResource>();
        Assert.Equal(first, retry);
        var profileResponse = await client.PostAsJsonAsync($"/api/v1/applications/{first!.Id}/profiles", new { operationId = Guid.NewGuid(), name = "Draft" });
        Assert.Equal(HttpStatusCode.OK, profileResponse.StatusCode);
        var profile = await profileResponse.Content.ReadFromJsonAsync<ProvisionedResource>();
        var source = await client.PostAsJsonAsync($"/api/v1/profiles/{profile!.Id}/sources", new { operationId = Guid.NewGuid(), configuration = new { root = @"\\fixture-server\logs\CAM", enabled = true } });
        Assert.Equal(HttpStatusCode.OK, source.StatusCode);
        await using var db = SondaDbContext.Open(database.Connection);
        Assert.Empty(await db.Set<VersionRow>().Where(x => x.TeamId == team).ToArrayAsync());
        Assert.Empty(await db.Set<MonitoringSessionRow>().Where(x => x.TeamId == team).ToArrayAsync());
    }

    [Fact]
    public async Task Unknown_authority_fields_and_cross_origin_mutation_are_rejected()
    {
        await using var factory = new ServerFactory(database.Connection); var (client, _, _) = await Admin(factory); using var owned = client;
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/applications", new { operationId = Guid.NewGuid(), name = "Denied", teamId = "other" })).StatusCode);
        client.DefaultRequestHeaders.Add("Origin", "https://attacker.invalid");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/applications", new { operationId = Guid.NewGuid(), name = "Denied" })).StatusCode);
    }

    [Fact]
    public async Task Logout_revokes_durable_session_and_denies_next_request()
    {
        await using var factory = new ServerFactory(database.Connection); var (client, id, _) = await Admin(factory); using var owned = client;
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/v1/session/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/session")).StatusCode);
        await using var db = AccessDbContext.Open(database.Connection);
        Assert.All(await db.Set<WebSession>().Where(x => x.AccountId == id).ToArrayAsync(), x => Assert.NotNull(x.RevokedAt));
    }

    [Fact]
    public async Task Member_cannot_provision_and_disable_revokes_existing_cookie()
    {
        await using var factory = new ServerFactory(database.Connection); var (admin, _, _) = await Admin(factory); using var adminOwned = admin;
        var login = Guid.NewGuid().ToString("N"); var operationId = Guid.NewGuid();
        var invite = await admin.PostAsJsonAsync("/api/v1/team/members", new { operationId, login, role = "Member" });
        Assert.Equal(HttpStatusCode.OK, invite.StatusCode);
        var grant = (await invite.Content.ReadFromJsonAsync<GrantResult>())!;
        var replay = await (await admin.PostAsJsonAsync("/api/v1/team/members", new { operationId, login, role = "Member" })).Content.ReadFromJsonAsync<GrantResult>();
        Assert.True(replay!.SecretUnavailable); Assert.Null(replay.Token);
        using var member = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        await Csrf(member);
        Assert.Equal(HttpStatusCode.NoContent, (await member.PostAsJsonAsync("/api/v1/account-grants/redeem", new { token = grant.Token, password = Password })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await member.PostAsJsonAsync("/api/v1/session/login", new { login, password = Password })).StatusCode);
        await Csrf(member);
        foreach (var endpoint in factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(x => x.Metadata.GetOrderedMetadata<Microsoft.AspNetCore.Authorization.IAuthorizeData>().Any(p => p.Policy == "Admin")))
        {
            var path = System.Text.RegularExpressions.Regex.Replace(endpoint.RoutePattern.RawText!, @"\{([^}:]+)(?::[^}]+)?\}", m => m.Groups[1].Value == "version" ? "1" : m.Groups[1].Value == "documentName" ? "v1" : Guid.NewGuid().ToString());
            foreach (var method in endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()!.HttpMethods)
            {
                using var request = new HttpRequestMessage(new HttpMethod(method), path);
                if (method != "GET") request.Content = JsonContent.Create(new { });
                Assert.Equal(HttpStatusCode.Forbidden, (await member.SendAsync(request)).StatusCode);
            }
        }
        Assert.Equal(HttpStatusCode.Forbidden, (await member.PostAsJsonAsync("/api/v1/applications", new { operationId = Guid.NewGuid(), name = "Denied" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PatchAsJsonAsync($"/api/v1/team/members/{grant.AccountId}", new { operationId = Guid.NewGuid(), expectedRevision = 1, role = "Member", enabled = false })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await member.GetAsync("/api/v1/session")).StatusCode);
    }

    [Fact]
    public async Task Last_admin_cannot_disable_self()
    {
        await using var factory = new ServerFactory(database.Connection); var (client, id, _) = await Admin(factory); using var owned = client;
        var response = await client.PatchAsJsonAsync($"/api/v1/team/members/{id}", new { operationId = Guid.NewGuid(), expectedRevision = 1, role = "Member", enabled = false });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/session")).StatusCode);
    }

    [Fact]
    public async Task Strict_filters_chunked_limits_and_null_configuration_fail_without_mutation()
    {
        await using var factory = new ServerFactory(database.Connection); var (client, _, team) = await Admin(factory); using var owned = client;
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/v1/search?regex=true")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/v1/applications?size=1&size=2")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/applications?size=1")).StatusCode);
        var members = await client.GetFromJsonAsync<JsonElement>("/api/v1/team/members?size=1");
        Assert.False(string.IsNullOrWhiteSpace(Assert.Single(members.GetProperty("items").EnumerateArray()).GetProperty("login").GetString()));
        using var bytes = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(new string('x', 262145)));
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/applications") { Content = new StreamContent(bytes) };
        request.Headers.TransferEncodingChunked = true;
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await client.SendAsync(request)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.PutAsJsonAsync("/api/v1/profiles/anything/draft", new { operationId = Guid.NewGuid(), expectedRevision = 0, configuration = (object?)null })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.PutAsJsonAsync("/api/v1/profiles/anything/draft", new { operationId = Guid.NewGuid(), expectedRevision = 0, configuration = new { parsing = new { entryPattern = (string?)null } } })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/account-grants/redeem", new { token = (string?)null, password = Password })).StatusCode);
        await using var core = SondaDbContext.Open(database.Connection);
        Assert.Empty(await core.Set<ApplicationRow>().Where(x => x.TeamId == team).ToArrayAsync());
    }

    [Fact]
    public async Task Foreign_and_missing_configuration_resources_are_indistinguishable()
    {
        await using var factory = new ServerFactory(database.Connection); var (first, _, _) = await Admin(factory); using var one = first;
        var (second, _, _) = await Admin(factory); using var two = second;
        var app = (await (await first.PostAsJsonAsync("/api/v1/applications", new { operationId = Guid.NewGuid(), name = "Same name" })).Content.ReadFromJsonAsync<ProvisionedResource>())!;
        var profile = (await (await first.PostAsJsonAsync($"/api/v1/applications/{app.Id}/profiles", new { operationId = Guid.NewGuid(), name = "Same name" })).Content.ReadFromJsonAsync<ProvisionedResource>())!;
        foreach (var suffix in new[] { "draft", "versions", "sources", "simulations/" + Guid.NewGuid() })
        {
            var foreign = await second.GetAsync($"/api/v1/profiles/{profile.Id}/{suffix}");
            var missing = await second.GetAsync($"/api/v1/profiles/missing/{suffix}");
            Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode); Assert.Equal(missing.StatusCode, foreign.StatusCode);
        }
        Assert.Equal(HttpStatusCode.NotFound, (await second.PostAsJsonAsync($"/api/v1/applications/{app.Id}/profiles", new { operationId = Guid.NewGuid(), name = "Forbidden" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await second.PostAsJsonAsync($"/api/v1/profiles/{profile.Id}/sources", new { operationId = Guid.NewGuid(), configuration = new { root = @"C:\synthetic-logs" } })).StatusCode);
    }

    [Fact]
    public async Task New_configuration_requires_simulation_publication_and_explicit_activation()
    {
        await using var factory = new ServerFactory(database.Connection); var (client, _, team) = await Admin(factory); using var owned = client;
        var app = (await (await client.PostAsJsonAsync("/api/v1/applications", new { operationId = Guid.NewGuid(), name = "Workflow fixture" })).Content.ReadFromJsonAsync<ProvisionedResource>())!;
        var profile = (await (await client.PostAsJsonAsync($"/api/v1/applications/{app.Id}/profiles", new { operationId = Guid.NewGuid(), name = "New profile" })).Content.ReadFromJsonAsync<ProvisionedResource>())!;
        var fixture = SimulationJson.Deserialize<SimulationRequest>(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "fixtures", "mixed-orders.json")));
        var configuration = fixture.Profiles.Single() with { TeamId = team, ApplicationId = app.Id, Id = profile.Id,
            Policy = new InterpretationPolicy { RoutingContract = "synthetic-routing-v1", RecoveryCompatibility = "synthetic-recovery-v1" } };
        var activation = new { operationId = Guid.NewGuid(), version = 1, expectedRevision = 0, expectedVersion = 0 };
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync($"/api/v1/profiles/{profile.Id}/activate", activation)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/v1/profiles/{profile.Id}/draft", new { operationId = Guid.NewGuid(), expectedRevision = 0, configuration })).StatusCode);
        var sourceResponse = await client.PostAsJsonAsync($"/api/v1/profiles/{profile.Id}/sources", new { operationId = Guid.NewGuid(), configuration = new { root = @"C:\synthetic-logs", enabled = true } });
        Assert.Equal(HttpStatusCode.OK, sourceResponse.StatusCode);
        var source = (await sourceResponse.Content.ReadFromJsonAsync<ProvisionedResource>())!;
        var reportId = Guid.NewGuid();
        var preview = await client.PostAsJsonAsync($"/api/v1/profiles/{profile.Id}/simulations", new { operationId = reportId, expectedRevision = 1, asOf = fixture.AsOf, sampleDate = fixture.SampleDate, entries = fixture.Entries.Select(x => x with { ProfileId = profile.Id }).ToArray() });
        Assert.True(preview.IsSuccessStatusCode, await preview.Content.ReadAsStringAsync());
        var previewBody = await preview.Content.ReadFromJsonAsync<JsonElement>(); Assert.True(previewBody.GetProperty("report").GetProperty("complete").GetBoolean(), previewBody.GetProperty("report").GetRawText());
        var publish = await client.PostAsJsonAsync($"/api/v1/profiles/{profile.Id}/publish", new { operationId = Guid.NewGuid(), expectedRevision = 1, reportId, acknowledgeWarnings = true });
        Assert.True(publish.IsSuccessStatusCode, await publish.Content.ReadAsStringAsync());
        await using (var core = SondaDbContext.Open(database.Connection)) Assert.Empty(await core.Set<MonitoringSessionRow>().Where(x => x.TeamId == team).ToArrayAsync());
        var activated = await client.PostAsJsonAsync($"/api/v1/profiles/{profile.Id}/activate", activation);
        Assert.True(activated.IsSuccessStatusCode, await activated.Content.ReadAsStringAsync());
        var original = await activated.Content.ReadFromJsonAsync<JsonElement>();
        var repeated = await (await client.PostAsJsonAsync($"/api/v1/profiles/{profile.Id}/activate", activation)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(original.GetProperty("sessionId").GetGuid(), repeated.GetProperty("sessionId").GetGuid());
        await using var db = SondaDbContext.Open(database.Connection);
        Assert.Single(await db.Set<MonitoringSessionRow>().Where(x => x.TeamId == team).ToArrayAsync());
        Assert.Empty(await db.Set<RunRow>().Where(x => x.TeamId == team).ToArrayAsync());
        Assert.Empty(await db.Set<EvidenceRow>().Where(x => x.TeamId == team).ToArrayAsync());
        var change = new { operationId = Guid.NewGuid(), expectedRevision = 1, configuration = new { root = @"C:\synthetic-logs", enabled = false } };
        var changed = await client.PutAsJsonAsync($"/api/v1/profiles/{profile.Id}/sources/{source.Id}", change);
        Assert.True(changed.IsSuccessStatusCode, await changed.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/v1/profiles/{profile.Id}/sources/{source.Id}", change)).StatusCode);
        Assert.Equal(2, await db.Set<AcquisitionRevisionRow>().CountAsync(x => x.TeamId == team));
        var publishedSource = await db.Set<VersionSourceRow>().SingleAsync(x => x.TeamId == team);
        Assert.True(SimulationJson.Deserialize<Sonda.Application.Acquisition.SourceConfiguration>(publishedSource.Configuration).Enabled);
    }
}
