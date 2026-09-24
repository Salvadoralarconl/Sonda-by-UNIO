using System.ComponentModel;
using System.IO.Enumeration;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;
using Sonda.Application.Acquisition;

namespace Sonda.Infrastructure.Files;

public sealed class WindowsFileSource : IFileIdentityProvider
{
    public IEnumerable<string> Enumerate(SourceConfiguration config)
    {
        config.Validate();RequireNoReparse(config.Root);
        var options=new EnumerationOptions{RecurseSubdirectories=config.Recursive,IgnoreInaccessible=false,ReturnSpecialDirectories=false,AttributesToSkip=FileAttributes.ReparsePoint};
        foreach(var path in Directory.EnumerateFiles(config.Root,"*",options))
        {
            var relative=Path.GetRelativePath(config.Root,path).Replace('\\','/');
            if(config.Include.Any(p=>FileSystemName.MatchesSimpleExpression(p.Replace('\\','/'),relative,true))&&!config.Exclude.Any(p=>FileSystemName.MatchesSimpleExpression(p.Replace('\\','/'),relative,true)))yield return path;
        }
    }
    public FileStream Open(string path,string root)
    {
        if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException("Windows handle identity is required.");
        path=Path.GetFullPath(path);root=Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        if(!path.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new UnauthorizedAccessException("File is outside the allowed root.");
        RequireNoReparse(path);
        var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete,4096,FileOptions.Asynchronous);
        try
        {
            var buffer=new StringBuilder(32768);var length=GetFinalPathNameByHandle(stream.SafeFileHandle,buffer,(uint)buffer.Capacity,0);
            if(length==0||length>=buffer.Capacity)throw new IOException("Cannot prove final file path.",new Win32Exception());
            var final=buffer.ToString();if(final.StartsWith(@"\\?\UNC\",StringComparison.OrdinalIgnoreCase))final=@"\\"+final[8..];else if(final.StartsWith(@"\\?\",StringComparison.Ordinal))final=final[4..];
            if(!final.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new UnauthorizedAccessException("Opened handle resolves outside the allowed root.");
            return stream;
        }
        catch{stream.Dispose();throw;}
    }
    public FileObservation Inspect(FileStream stream,string path,DateTimeOffset observedAt)
    {
        if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException();
        if(!GetFileInformationByHandleEx(stream.SafeFileHandle,18,out FileIdInfo id,(uint)Marshal.SizeOf<FileIdInfo>())||
           !GetFileInformationByHandleEx(stream.SafeFileHandle,0,out FileBasicInfo basic,(uint)Marshal.SizeOf<FileBasicInfo>()))
            throw new IOException("IdentityUncertain: file provider did not supply stable handle identity.",new Win32Exception(Marshal.GetLastWin32Error()));
        var full=Path.GetFullPath(path);var authority=full.StartsWith(@"\\",StringComparison.Ordinal)?string.Join('/',full.Split('\\',StringSplitOptions.RemoveEmptyEntries).Take(2)).ToUpperInvariant():Environment.MachineName.ToUpperInvariant();
        var identity=new PhysicalFileIdentity(authority,id.Volume.ToString("X16"),id.Low.ToString("X16")+id.High.ToString("X16"),basic.Creation.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var length=RandomAccess.GetLength(stream.SafeFileHandle);return new(path,identity,length,observedAt,PrefixHash(stream,(int)Math.Min(length,64)));
    }
    public static string PrefixHash(FileStream stream,int count)
    {
        if(count<0||count>1048576)throw new ArgumentOutOfRangeException(nameof(count));
        var bytes=new byte[count];var read=0;while(read<count){var n=RandomAccess.Read(stream.SafeFileHandle,bytes.AsSpan(read),read);if(n==0)throw new IOException("File changed during continuity read.");read+=n;}
        return Convert.ToHexString(SHA256.HashData(bytes));
    }
    private static void RequireNoReparse(string path)
    {
        for(var current=Path.GetFullPath(path);!string.IsNullOrEmpty(current);current=Path.GetDirectoryName(current))
            if((File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)throw new UnauthorizedAccessException("Reparse points are not followed.");
    }
    [StructLayout(LayoutKind.Sequential)] private struct FileIdInfo { public ulong Volume;public ulong Low;public ulong High; }
    [StructLayout(LayoutKind.Sequential)] private struct FileBasicInfo { public long Creation;public long Access;public long Write;public long Change;public uint Attributes; }
    [DllImport("kernel32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool GetFileInformationByHandleEx(SafeFileHandle handle,int info,out FileIdInfo data,uint size);
    [DllImport("kernel32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool GetFileInformationByHandleEx(SafeFileHandle handle,int info,out FileBasicInfo data,uint size);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern uint GetFinalPathNameByHandle(SafeFileHandle handle,StringBuilder path,uint size,uint flags);
}
