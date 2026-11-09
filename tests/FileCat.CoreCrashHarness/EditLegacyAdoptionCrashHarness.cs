using System.Reflection;
using FileCat.Core.Archives;
using FileCat.Core.Edit;
using FileCat.Core.FileSystem;

internal static class EditLegacyAdoptionCrashHarness
{
    internal static int Run(string legacyRoot,string nativeRoot,string platformAssembly,string point,string marker)
    {
        if(!Path.IsPathFullyQualified(legacyRoot)||!Path.IsPathFullyQualified(nativeRoot)||
           !Path.IsPathFullyQualified(marker)||point is not("marker" or "pending" or "ready"))return 64;
        Func<string,bool,CancellationToken,EditDirectoryLease> nativeOpen=UnixEditDirectoryLease.OpenRoot;
        Func<string,EditSessionRecord,CancellationToken,ILegacyEditSource> legacyOpen=
            (root,record,ct)=>UnixLegacyEditSource.Open(root,record,ct);
        if(OperatingSystem.IsWindows())
        {
            if(!Path.IsPathFullyQualified(platformAssembly))return 64;
            var assembly=System.Runtime.Loader.AssemblyLoadContext.Default.LoadFromAssemblyPath(platformAssembly);
            var native=assembly.GetType("FileCat.Platform.Windows.Archives.WindowsEditDirectoryLease",true)!
                .GetMethod("OpenRoot",BindingFlags.Static|BindingFlags.NonPublic)!;
            var source=assembly.GetType("FileCat.Platform.Windows.Archives.WindowsLegacyEditSource",true)!
                .GetMethod("Open",BindingFlags.Static|BindingFlags.NonPublic)!;
            nativeOpen=(path,priv,ct)=>(EditDirectoryLease)native.Invoke(null,[path,priv,ct])!;
            legacyOpen=(path,record,ct)=>(ILegacyEditSource)source.Invoke(null,[path,record,ct])!;
        }
        var store=new EditSessionStore(legacyRoot,new PortableFileOperations(),nativeOpen,null,null,nativeRoot);
        var old=store.LoadAll().Single(row=>row.IsSupported&&row.Version is 1 or EditSessionRecord.CurrentVersion);
        using var held=legacyOpen(legacyRoot,old,default);
        using var paused=new PausingSource(held,point,marker);
        store.CopyLegacyForAdoption(paused);
        if(point=="ready")Pause(marker,point);
        return 65; // A normal return means the requested process-kill boundary was missed.
    }

    private static void Pause(string marker,string point)
    {
        File.WriteAllText(marker,point);
        Console.WriteLine("PAUSED");Console.Out.Flush();
        Thread.Sleep(Timeout.Infinite);
    }

    private sealed class PausingSource(ILegacyEditSource inner,string point,string marker):ILegacyEditSource
    {
        private int reviews;
        public EditSessionRecord Session=>inner.Session;
        public EditFileVersion Working=>inner.Working;
        public ArchiveOriginSnapshot Origin=>inner.Origin;
        public string RootPath=>inner.RootPath;
        public string RootIdentity=>inner.RootIdentity;
        public string SessionIdentity=>inner.SessionIdentity;
        public string FilesIdentity=>inner.FilesIdentity;
        public string RecordIdentity=>inner.RecordIdentity;
        public string RecordSha256=>inner.RecordSha256;
        public void Review(CancellationToken ct=default)
        {if(++reviews==4&&point=="pending")Pause(marker,point);inner.Review(ct);}
        public void CopyTo(Stream destination,CancellationToken ct=default)
        {if(point=="marker")Pause(marker,point);inner.CopyTo(destination,ct);}
        public void Dispose(){ /* The harness owns the held old-source lease. */ }
    }
}
