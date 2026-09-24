using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sonda.Access;
using Sonda.Access.Tests;
using Sonda.Infrastructure.Persistence;
using Xunit;
namespace Sonda.Api.Tests;

[Collection("AccessPostgres")]
public sealed class FrontendBrowserTests(AccessDatabase database)
{
 [Fact]
 public async Task Real_browser_uses_https_cookie_csrf_and_postgres_for_provisioning()
 {
  var root=new DirectoryInfo(AppContext.BaseDirectory);while(root is not null&&!File.Exists(Path.Combine(root.FullName,"global.json")))root=root.Parent;
  var workspace=root!.FullName;
  Assert.True(File.Exists(Path.Combine(workspace,"src","Sonda.Web","dist","index.html")),"Build Sonda.Web before browser integration tests.");
  using var rsa=RSA.Create(2048);var request=new CertificateRequest("CN=localhost",rsa,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1);
  var names=new SubjectAlternativeNameBuilder();names.AddDnsName("localhost");names.AddIpAddress(IPAddress.Loopback);request.CertificateExtensions.Add(names.Build());
  using var generated=request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5),DateTimeOffset.UtcNow.AddHours(1));
  using var cert=X509CertificateLoader.LoadPkcs12(generated.Export(X509ContentType.Pfx),null);
  await using var parent=new ServerFactory(database.Connection);
  await using var factory=parent.WithWebHostBuilder(b=>b.ConfigureAppConfiguration((_,c)=>c.AddInMemoryCollection(new Dictionary<string,string?>{["Frontend:Root"]=Path.Combine(workspace,"src","Sonda.Web","dist")})));
  factory.UseKestrel(o=>o.Listen(IPAddress.Loopback,0,l=>l.UseHttps(cert)));
  factory.StartServer();
  var team=Guid.NewGuid().ToString("N");await using(var db=SondaDbContext.Open(database.Connection)){db.Add(new TeamRow{TeamId=team,Name="Synthetic browser team"});await db.SaveChangesAsync();}
  var login="Browser-"+Guid.NewGuid().ToString("N");var password="Synthetic browser passphrase "+Guid.NewGuid().ToString("N");
  using(var scope=factory.Services.CreateScope()){var accounts=scope.ServiceProvider.GetRequiredService<AccountService>();var grant=await accounts.BootstrapAsync(team,login);await accounts.RedeemAsync(grant.Token!,password);}
  var address=factory.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
  var start=new ProcessStartInfo("node"){WorkingDirectory=workspace,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
  start.ArgumentList.Add("tools/verify-frontend-browser.cjs");start.Environment["SONDA_BROWSER_URL"]=address;start.Environment["SONDA_BROWSER_LOGIN"]=login;start.Environment["SONDA_BROWSER_PASSWORD"]=password;
  // The child browser needs no database credentials.
  start.Environment.Remove("SONDA_TEST_ADMIN");start.Environment.Remove("SONDA_DATABASE");
  using var process=Process.Start(start)!;var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(3));
  try{await process.WaitForExitAsync(timeout.Token);}catch{process.Kill(true);throw;}
  Assert.True(process.ExitCode==0,(await stdout)+"\n"+(await stderr));
 }
}
