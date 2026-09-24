using Sonda.Application.Persistence;

namespace Sonda.Application.Acquisition;
public sealed record WorkerManifest(ProcessingScope Scope,string HostAuthority,SourceConfiguration[] Sources)
{
    public string[] Validate()
    {
        List<string> errors=[];
        if(string.IsNullOrWhiteSpace(Scope.TeamId)||string.IsNullOrWhiteSpace(Scope.ApplicationId)||Scope.SessionId==Guid.Empty||string.IsNullOrWhiteSpace(HostAuthority))errors.Add("Explicit pre-provisioned monitoring scope/host required.");
        if(Sources.Length==0)errors.Add("No sources configured.");
        if(Sources.GroupBy(s=>(s.ProfileId,s.SourceKey)).Any(g=>g.Count()>1))errors.Add("Duplicate source identity.");
        foreach(var source in Sources)try{source.Validate();}catch(ArgumentException e){errors.Add(source.SourceKey+": "+e.Message);}
        return errors.ToArray();
    }
}
