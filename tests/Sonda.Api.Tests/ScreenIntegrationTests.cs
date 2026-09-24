using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sonda.Access;
using Sonda.Access.Tests;
using Sonda.Application.Acquisition;
using Sonda.Application.Persistence;
using Sonda.Application.Processing;
using Sonda.Application.Simulation;
using Sonda.Domain.Profiles;
using Sonda.Infrastructure.Persistence;
using Xunit;

namespace Sonda.Api.Tests;

[Collection("AccessPostgres")]
public sealed class ScreenIntegrationTests(AccessDatabase database)
{
    [Fact]
    public async Task Real_browser_investigates_and_updates_incidents_and_preserves_evidence_grouping()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "global.json"))) root = root.Parent;
        var workspace = root!.FullName;
        using var rsa = RSA.Create(2048);
        var certificateRequest = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder(); names.AddDnsName("localhost"); names.AddIpAddress(IPAddress.Loopback);
        certificateRequest.CertificateExtensions.Add(names.Build());
        using var generated = certificateRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddHours(1));
        using var certificate = X509CertificateLoader.LoadPkcs12(generated.Export(X509ContentType.Pfx), null);
        await using var parent = new ServerFactory(database.Connection);
        await using var factory = parent.WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
        { ["Frontend:Root"] = Path.Combine(workspace, "src", "Sonda.Web", "dist") })));
        factory.UseKestrel(o => o.Listen(IPAddress.Loopback, 0, l => l.UseHttps(certificate)));
        factory.StartServer();
        var team = Guid.NewGuid().ToString("N");
        await using (var db = SondaDbContext.Open(database.Connection)) { db.Add(new TeamRow { TeamId = team, Name = "Screen fixture" }); await db.SaveChangesAsync(); }
        var login = "Maria-" + Guid.NewGuid().ToString("N"); var password = "Synthetic screen passphrase " + Guid.NewGuid().ToString("N");
        using (var services = factory.Services.CreateScope())
        {
            var accounts = services.ServiceProvider.GetRequiredService<AccountService>();
            var grant = await accounts.BootstrapAsync(team, login); await accounts.RedeemAsync(grant.Token!, password);
        }
        await Seed(team, "app-cam", "CAM", shared: true);
        await Seed(team, "app-uniteller", "UNITELLER", shared: false);
        var start = new ProcessStartInfo("node") { WorkingDirectory = workspace, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("tools/verify-screen-integration.cjs");
        start.Environment["SONDA_BROWSER_URL"] = factory.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        start.Environment["SONDA_BROWSER_LOGIN"] = login; start.Environment["SONDA_BROWSER_PASSWORD"] = password;
        start.Environment.Remove("SONDA_TEST_ADMIN"); start.Environment.Remove("SONDA_DATABASE");
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        try { await process.WaitForExitAsync(timeout.Token); } catch { process.Kill(true); throw; }
        Assert.True(process.ExitCode == 0, (await stdout) + "\n" + (await stderr));
        await using var verify = SondaDbContext.Open(database.Connection);
        Assert.Single(await verify.Set<IncidentRow>().Where(x => x.TeamId == team && x.Status == "Investigating").ToArrayAsync());
        Assert.Equal(11, await verify.Set<FactRow>().CountAsync(x => x.TeamId == team));
    }

    private async Task Seed(string team, string application, string name, bool shared)
    {
        var sample = SimulationJson.Deserialize<SimulationRequest>(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "fixtures", "mixed-orders.json")));
        var profile = sample.Profiles.Single() with { TeamId = team, ApplicationId = application, Id = application + "-profile", Policy = new InterpretationPolicy { RoutingContract = "screen-route", RecoveryCompatibility = "screen-recovery" } };
        var request = sample with { Profiles = [profile], Entries = sample.Entries.Select(e => e with { ProfileId = profile.Id }).ToList(), ServerTimeZoneId = TimeZoneInfo.Local.Id };
        var session = await new PostgresConfigurationStore(database.Connection).CreateSessionAsync(request);
        var scope = new ProcessingScope(team, session, application);
        var lines = sample.Entries.Select(e => shared ? e.Raw : e.Raw.Replace("Unable to send order", "Order sent")).ToList();
        if (shared) lines.AddRange(["10:01:00 CAM process started", "10:01:01 Finding order OrderID=shared-1", "10:01:02 Finding order OrderID=shared-2", "10:01:03 CAM process completed", "10:01:04 Outside cycle unmatched evidence"]);
        var processor = new PostgresPolicyStore(database.Connection);
        var processed = DateTimeOffset.UtcNow.AddMinutes(-1);
        for (var i = 0; i < lines.Count; i++) await processor.ExecuteAsync(scope, profile.Id, new PolicyCommand
        { Id = Guid.NewGuid(), Kind = PolicyCommandKind.Evidence, Sequence = i + 1, ProcessedAt = processed.AddSeconds(i), Raw = lines[i], EvidenceKey = "screen-" + i, SampleDate = sample.SampleDate });
        await new PostgresIngestionStore(database.Connection).RegisterAsync(scope, new SourceConfiguration { ProfileId = profile.Id, SourceKey = "sample", Root = @"C:\synthetic-logs", SampleDate = sample.SampleDate, Enabled = false }, Environment.MachineName);
        await using var db = SondaDbContext.Open(database.Connection);
        var app = await db.Set<ApplicationRow>().SingleAsync(x => x.TeamId == team && x.ApplicationId == application);
        app.Name = name; app.Revision++;
        await db.SaveChangesAsync();
    }
}
