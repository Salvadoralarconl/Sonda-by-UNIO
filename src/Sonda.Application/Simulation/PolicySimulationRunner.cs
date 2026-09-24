using Sonda.Application.Processing;
using Sonda.Domain.Availability;
using Sonda.Domain.Incidents;
using Sonda.Domain.Metrics;
using Sonda.Domain.Profiles;

namespace Sonda.Application.Simulation;

public sealed record PolicySimulationRequest(Profile Profile,string Seed,string ServerTimeZoneId,DateTimeOffset AsOf,PolicyCommand[] Commands,string[] RequiredSources);
public sealed record PolicySimulationReport(string EngineVersion,string RequestHash,bool Complete,PolicySessionState State,
    PolicyReceipt[] Results,Metrics Metrics,AvailabilityResult Availability)
{
    public string[] OverdueRunIds { get; init; } = [];
}
public static class PolicySimulationRunner
{
    public static PolicySimulationReport Run(PolicySimulationRequest request)
    {
        if(request.Commands.Length>10000 || string.IsNullOrWhiteSpace(request.Seed) || request.AsOf==default || request.Commands.Any(c=>c.ProcessedAt>request.AsOf))throw new ArgumentException("Invalid bounded simulation.");
        var session=new PolicySession(request.Profile,request.Seed);var results=request.Commands.Select(session.Execute).ToArray();
        return Report(request,session.Export(),results);
    }
    public static PolicySimulationReport Report(PolicySimulationRequest request,PolicySessionState state,PolicyReceipt[] results)
    {
        var zone=TimeZoneInfo.FindSystemTimeZoneById(request.ServerTimeZoneId);
        var session=PolicySession.Restore(state);
        var facts=state.Interpreter.Runs.Where(r=>r.Result is not null).Select(r=>new RunMetricFact(r.Id,r.Scope,r.Result!.Value,r.CompletedEventAt!.Value,r.CompletedProcessedAt!.Value));
        var last=state.Interpreter.Runs.Where(r=>r.Scope==TargetScope.Application).Select(r=>r.CompletedProcessedAt).Max();
        var availability=AvailabilityProjection.Calculate(session.Engine.CurrentHealth,request.RequiredSources,state.Observations,session.Engine.ActiveProfile.Policy!,last,request.AsOf);
        return new("phase3.1",SimulationJson.Hash(request),results.All(r=>r.Disposition is not ("Rejected" or "BlockedByPriorError")),state,results,MetricCalculator.Calculate(facts,request.AsOf,zone),availability)
            {OverdueRunIds=state.Interpreter.Runs.Where(r=>r.Result is null && r.PolicyContext?.OverdueAt<request.AsOf).Select(r=>r.Id).ToArray()};
    }
}
