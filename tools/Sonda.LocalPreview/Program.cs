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
using Sonda.Api.Tests;
using Sonda.Application.Acquisition;
using Sonda.Application.Persistence;
using Sonda.Application.Processing;
using Sonda.Application.Simulation;
using Sonda.Domain.Profiles;
using Sonda.Infrastructure.Persistence;
namespace Sonda.LocalPreview;
internal sealed class Preview(AccessDatabase database)
{
 public static async Task Main()
 {
  var database = new AccessDatabase();
  await database.InitializeAsync();
  try { await new Preview(database).Run(); }
  finally { await database.DisposeAsync(); File.WriteAllText(".tools/local-preview-status.json", "{\"state\":\"stopped\"}"); }
 }
 private async Task Run()
 {
  var workspace = Directory.GetCurrentDirectory();
  using var rsa=RSA.Create(2048);
  var request=new CertificateRequest("CN=localhost",rsa,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1);
  var names=new SubjectAlternativeNameBuilder();names.AddDnsName("localhost");names.AddIpAddress(IPAddress.Loopback);request.CertificateExtensions.Add(names.Build());
  using var generated=request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5),DateTimeOffset.UtcNow.AddHours(8));
  using var cert=X509CertificateLoader.LoadPkcs12(generated.Export(X509ContentType.Pfx),null);
  await using var parent=new ServerFactory(database.Connection);
  await using var factory=parent.WithWebHostBuilder(b=>b.UseContentRoot(Path.Combine(workspace,"src","Sonda.Server")).ConfigureAppConfiguration((_,c)=>c.AddInMemoryCollection(new Dictionary<string,string?>{["Frontend:Root"]=Path.Combine(workspace,"src","Sonda.Web","dist"),["Monitoring:RunEnabled"]="false"})));
  factory.UseKestrel(o=>o.Listen(IPAddress.Loopback,0,l=>l.UseHttps(cert)));factory.StartServer();
  var team=Guid.NewGuid().ToString("N");
  await using(var db=SondaDbContext.Open(database.Connection)){db.Add(new TeamRow{TeamId=team,Name="Local visual preview"});await db.SaveChangesAsync();}
  var login="Salvador";var password="Local preview "+Guid.NewGuid().ToString("N");
  using(var scope=factory.Services.CreateScope()){var accounts=scope.ServiceProvider.GetRequiredService<AccountService>();var grant=await accounts.BootstrapAsync(team,login);await accounts.RedeemAsync(grant.Token!,password);}
  await Seed(team,"app-cam","CAM",true);await Seed(team,"app-uniteller","UNITELLER",false);await Seed(team,"app-digicel","DIGICEL",true);await Seed(team,"app-terrapay","TERRAPAY",true);
  var start=new ProcessStartInfo("node"){WorkingDirectory=workspace,UseShellExecute=false,CreateNoWindow=true};
  start.ArgumentList.Add("tools/open-local-preview.cjs");
  start.Environment["SONDA_BROWSER_URL"]=factory.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
  start.Environment["SONDA_BROWSER_LOGIN"]=login;start.Environment["SONDA_BROWSER_PASSWORD"]=password;start.Environment["SONDA_PREVIEW_HOST_PID"]=Environment.ProcessId.ToString();
  start.Environment.Remove("SONDA_TEST_ADMIN");start.Environment.Remove("SONDA_DATABASE");
  using var browser=Process.Start(start)!;
  await browser.WaitForExitAsync();
  if(browser.ExitCode!=0)throw new InvalidOperationException("Local preview browser did not start successfully.");
 }
    private async Task Seed(string team, string application, string name, bool shared)
    {
        var sample = SimulationJson.Deserialize<SimulationRequest>(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "fixtures", "mixed-orders.json")));
        var profile = sample.Profiles.Single() with { TeamId = team, ApplicationId = application, Id = application + "-profile", Policy = new InterpretationPolicy { RoutingContract = "screen-route", RecoveryCompatibility = "screen-recovery" } };
        if(name == "TERRAPAY") profile = profile with { OrderFailureSeverity = Classification.Error, Rules = profile.Rules.Select(r => r.Classification == Classification.Warning ? r with { Classification = Classification.Error } : r).ToList() };
        var request = sample with { Profiles = [profile], Entries = sample.Entries.Select(e => e with { ProfileId = profile.Id }).ToList(), ServerTimeZoneId = TimeZoneInfo.Local.Id };
        var session = await new PostgresConfigurationStore(database.Connection).CreateSessionAsync(request);
        var scope = new ProcessingScope(team, session, application);
        var lines = sample.Entries.Select(e => shared ? e.Raw : e.Raw.Replace("Unable to send order", "Order sent")).ToList();
        if (shared) lines.AddRange(["10:01:00 CAM process started", "10:01:01 Finding order OrderID=shared-1", "10:01:02 Finding order OrderID=shared-2", "10:01:03 CAM process completed", "10:01:04 Outside cycle unmatched evidence"]);
        var processor = new PostgresPolicyStore(database.Connection);
        var processed = DateTimeOffset.UtcNow.AddMinutes(-1);
        for (var i = 0; i < lines.Count; i++) await processor.ExecuteAsync(scope, profile.Id, new PolicyCommand
        { Id = Guid.NewGuid(), Kind = PolicyCommandKind.Evidence, Sequence = i + 1, ProcessedAt = processed.AddSeconds(i), Raw = lines[i], EvidenceKey = "screen-" + i, SampleDate = DateOnly.FromDateTime(DateTime.Now) });
        await new PostgresIngestionStore(database.Connection).RegisterAsync(scope, new SourceConfiguration { ProfileId = profile.Id, SourceKey = "sample", Root = @"C:\synthetic-logs", SampleDate = DateOnly.FromDateTime(DateTime.Now), Enabled = false }, Environment.MachineName);
        await using var db = SondaDbContext.Open(database.Connection);
        var app = await db.Set<ApplicationRow>().SingleAsync(x => x.TeamId == team && x.ApplicationId == application);
        app.Name = name; app.Revision++;
        await db.SaveChangesAsync();
    }

}


