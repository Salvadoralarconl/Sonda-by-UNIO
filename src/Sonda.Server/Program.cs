using System.Security.Claims;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Sonda.Access;
using Sonda.Application.Acquisition;
using System.Threading.RateLimiting;
using Sonda.Server.Queries;
using Sonda.Api.Contracts;
using Sonda.Server.Profiles;
using Sonda.Server.Hosting;
using Microsoft.OpenApi;

var operatorCommand = args.FirstOrDefault() is "bootstrap-admin" or "recover-admin";
var builder = WebApplication.CreateBuilder(operatorCommand ? [] : args);
builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.None);
builder.Services.AddDbContext<AccessDbContext>((sp, o) => o.UseNpgsql(Connection(sp), x => x.MigrationsHistoryTable("__AccessMigrationsHistory", "sonda_access")));
builder.Services.AddIdentityCore<Account>(o =>
{
    o.Password.RequiredLength = 15; o.Password.RequireDigit = false; o.Password.RequireUppercase = false;
    o.Password.RequireLowercase = false; o.Password.RequireNonAlphanumeric = false;
    o.Lockout.MaxFailedAccessAttempts = 5; o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
}).AddEntityFrameworkStores<AccessDbContext>().AddDefaultTokenProviders();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<AccountService>();
builder.Services.AddScoped<OperationLedger>();
builder.Services.AddScoped(sp => new SourceService(Connection(sp), sp.GetRequiredService<MonitoringPathPolicy>(), sp.GetRequiredService<ProvisioningStore>(), sp.GetRequiredService<OperationLedger>(), sp.GetRequiredService<ApplicationAdmission>()));
builder.Services.AddSingleton(sp => new ApplicationAdmission(Connection(sp), sp.GetRequiredService<MonitoringPathPolicy>(), sp.GetRequiredService<ILogger<ApplicationAdmission>>(), hostAuthority: sp.GetRequiredService<IConfiguration>()["Monitoring:HostAuthority"] ?? Environment.MachineName));
builder.Services.AddHostedService<SharedMonitoringWorker>();
builder.Services.AddSingleton(sp => new InitialActivationStore(Connection(sp), sp.GetRequiredService<MonitoringPathPolicy>(), sp.GetRequiredService<IConfiguration>()["Monitoring:HostAuthority"] ?? Environment.MachineName));
builder.Services.AddScoped(sp => new WorkflowService(Connection(sp), sp.GetRequiredService<OperationLedger>(), sp.GetRequiredService<ApplicationAdmission>(), sp.GetRequiredService<InitialActivationStore>()));
builder.Services.AddSingleton<SimulationProcess>();
builder.Services.AddScoped(sp => new ProfileService(Connection(sp), sp.GetRequiredService<AccessDbContext>(), sp.GetRequiredService<SimulationProcess>()));
builder.Services.AddSingleton<CursorCodec>();
builder.Services.AddScoped(sp => new DashboardQueries(Connection(sp), sp.GetRequiredService<TimeProvider>()));
builder.Services.AddScoped(sp => new InvestigationQueries(Connection(sp), sp.GetRequiredService<CursorCodec>(), sp.GetRequiredService<TimeProvider>()));
builder.Services.AddScoped(sp => new SearchQueries(Connection(sp), sp.GetRequiredService<CursorCodec>(), sp.GetRequiredService<TimeProvider>()));
builder.Services.AddScoped(sp => new DetailQueries(Connection(sp), sp.GetRequiredService<AccessDbContext>(), sp.GetRequiredService<CursorCodec>(), sp.GetRequiredService<TimeProvider>()));
builder.Services.AddSingleton(sp => new MonitoringPathPolicy(sp.GetRequiredService<IConfiguration>().GetSection("Monitoring:AllowedRoots").Get<string[]>() ?? []));
builder.Services.AddScoped(sp => new ProvisioningStore(Connection(sp), sp.GetRequiredService<MonitoringPathPolicy>()));
var protection = builder.Services.AddDataProtection().SetApplicationName("SONDA.SharedServer");
if (!builder.Environment.IsEnvironment("Testing"))
{
    var keyPath = builder.Configuration["Security:KeyDirectory"] ?? throw new InvalidOperationException("Protected key directory required.");
    protection.PersistKeysToFileSystem(new DirectoryInfo(keyPath));
    if (builder.Configuration["Security:KeyCertificatePath"] is { } certificatePath)
        protection.ProtectKeysWithCertificate(System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadPkcs12FromFile(certificatePath, builder.Configuration["Security:KeyCertificatePassword"]));
    else protection.ProtectKeysWithCertificate(builder.Configuration["Security:KeyCertificateThumbprint"] ?? throw new InvalidOperationException("Key encryption certificate required."));
}
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(o =>
{
    o.Cookie.Name = "__Host-Sonda"; o.Cookie.Path = "/"; o.Cookie.HttpOnly = true;
    o.Cookie.SecurePolicy = CookieSecurePolicy.Always; o.Cookie.SameSite = SameSiteMode.Lax;
    o.ExpireTimeSpan = TimeSpan.FromHours(8); o.SlidingExpiration = false;
    o.Events.OnRedirectToLogin = c => { c.Response.StatusCode = 401; return Task.CompletedTask; };
    o.Events.OnRedirectToAccessDenied = c => { c.Response.StatusCode = 403; return Task.CompletedTask; };
    o.Events.OnValidatePrincipal = async c =>
    {
        var value = c.Principal?.FindFirstValue("session");
        var actor = Guid.TryParse(value, out var id) ? await c.HttpContext.RequestServices.GetRequiredService<AccountService>().ValidateSessionAsync(id, c.HttpContext.RequestAborted) : null;
        if (actor is null) { c.RejectPrincipal(); return; }
        c.HttpContext.Items["Actor"] = actor;
    };
});
builder.Services.AddAuthorization(o =>
{
    o.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
    o.AddPolicy("Admin", p => p.RequireAssertion(c => c.Resource is HttpContext h && h.Items["Actor"] is Actor { Role: "Admin" }));
});
builder.Services.AddAntiforgery(o => { o.HeaderName = "X-CSRF-TOKEN"; o.Cookie.Name = "__Host-Sonda-Csrf"; o.Cookie.SecurePolicy = CookieSecurePolicy.Always; o.Cookie.Path = "/"; o.Cookie.HttpOnly = true; });
builder.Services.ConfigureHttpJsonOptions(o => { o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()); o.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow; });
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 1048576);
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = 429;
    o.AddPolicy("credentials", context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "local", _ => new FixedWindowRateLimiterOptions
    { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true }));
});
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, _, _) =>
    {
        document.Info.Title = "SONDA shared backend"; document.Info.Version = "v1";
        document.Components ??= new(); document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes["sessionCookie"] = new OpenApiSecurityScheme { Type = SecuritySchemeType.ApiKey, In = ParameterLocation.Cookie, Name = "__Host-Sonda", Description = "Authenticated durable SONDA session; current team membership is checked on every request." };
        document.Security = [new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("sessionCookie", document)] = [] }];
        return Task.CompletedTask;
    });
    options.AddOperationTransformer((operation, context, _) =>
    {
        var metadata = context.Description.ActionDescriptor.EndpointMetadata;
        if (metadata.OfType<IAllowAnonymous>().Any()) operation.Security = [];
        if (metadata.OfType<IAuthorizeData>().Any(x => x.Policy == "Admin")) operation.Description = "Admin only. Team is derived from the authenticated session.";
        if (context.Description.HttpMethod is not ("GET" or "HEAD" or "OPTIONS"))
        {
            operation.Parameters ??= [];
            operation.Parameters.Add(new OpenApiParameter { Name = "X-CSRF-TOKEN", In = ParameterLocation.Header, Required = true,
                Description = "Token from /api/v1/session/csrf, paired with its secure antiforgery cookie.", Schema = new OpenApiSchema { Type = JsonSchemaType.String } });
        }
        return Task.CompletedTask;
    });
});
var app = builder.Build();
if (operatorCommand)
{
    if (args.Length != 3) throw new ArgumentException("bootstrap-admin|recover-admin <existing-team-id> <login>");
    using var scope = app.Services.CreateScope();
    var accounts = scope.ServiceProvider.GetRequiredService<AccountService>();
    var grant = args[0] == "bootstrap-admin" ? await accounts.BootstrapAsync(args[1], args[2]) : await accounts.OperatorRecoveryAsync(args[1], args[2]);
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(grant));
    await app.DisposeAsync(); return;
}
app.UseStatusCodePages(async status =>
{
    var h = status.HttpContext;
    await Results.Problem(statusCode: h.Response.StatusCode, title: "request_rejected", extensions: new Dictionary<string, object?> { ["code"] = "http_" + h.Response.StatusCode, ["correlationId"] = h.TraceIdentifier }).ExecuteAsync(h);
});
app.Use(async (context, next) =>
{
    try
    {
        context.Response.Headers.CacheControl = "no-store";
        if (!context.Request.IsHttps && !app.Environment.IsEnvironment("Testing")) throw new AccessFault(400, "https_required");
        if (context.Request.ContentLength > 262144 && !context.Request.Path.Value!.EndsWith("/simulations", StringComparison.Ordinal)) throw new AccessFault(413, "request_too_large");
        await next();
    }
    catch (AccessFault e) { await Results.Problem(statusCode: e.Status, title: e.Code, extensions: new Dictionary<string, object?> { ["code"] = e.Code, ["correlationId"] = context.TraceIdentifier }).ExecuteAsync(context); }
    catch (AntiforgeryValidationException) { await Results.Problem(statusCode: 400, title: "csrf_invalid").ExecuteAsync(context); }
    catch (BadHttpRequestException) { await Results.Problem(statusCode: 400, title: "invalid_request").ExecuteAsync(context); }
    catch (Sonda.Application.Persistence.PersistenceConflict) { await Results.Problem(statusCode: 409, title: "persistence_conflict", extensions: new Dictionary<string, object?> { ["code"] = "persistence_conflict", ["correlationId"] = context.TraceIdentifier }).ExecuteAsync(context); }
    catch (DbUpdateConcurrencyException) { await Results.Problem(statusCode: 409, title: "revision_conflict").ExecuteAsync(context); }
    catch (Npgsql.PostgresException e) when (e.SqlState == "57014") { await Results.Problem(statusCode: 503, title: "query_timeout").ExecuteAsync(context); }
    catch (Exception e) when (e is not OperationCanceledException)
    {
        app.Logger.LogWarning("Request {Correlation} failed with {ErrorType}", context.TraceIdentifier, e.GetType().Name);
        await Results.Problem(statusCode: 503, title: "operation_unavailable", extensions: new Dictionary<string, object?> { ["correlationId"] = context.TraceIdentifier }).ExecuteAsync(context);
    }
});
app.UseFrontendAssets();
app.UseAuthentication(); app.UseAuthorization(); app.UseRateLimiter();
app.Use(RequestContract.Validate);
app.Use(async (context, next) =>
{
    if (context.Request.Method is not ("GET" or "HEAD" or "OPTIONS"))
    {
        var origin = context.Request.Headers.Origin.ToString();
        if (origin.Length > 0 && origin != $"{context.Request.Scheme}://{context.Request.Host}") throw new AccessFault(400, "origin_rejected");
        await context.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context);
    }
    await next();
});
app.MapGet("/health/live", () => new { live = true }).AllowAnonymous();
app.MapGet("/health/ready", async (AccessDbContext db) => new { ready = await db.Database.CanConnectAsync() }).RequireAuthorization("Admin");
app.MapOpenApi().RequireAuthorization("Admin");
var api = app.MapGroup("/api/v1");
api.MapGet("/team", (HttpContext h, DetailQueries q) => q.Team(Current(h), h.RequestAborted));
api.MapGet("/evidence/{id:guid}", (Guid id, Guid session, HttpContext h, DetailQueries q) => q.Evidence(Current(h), session, id, h.RequestAborted));
api.MapGet("/incidents/{id}/evidence", (string id, Guid session, int? size, string? cursor, string? order, HttpContext h, DetailQueries q) => q.IncidentEvidence(Current(h), session, id, size, cursor, h.RequestAborted, order));
api.MapGet("/incidents/{id}/presentation", async (string id, Guid session, HttpContext h) =>
{
    await using var db = Sonda.Infrastructure.Persistence.SondaDbContext.Open(Connection(h.RequestServices));
    db.Database.SetCommandTimeout(5);
    await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, h.RequestAborted);
    var result = await IncidentPresentation.Read(db, Current(h), session, id, h.RequestAborted);
    await tx.CommitAsync(h.RequestAborted);
    return result;
});
api.MapGet("/monitoring", async (HttpContext h, DashboardQueries q) => (await q.ReadAsync(Current(h), h.RequestAborted)).Applications);
api.MapGet("/profiles", (int? size, string? cursor, HttpContext h, DetailQueries q) => q.Profiles(Current(h), size, cursor, h.RequestAborted));
api.MapGet("/profiles/{id}/versions", (string id, int? before, HttpContext h, DetailQueries q) => q.Versions(Current(h), id, before, h.RequestAborted));
api.MapGet("/profiles/{id}/activation", (string id, HttpContext h, DetailQueries q) => q.Activation(Current(h), id, h.RequestAborted));
api.MapGet("/profiles/{id}/versions/{version:int}", (string id, int version, HttpContext h, DetailQueries q) => q.Version(Current(h), id, version, h.RequestAborted));
foreach (var child in new[] { "occurrences", "recoveries", "history" })
    api.MapGet("/incidents/{id}/" + child, (string id, Guid session, int? size, string? cursor, HttpContext h, DetailQueries q) => q.IncidentChildren(Current(h), session, id, child, size, cursor, h.RequestAborted));
