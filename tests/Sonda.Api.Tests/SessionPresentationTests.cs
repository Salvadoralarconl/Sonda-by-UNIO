using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Sonda.Access;
using Sonda.Access.Tests;
using Sonda.Api.Contracts;
using Sonda.Infrastructure.Persistence;
using Xunit;

namespace Sonda.Api.Tests;

[Collection("AccessPostgres")]
public sealed class SessionPresentationTests(AccessDatabase database)
{
    [Fact]
    public async Task Display_name_is_the_authenticated_accounts_login_and_existing_session_fields_are_preserved()
    {
        await using var factory = new ServerFactory(database.Connection);
        foreach (var displayName in new[] { "Maria-" + Guid.NewGuid().ToString("N"), "David-" + Guid.NewGuid().ToString("N") })
        {
            const string password = "Synthetic session presentation passphrase";
            var team = Guid.NewGuid().ToString("N");
            await using (var core = SondaDbContext.Open(database.Connection))
            { core.Add(new TeamRow { TeamId = team, Name = "Session presentation" }); await core.SaveChangesAsync(); }
            Guid accountId;
            using (var scope = factory.Services.CreateScope())
            {
                var accounts = scope.ServiceProvider.GetRequiredService<AccountService>();
                var grant = await accounts.BootstrapAsync(team, displayName);
                accountId = grant.AccountId;
                await accounts.RedeemAsync(grant.Token!, password);
            }
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
                { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
            var csrf = await client.GetFromJsonAsync<JsonElement>("/api/v1/session/csrf");
            client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
            var login = await client.PostAsJsonAsync("/api/v1/session/login", new { login = displayName, password });
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
            var session = await client.GetFromJsonAsync<SessionDto>("/api/v1/session");
            Assert.NotNull(session);
            Assert.Equal(displayName, session.DisplayName);
            Assert.Equal(accountId, session.AccountId);
            Assert.Equal(team, session.TeamId);
            Assert.Equal("Admin", session.Role);
            Assert.Equal(1, session.Revision);
            Assert.Equal(TimeZoneInfo.Local.Id, session.ReportingTimeZoneId);
            Assert.NotNull(session.ReportingTimeZoneIanaId);
            Assert.InRange(session.AsOf, DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(1));
        }
    }

    [Fact]
    public async Task Anonymous_request_cannot_obtain_a_display_name()
    {
        await using var factory = new ServerFactory(database.Connection);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
            { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/session")).StatusCode);
    }
}
