using Microsoft.EntityFrameworkCore;
using Sonda.Application.Persistence;
using Sonda.Domain.Incidents;
using Sonda.Infrastructure.Persistence;
using Xunit;

namespace Sonda.Persistence.Tests;

public sealed partial class PersistenceTests
{
    [Fact] public async Task Concurrent_draft_edits_do_not_lose_updates()
    {
        var (sample,_,_)=await Setup();var p=sample.Profiles[0];
        var outcomes=await Task.WhenAll(Enumerable.Range(0,2).Select(async n=>
        {
            try { await new PostgresConfigurationStore(pg.Connection).EditDraftAsync(p with{Name="Editor "+n},0,Guid.NewGuid());return true; }
            catch(PersistenceConflict){return false;}
        }));
        Assert.Single(outcomes,x=>x);Assert.Single(outcomes,x=>!x);
    }
    [Fact] public async Task Concurrent_status_edits_preserve_single_history_transition()
    {
        var (sample,scope,inputs)=await Setup();await Apply(inputs);await using var db=SondaDbContext.Open(pg.Connection);
        var incident=await db.Set<IncidentRow>().SingleAsync(x=>x.SessionId==scope.SessionId);
        var outcomes=await Task.WhenAll(Enumerable.Range(0,2).Select(async _=>
        {
            try {await new IncidentRevisionStore(pg.Connection).CompareExchangeAsync(scope,incident.Id,incident.Revision,IncidentStatus.Investigating,Guid.NewGuid(),sample.AsOf);return true;}
            catch(PersistenceConflict){return false;}
        }));
        Assert.Single(outcomes,x=>x);Assert.Equal(2,await db.Set<HistoryRow>().CountAsync(x=>x.SessionId==scope.SessionId));
    }
    [Fact] public async Task Source_edit_does_not_mutate_published_source_configuration()
    {
        var (_,scope,_)=await Setup();var config=new PostgresConfigurationStore(pg.Connection);var command=Guid.NewGuid();
        Assert.Equal(1,await config.EditSourceAsync(scope.TeamId,"cam","sample","{\"location\":\"synthetic-next\"}",0,command));
        Assert.Equal(1,await config.EditSourceAsync(scope.TeamId,"cam","sample","{\"location\":\"synthetic-next\"}",0,command));
        await Assert.ThrowsAsync<PersistenceConflict>(()=>config.EditSourceAsync(scope.TeamId,"cam","sample","{}",0,Guid.NewGuid()));
        await using var db=SondaDbContext.Open(pg.Connection);var old=await db.Set<VersionSourceRow>().FindAsync(scope.TeamId,"cam",1,"sample");Assert.Equal("{}",old!.Configuration);
    }
}
