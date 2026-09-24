using System.Diagnostics;
using Sonda.Application.Acquisition;
using Sonda.Application.Persistence;
using Sonda.Infrastructure.Files;
using Xunit;
namespace Sonda.Acquisition.Tests;
public sealed class SourceSafetyTests
{
    [Fact] public void Invalid_capacity_and_ambiguous_manifest_are_rejected_without_reading_sources()
    {
        var source=new SourceConfiguration{ProfileId="p",SourceKey="s",Root=Path.GetFullPath("unopened-synthetic")};
        Assert.Throws<ArgumentException>(()=>(source with{VisitBytes=1024,ReadBytes=1024,MaximumRecordBytes=2048}).Validate());
        Assert.Throws<ArgumentException>(()=>(source with{MaximumFiles=10001}).Validate());Assert.Throws<ArgumentException>(()=>(source with{Include=["../outside"]}).Validate());
        var manifest=new WorkerManifest(new("team",Guid.NewGuid(),"app"),"host",[source,source]);Assert.Contains(manifest.Validate(),e=>e.Contains("Duplicate"));
    }
    [Fact] public void Recursive_filters_and_root_containment_are_enforced()
    {
        var root=Path.Combine(Path.GetTempPath(),"sonda-boundary-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Path.Combine(root,"nested"));
        try
        {
            File.WriteAllText(Path.Combine(root,"a.log"),"a");File.WriteAllText(Path.Combine(root,"skip.log"),"b");File.WriteAllText(Path.Combine(root,"nested/b.log"),"c");
            var config=new SourceConfiguration{ProfileId="p",SourceKey="s",Root=root,Recursive=true,Include=["*.log"],Exclude=["skip.log"]};var source=new WindowsFileSource();Assert.Equal(2,source.Enumerate(config).Count());
            Assert.Throws<UnauthorizedAccessException>(()=>source.Open(Path.Combine(Path.GetTempPath(),"outside.log"),root));
            using var handle=source.Open(Path.Combine(root,"a.log"),root);Assert.False(handle.CanWrite);
        }
        finally{Directory.Delete(root,true);}
    }
    [Fact] public async Task Real_windows_junction_is_not_followed()
    {
        var root=Path.Combine(Path.GetTempPath(),"sonda-junction-"+Guid.NewGuid().ToString("N"));var target=Path.Combine(root,"target");var link=Path.Combine(root,"alias");Directory.CreateDirectory(target);File.WriteAllText(Path.Combine(target,"a.log"),"synthetic");
        try
        {
            var start=new ProcessStartInfo("cmd.exe"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};foreach(var arg in new[]{"/c","mklink","/J",link,target})start.ArgumentList.Add(arg);
            using var child=Process.Start(start)!;var output=child.StandardOutput.ReadToEndAsync();var error=child.StandardError.ReadToEndAsync();await child.WaitForExitAsync();Assert.True(child.ExitCode==0,(await output)+(await error));
            Assert.Throws<UnauthorizedAccessException>(()=>new WindowsFileSource().Open(Path.Combine(link,"a.log"),root));
        }
        finally{if(Directory.Exists(link))Directory.Delete(link);Directory.Delete(root,true);}
    }
}
