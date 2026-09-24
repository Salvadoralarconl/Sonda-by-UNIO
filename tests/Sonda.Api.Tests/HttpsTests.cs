using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Sonda.Access;
using Sonda.Access.Tests;
using Sonda.Infrastructure.Persistence;
using Xunit;
using Npgsql;
using Microsoft.EntityFrameworkCore;
using Sonda.Application.Simulation;
using Sonda.Application.Persistence;

namespace Sonda.Api.Tests;

[Collection("AccessPostgres")]
public sealed class HttpsTests(AccessDatabase database)
{
    [Fact]
    public async Task Real_https_cookie_survives_process_restart_with_encrypted_keys_and_fails_without_keys()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "global.json"))) root = root.Parent;
        var workspace = root!.FullName;
        var directory = Path.Combine(workspace, "artifacts", "phase5", "temporary-https-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        using var rsa = RSA.Create(2048);
        var certificateRequest = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder(); names.AddDnsName("localhost"); names.AddIpAddress(IPAddress.Loopback);
        certificateRequest.CertificateExtensions.Add(names.Build());
        using var certificate = certificateRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(1));
        var certificatePath = Path.Combine(directory, "synthetic.pfx"); await File.WriteAllBytesAsync(certificatePath, certificate.Export(X509ContentType.Pfx));
        var login = Guid.NewGuid().ToString("N"); const string password = "Synthetic HTTPS fixture passphrase";
        var team = Guid.NewGuid().ToString("N");
        await using (var core = SondaDbContext.Open(database.Connection)) { core.Add(new TeamRow { TeamId = team, Name = "HTTPS fixture" }); await core.SaveChangesAsync(); }
        var sample = SimulationJson.Deserialize<SimulationRequest>(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "fixtures", "mixed-orders.json")));
        sample = sample with { Profiles = [sample.Profiles.Single() with { TeamId = team }] };
        var sessionId = await new PostgresConfigurationStore(database.Connection).CreateSessionAsync(sample);
        var processingScope = new ProcessingScope(team, sessionId, sample.Profiles[0].ApplicationId);
        for (var i = 0; i < sample.Entries.Count; i++)
            await new PostgresProcessingStore(database.Connection).ProcessAsync(new(processingScope, sample.Profiles[0].Id, Guid.NewGuid(), "sample", "https-restore-fixture", i, i + 1, i + 1, sample.Entries[i].Raw, sample.Entries[i].ProcessedAt, sample.SampleDate));
        var coreBefore = await CoreFingerprint(database.Connection);
        await using (var factory = new ServerFactory(database.Connection))
        {
            using var scope = factory.Services.CreateScope(); var users = scope.ServiceProvider.GetRequiredService<UserManager<Account>>();
            var account = new Account { Id = Guid.NewGuid(), UserName = login, LockoutEnabled = true };
            Assert.True((await users.CreateAsync(account, password)).Succeeded);
            var access = scope.ServiceProvider.GetRequiredService<AccessDbContext>(); access.Add(new Membership { TeamId = team, AccountId = account.Id, Role = "Admin", Enabled = true }); await access.SaveChangesAsync();
        }
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
        var address = new Uri($"https://127.0.0.1:{port}");
        using var handler = new HttpClientHandler { CookieContainer = new CookieContainer(), AllowAutoRedirect = false,
            ServerCertificateCustomValidationCallback = (_, presented, _, _) => presented?.Thumbprint == certificate.Thumbprint };
        using var client = new HttpClient(handler) { BaseAddress = address, Timeout = TimeSpan.FromSeconds(10) };
        Process? process = null;
        var hostConnection = database.Connection;
        var restoredDatabase = "sonda_https_restore_" + Guid.NewGuid().ToString("N");
        var restoredCreated = false;
        try
        {
            process = await Start("keys");
            var csrf = await client.GetFromJsonAsync<JsonElement>("/api/v1/session/csrf"); client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
            var response = await client.PostAsJsonAsync("/api/v1/session/login", new { login, password });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var cookie = response.Headers.GetValues("Set-Cookie").Single(x => x.StartsWith("__Host-Sonda=", StringComparison.Ordinal));
            Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase); Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("domain=", cookie, StringComparison.OrdinalIgnoreCase);
            var keyFiles = Directory.GetFiles(Path.Combine(directory, "keys"), "*.xml"); Assert.NotEmpty(keyFiles);
            Assert.All(keyFiles, file => Assert.Contains("encryptedSecret", File.ReadAllText(file), StringComparison.Ordinal));
            var backup = Path.Combine(directory, "synthetic.dump");
            await Pg("pg_dump.exe", database.Connection, "-Fc", "-f", backup);
            await using (var admin = new NpgsqlConnection(Environment.GetEnvironmentVariable("SONDA_TEST_ADMIN")))
            { await admin.OpenAsync(); await new NpgsqlCommand($"CREATE DATABASE {restoredDatabase}", admin).ExecuteNonQueryAsync(); restoredCreated = true; }
            hostConnection = new NpgsqlConnectionStringBuilder(database.Connection) { Database = restoredDatabase }.ConnectionString;
            await Pg("pg_restore.exe", hostConnection, "--no-owner", "--exit-on-error", backup);
            var restoredKeys = Path.Combine(directory, "restored-keys"); Directory.CreateDirectory(restoredKeys);
            foreach (var file in keyFiles) File.Copy(file, Path.Combine(restoredKeys, Path.GetFileName(file)));
            await Stop(process); process = await Start("restored-keys");
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/session")).StatusCode);
            Assert.Equal(coreBefore, await CoreFingerprint(hostConnection));
            await Stop(process); process = await Start("different-keys");
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/session")).StatusCode);
        }
        finally
        {
            if (process is not null) await Stop(process);
            if (restoredCreated)
            {
                NpgsqlConnection.ClearAllPools();
                await using var admin = new NpgsqlConnection(Environment.GetEnvironmentVariable("SONDA_TEST_ADMIN")); await admin.OpenAsync();
                await new NpgsqlCommand($"DROP DATABASE {restoredDatabase} WITH (FORCE)", admin).ExecuteNonQueryAsync();
            }
            // Generated, fixed child of the project artifact directory only.
            var artifactRoot = Path.GetFullPath(Path.Combine(workspace, "artifacts", "phase5")) + Path.DirectorySeparatorChar;
            Assert.StartsWith(artifactRoot, Path.GetFullPath(directory), StringComparison.OrdinalIgnoreCase);
            Directory.Delete(Path.GetFullPath(directory), recursive: true);
        }
        async Task<Process> Start(string keys)
        {
            var start = new ProcessStartInfo(Path.Combine(workspace, ".tools", "dotnet", "dotnet.exe")) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = workspace, RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add(Path.Combine(workspace, "src", "Sonda.Server", "bin", typeof(SecurityTests).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyConfigurationAttribute), false).Cast<System.Reflection.AssemblyConfigurationAttribute>().Single().Configuration, "net10.0", "Sonda.Server.dll"));
            start.Environment["ASPNETCORE_ENVIRONMENT"] = "Production"; start.Environment["DOTNET_ENVIRONMENT"] = "Production";
            start.Environment["SONDA_DATABASE"] = hostConnection; start.Environment["ASPNETCORE_URLS"] = address.ToString();
            start.Environment["Kestrel__Certificates__Default__Path"] = certificatePath;
            start.Environment["Security__KeyDirectory"] = Path.Combine(directory, keys);
            start.Environment["Security__KeyCertificatePath"] = certificatePath; start.Environment["Monitoring__RunEnabled"] = "false";
            var p = Process.Start(start)!;
            _ = p.StandardOutput.ReadToEndAsync(); _ = p.StandardError.ReadToEndAsync();
            for (var n = 0; n < 80; n++)
            {
                if (p.HasExited) { p.Dispose(); throw new InvalidOperationException("Synthetic HTTPS host exited during startup."); }
                try { if ((await client.GetAsync("/health/live")).IsSuccessStatusCode) return p; } catch (HttpRequestException) { }
                await Task.Delay(100);
            }
            await Stop(p); throw new TimeoutException("Synthetic HTTPS readiness timeout.");
        }
        static async Task Stop(Process p) { if (!p.HasExited) p.Kill(entireProcessTree: true); await p.WaitForExitAsync(); p.Dispose(); }
        async Task<string> CoreFingerprint(string connection)
        {
            await using var db = SondaDbContext.Open(connection);
            return SimulationJson.Hash(new {
                profiles = await db.Set<VersionRow>().Where(x => x.TeamId == team).OrderBy(x => x.ProfileId).ThenBy(x => x.Version).ToArrayAsync(),
                runs = await db.Set<RunRow>().Where(x => x.TeamId == team).OrderBy(x => x.Id).ToArrayAsync(),
                incidents = await db.Set<IncidentRow>().Where(x => x.TeamId == team).OrderBy(x => x.Id).ToArrayAsync(),
                receipts = await db.Set<ReceiptRow>().Where(x => x.TeamId == team).OrderBy(x => x.Sequence).ToArrayAsync(),
                metrics = await db.Set<FactRow>().Where(x => x.TeamId == team).OrderBy(x => x.RunId).ToArrayAsync() });
        }
        async Task Pg(string executable, string connection, params string[] extra)
        {
            var settings = new NpgsqlConnectionStringBuilder(connection);
            var start = new ProcessStartInfo(Path.Combine(workspace, ".tools", "postgresql", "pgsql", "bin", executable)) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
            start.Environment["PGPASSWORD"] = settings.Password;
            foreach (var argument in new[] { "-h", settings.Host!, "-p", settings.Port.ToString(System.Globalization.CultureInfo.InvariantCulture), "-U", settings.Username! }) start.ArgumentList.Add(argument);
            if (executable == "pg_restore.exe") start.ArgumentList.Add("-d");
            start.ArgumentList.Add(settings.Database!);
            foreach (var argument in extra) start.ArgumentList.Add(argument);
            using var p = Process.Start(start)!;
            var error = p.StandardError.ReadToEndAsync(); var output = p.StandardOutput.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try { await p.WaitForExitAsync(timeout.Token); }
            catch { if (!p.HasExited) p.Kill(entireProcessTree: true); throw; }
            await output; Assert.True(p.ExitCode == 0, await error);
        }
    }
}
