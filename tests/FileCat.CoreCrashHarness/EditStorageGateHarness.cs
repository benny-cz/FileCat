using System.Reflection;
using FileCat.Core.Edit;
using FileCat.Core.FileSystem;
using FileCat.Core.Resources;

internal static class EditStorageGateHarness
{
    internal static int Run(string path,string nativeAssembly,string mode)
    {
        if(!Path.IsPathFullyQualified(path)||!Directory.Exists(path)||mode is not("hold" or "allocate"))return 64;
        Func<string,bool,CancellationToken,EditDirectoryLease> open=UnixEditDirectoryLease.OpenRoot;
        if(OperatingSystem.IsWindows())
        {
            if(!Path.IsPathFullyQualified(nativeAssembly))return 64;
            var method=System.Runtime.Loader.AssemblyLoadContext.Default.LoadFromAssemblyPath(nativeAssembly)
                .GetType("FileCat.Platform.Windows.Archives.WindowsEditDirectoryLease",true)!
                .GetMethod("OpenRoot",BindingFlags.Static|BindingFlags.NonPublic)!;
            open=(folder,priv,ct)=>(EditDirectoryLease)method.Invoke(null,[folder,priv,ct])!;
        }
        if(mode=="hold")
        {
            using var root=open(path,true,default);using var gate=EditStorageAdmission.Acquire(root,default);
            Console.WriteLine("READY");Console.Out.Flush();Console.ReadLine();gate.Review(default);return 0;
        }
        Console.WriteLine("READY");Console.Out.Flush();Console.ReadLine();
        var store=new EditSessionStore(path,new PortableFileOperations(),open,null,new(Sessions:1));
        using var source=new MemoryContentSource("child.txt","child allocation"u8.ToArray());
        try{store.CreateRemote("test","test","/child.txt",source,source.GetRevision()!.Value,null);Console.WriteLine("ALLOWED");}
        catch(IOException){Console.WriteLine("REFUSED");}
        Console.Out.Flush();return 0;
    }
}
