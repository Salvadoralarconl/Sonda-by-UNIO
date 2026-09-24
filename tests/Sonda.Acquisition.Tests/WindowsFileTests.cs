using System.Text;
using Sonda.Application.Acquisition;
using Sonda.Infrastructure.Files;
using Xunit;
namespace Sonda.Acquisition.Tests;
public sealed class WindowsFileTests
{
    [Fact] public void Actual_windows_handle_survives_rename_but_recreation_is_distinct()
    {
        var root=Path.Combine(Path.GetTempPath(),"sonda-files-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            var path=Path.Combine(root,"current.log");File.WriteAllText(path,"one\n",new UTF8Encoding(false));var reader=new WindowsFileSource();
            using var old=reader.Open(path,root);var before=reader.Inspect(old,path,DateTimeOffset.UtcNow);
            File.Move(path,Path.Combine(root,"rotated.log"));File.WriteAllText(path,"two\n",new UTF8Encoding(false));
            using var replacement=reader.Open(path,root);Assert.NotEqual(before.Identity,reader.Inspect(replacement,path,DateTimeOffset.UtcNow).Identity);
            Assert.Equal(before.Identity,reader.Inspect(old,Path.Combine(root,"rotated.log"),DateTimeOffset.UtcNow).Identity);
            var bytes=new byte[4];Assert.Equal(4,RandomAccess.Read(old.SafeFileHandle,bytes,0));Assert.Equal("one\n",Encoding.UTF8.GetString(bytes));
        }
        finally {Directory.Delete(root,true);}
    }
    [Fact] public void Real_file_append_and_truncation_are_read_only_observations()
    {
        var root=Path.Combine(Path.GetTempPath(),"sonda-files-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            var path=Path.Combine(root,"a.log");File.WriteAllText(path,"one\n",new UTF8Encoding(false));var reader=new WindowsFileSource();using var stream=reader.Open(path,root);
            var first=reader.Inspect(stream,path,DateTimeOffset.UtcNow);File.AppendAllText(path,"two\n",new UTF8Encoding(false));
            var second=reader.Inspect(stream,path,DateTimeOffset.UtcNow);Assert.Equal(first.Identity,second.Identity);Assert.Equal(8,second.Length);
            using(var writer=new FileStream(path,FileMode.Open,FileAccess.Write,FileShare.ReadWrite|FileShare.Delete))writer.SetLength(0);
            var third=reader.Inspect(stream,path,DateTimeOffset.UtcNow);Assert.Equal(first.Identity,third.Identity);Assert.Equal(0,third.Length);
        }
        finally {Directory.Delete(root,true);}
    }
}
