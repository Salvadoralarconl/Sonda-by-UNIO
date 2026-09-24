using System.Data;
using Microsoft.EntityFrameworkCore;
using Sonda.Access;
using Sonda.Api.Contracts;
using Sonda.Infrastructure.Persistence;
namespace Sonda.Server.Queries;
public static class ApplicationOverview
{
 public static async Task<object> Read(string connection, Actor actor, string application, CancellationToken ct)
 {
  await using var db=SondaDbContext.Open(connection);db.Database.SetCommandTimeout(5);
  await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead,ct);
  await db.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY",ct);
  var monitor=await db.Set<MonitoringSessionRow>().SingleOrDefaultAsync(x=>x.TeamId==actor.TeamId&&x.ApplicationId==application,ct)??throw new AccessFault(404,"resource_not_found");
  var incidents=db.Set<IncidentRow>().Where(x=>x.TeamId==actor.TeamId&&x.SessionId==monitor.SessionId);
  var errors=await incidents.CountAsync(x=>x.Status!="Resolved"&&x.Severity=="Error",ct);
  var warnings=await incidents.CountAsync(x=>x.Status!="Resolved"&&x.Severity=="Warning",ct);
  var investigating=await incidents.CountAsync(x=>x.Status=="Investigating",ct);
  var recent=await (from i in incidents join r in db.Set<ReceiptRow>() on new {i.TeamId,i.SessionId,Id=i.CreatedReceipt} equals new {r.TeamId,r.SessionId,r.Id} orderby r.ProcessedAt descending,i.Id select new IncidentDto(i.SessionId,i.Id,i.ProfileId,i.Problem,i.Severity,i.Status,i.Revision,i.PolicyContext!=null)).Take(20).ToArrayAsync(ct);
  var investigatingItems=await (from i in incidents join r in db.Set<ReceiptRow>() on new {i.TeamId,i.SessionId,Id=i.CreatedReceipt} equals new {r.TeamId,r.SessionId,r.Id} where i.Status=="Investigating" orderby r.ProcessedAt descending,i.Id select new IncidentDto(i.SessionId,i.Id,i.ProfileId,i.Problem,i.Severity,i.Status,i.Revision,i.PolicyContext!=null)).Take(20).ToArrayAsync(ct);
  var runs=await (from r in db.Set<RunRow>() join p in db.Set<ReceiptRow>() on new {r.TeamId,r.SessionId,Id=r.CreatedReceipt} equals new {p.TeamId,p.SessionId,p.Id} where r.TeamId==actor.TeamId&&r.SessionId==monitor.SessionId&&r.Scope=="Application" orderby p.ProcessedAt descending,r.Id select new {r.Id,r.Scope,r.ParentId,r.Identifier,r.Attempt,r.Result,r.Version,p.ProcessedAt}).Take(20).ToArrayAsync(ct);
  var failedOrders=await (from r in db.Set<RunRow>() join p in db.Set<ReceiptRow>() on new {r.TeamId,r.SessionId,Id=r.CreatedReceipt} equals new {p.TeamId,p.SessionId,p.Id} where r.TeamId==actor.TeamId&&r.SessionId==monitor.SessionId&&r.Scope=="Order"&&r.Result=="Failure" orderby p.ProcessedAt descending,r.Id select new {r.Id,r.ParentId,r.Identifier,r.Attempt,r.Result,r.Version,p.ProcessedAt}).Take(20).ToArrayAsync(ct);
  await tx.CommitAsync(ct);
  return new {applicationId=application,sessionId=monitor.SessionId,unresolvedErrorCount=errors,unresolvedWarningCount=warnings,investigatingCount=investigating,recentIncidents=recent,investigatingIncidents=investigatingItems,recentRuns=runs,recentFailedOrders=failedOrders,limit=20};
 }
}