api.MapGet("/operations/{id:guid}", (Guid id, HttpContext h, DetailQueries q) => q.Operation(Current(h), id, h.RequestAborted));
api.MapGet("/audit", (int? size, string? cursor, HttpContext h, DetailQueries q) => q.Audit(Current(h), size, cursor, h.RequestAborted)).RequireAuthorization("Admin");
api.MapGet("/monitoring/applications/{id}", (string id, HttpContext h, DetailQueries q) => q.Monitoring(Current(h), id, h.RequestAborted));
api.MapPost("/incidents/{id}/status", async (string id, StatusRequest r, HttpContext h, WorkflowService s) => Outcome(await s.Status(Current(h), id, r, h.RequestAborted)));
api.MapPost("/profiles/{id}/activate", async (string id, ActivationRequest r, HttpContext h, WorkflowService s) => Outcome(await s.Activate(Current(h), id, r, h.RequestAborted))).RequireAuthorization("Admin");
api.MapGet("/profiles/{id}/draft", (string id, HttpContext h, ProfileService s) => s.Draft(Current(h), id, h.RequestAborted)).RequireAuthorization("Admin");
api.MapPut("/profiles/{id}/draft", (string id, DraftEdit r, HttpContext h, ProfileService s) => s.Edit(Current(h), id, r, h.RequestAborted)).RequireAuthorization("Admin");
api.MapPost("/profiles/{id}/validate", (string id, HttpContext h, ProfileService s) => s.Validate(Current(h), id, h.RequestAborted)).RequireAuthorization("Admin");
api.MapPost("/profiles/{id}/simulations", (string id, PreviewRequest r, HttpContext h, ProfileService s) => s.Preview(Current(h), id, r, h.RequestAborted)).RequireAuthorization("Admin");
api.MapGet("/profiles/{id}/simulations/{reportId:guid}", (string id, Guid reportId, HttpContext h, ProfileService s) => s.Report(Current(h), id, reportId, h.RequestAborted)).RequireAuthorization("Admin");
api.MapPost("/profiles/{id}/publish", (string id, PublishRequest r, HttpContext h, ProfileService s) => s.Publish(Current(h), id, r, h.RequestAborted)).RequireAuthorization("Admin");
api.MapGet("/dashboard", (HttpContext h, DashboardQueries q) => q.ReadAsync(Current(h), h.RequestAborted));
api.MapGet("/monitoring/applications/{id}/overview", (string id, HttpContext h) => ApplicationOverview.Read(Connection(h.RequestServices), Current(h), id, h.RequestAborted));
api.MapGet("/search", ([AsParameters] SearchParameters p, HttpContext h, SearchQueries q) =>
{
    var today = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, TimeZoneInfo.Local).Date;
    var start = new DateTimeOffset(today, TimeZoneInfo.Local.GetUtcOffset(today));
    var end = new DateTimeOffset(today.AddDays(1), TimeZoneInfo.Local.GetUtcOffset(today.AddDays(1)));
    return q.Search(Current(h), new SearchFilter(p.From ?? start, p.To ?? end, p.TimeBasis ?? "processed", p.ApplicationId, p.ProfileId, p.Text, p.CaseSensitive ?? false, p.Identifier, p.IdentifierNamespace, p.Result, p.Classification, p.IncidentStatus), p.Size, p.Cursor, h.RequestAborted);
});
api.MapGet("/applications", (int? size, string? cursor, HttpContext h, InvestigationQueries q) => q.Applications(Current(h), size, cursor, h.RequestAborted));
api.MapGet("/applications/{id}", (string id, HttpContext h, InvestigationQueries q) => q.Application(Current(h), id, h.RequestAborted));
api.MapGet("/application-runs", (Guid session, int? size, string? cursor, HttpContext h, InvestigationQueries q) => q.Runs(Current(h), "Application", session, size, cursor, h.RequestAborted));
api.MapGet("/application-runs/{id}", (string id, Guid session, HttpContext h, InvestigationQueries q) => q.Run(Current(h), "Application", session, id, h.RequestAborted));
api.MapGet("/order-runs", (Guid session, int? size, string? cursor, string? parentId, HttpContext h, InvestigationQueries q) => q.Runs(Current(h), "Order", session, size, cursor, h.RequestAborted, parentId));
api.MapGet("/order-runs/{id}", (string id, Guid session, HttpContext h, InvestigationQueries q) => q.Run(Current(h), "Order", session, id, h.RequestAborted));
api.MapGet("/incidents", (Guid session, int? size, string? cursor, HttpContext h, InvestigationQueries q) => q.Incidents(Current(h), session, size, cursor, h.RequestAborted));
api.MapGet("/incidents/{id}", (string id, Guid session, HttpContext h, InvestigationQueries q) => q.Incident(Current(h), session, id, h.RequestAborted));
api.MapGet("/session/csrf", (HttpContext h, IAntiforgery antiforgery) => new { token = antiforgery.GetAndStoreTokens(h).RequestToken }).AllowAnonymous();
api.MapPost("/session/login", async (LoginRequest request, HttpContext h, AccountService accounts) =>
{
    var result = await accounts.LoginAsync(request.Login, request.Password, h.RequestAborted);
    var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, result.Actor.AccountId.ToString()), new Claim("session", result.Session.ToString())], CookieAuthenticationDefaults.AuthenticationScheme));
    await h.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, new AuthenticationProperties { IsPersistent = false, ExpiresUtc = DateTimeOffset.UtcNow.AddHours(8) });
    return Results.Ok(new { result.Actor.AccountId, result.Actor.Role });
}).AllowAnonymous().RequireRateLimiting("credentials");
api.MapGet("/session", async (HttpContext h, AccessDbContext db, TimeProvider clock) =>
{
    var actor = Current(h);
    var name = await db.Users.AsNoTracking().Where(x => x.Id == actor.AccountId)
        .Select(x => x.UserName).SingleOrDefaultAsync(h.RequestAborted);
    if (string.IsNullOrWhiteSpace(name)) throw new AccessFault(401, "session_invalid");
    var zone = TimeZoneInfo.Local;
    var iana = zone.HasIanaId ? zone.Id : TimeZoneInfo.TryConvertWindowsIdToIanaId(zone.Id, out var converted) ? converted : null;
    return new SessionDto(actor.AccountId, actor.TeamId, actor.Role, actor.Revision, name)
    { AsOf = clock.GetUtcNow(), ReportingTimeZoneId = zone.Id, ReportingTimeZoneIanaId = iana };
});
api.MapPost("/session/logout", async (HttpContext h, AccountService accounts) => { await accounts.LogoutAsync(Current(h), Session(h), false); await h.SignOutAsync(); return Results.NoContent(); });
api.MapPost("/session/logout-all", async (HttpContext h, AccountService accounts) => { await accounts.LogoutAsync(Current(h), Session(h), true); await h.SignOutAsync(); return Results.NoContent(); });
api.MapPost("/session/password", async (PasswordRequest r, HttpContext h, AccountService accounts) => { await accounts.ChangePasswordAsync(Current(h), r.CurrentPassword, r.NewPassword, h.RequestAborted); await h.SignOutAsync(); return Results.NoContent(); });
api.MapPost("/account-grants/redeem", async (RedeemRequest r, AccountService accounts, CancellationToken ct) => { await accounts.RedeemAsync(r.Token, r.Password, ct); return Results.NoContent(); }).AllowAnonymous().RequireRateLimiting("credentials");
api.MapGet("/team/members", (int? size, string? cursor, HttpContext h, DetailQueries q) => q.Members(Current(h), size, cursor, h.RequestAborted)).RequireAuthorization("Admin");
api.MapGet("/profiles/{id}/sources", (string id, HttpContext h, DetailQueries q) => q.Sources(Current(h), id, h.RequestAborted)).RequireAuthorization("Admin");
api.MapPost("/team/members", (InviteRequest r, HttpContext h, AccountService s) => s.InviteAsync(Current(h), r.OperationId, r.Login, r.Role, h.RequestAborted)).RequireAuthorization("Admin");
api.MapPatch("/team/members/{id:guid}", (Guid id, ChangeMemberRequest r, HttpContext h, AccountService s) => s.ChangeMemberAsync(Current(h), r.OperationId, id, RequiredRevision(r.ExpectedRevision), r.Role, r.Enabled, h.RequestAborted)).RequireAuthorization("Admin");
api.MapPost("/team/members/{id:guid}/invitation", (Guid id, GrantRequest r, HttpContext h, AccountService s) => s.ReplaceGrantAsync(Current(h), r.OperationId, id, "Invitation", RequiredRevision(r.ExpectedRevision), h.RequestAborted)).RequireAuthorization("Admin");
api.MapPost("/team/members/{id:guid}/reset-grants", (Guid id, GrantRequest r, HttpContext h, AccountService s) => s.ReplaceGrantAsync(Current(h), r.OperationId, id, "Reset", RequiredRevision(r.ExpectedRevision), h.RequestAborted)).RequireAuthorization("Admin");
api.MapPost("/applications", (CreateApplication r, HttpContext h, ProvisioningStore s) => s.CreateApplicationAsync(Current(h), r, h.RequestAborted)).RequireAuthorization("Admin");
api.MapPost("/applications/{id}/profiles", (string id, CreateProfile r, HttpContext h, ProvisioningStore s) => s.CreateProfileAsync(Current(h), id, r, h.RequestAborted)).RequireAuthorization("Admin");
api.MapPost("/profiles/{id}/sources", (string id, SourceRequest r, HttpContext h, ProvisioningStore s) => s.ConfigureSourceAsync(Current(h), id, r.OperationId, null, r.ExpectedRevision, r.Configuration, h.RequestAborted)).RequireAuthorization("Admin");
api.MapPut("/profiles/{id}/sources/{sourceId}", (string id, string sourceId, SourceRequest r, HttpContext h, SourceService s) => s.Configure(Current(h), id, sourceId, r.OperationId, r.ExpectedRevision, r.Configuration, h.RequestAborted)).RequireAuthorization("Admin");
await app.RunAsync();

static Actor Current(HttpContext h) => h.Items["Actor"] as Actor ?? throw new AccessFault(401, "session_invalid");
static long RequiredRevision(long? revision) => revision ?? throw new AccessFault(428, "expected_revision_required");
static IResult Outcome(object result)
{
    var json = System.Text.Json.JsonSerializer.SerializeToElement(result, Sonda.Application.Simulation.SimulationJson.Options);
    if (json.TryGetProperty("disposition", out var disposition) && disposition.GetString() == "Rejected")
    {
        var conflict = json.TryGetProperty("diagnostics", out var diagnostics) && diagnostics.EnumerateArray().Any(x => x.GetProperty("code").GetString() is "RevisionConflict" or "ActivationRevisionConflict");
        return Results.Problem(statusCode: conflict ? 409 : 422, title: "engine_command_rejected", extensions: new Dictionary<string, object?> { ["code"] = "engine_command_rejected", ["result"] = json });
    }
    return Results.Ok(json);
}
static string Connection(IServiceProvider sp) => sp.GetRequiredService<IConfiguration>()["SONDA_DATABASE"] ?? throw new InvalidOperationException("SONDA_DATABASE required.");
static Guid Session(HttpContext h) => Guid.Parse(h.User.FindFirstValue("session")!);
public sealed record LoginRequest(string Login, string Password);
public sealed record PasswordRequest(string CurrentPassword, string NewPassword);
public sealed record RedeemRequest(string Token, string Password);
public sealed record InviteRequest(Guid OperationId, string Login, string Role);
public sealed record GrantRequest(Guid OperationId, long? ExpectedRevision);
public sealed record ChangeMemberRequest(Guid OperationId, long? ExpectedRevision, string Role, bool Enabled);
public sealed record SourceRequest(Guid OperationId, SourceConfiguration Configuration, long? ExpectedRevision);
public sealed record SearchParameters(DateTimeOffset? From, DateTimeOffset? To, string? TimeBasis, string? ApplicationId, string? ProfileId, string? Text,
    bool? CaseSensitive, string? Identifier, string? IdentifierNamespace, string? Result, string? Classification, string? IncidentStatus, int? Size, string? Cursor);
public partial class Program;
