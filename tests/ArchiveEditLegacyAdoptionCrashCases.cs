using System.Diagnostics;
using System.Runtime.InteropServices;
using FileCat.Core.Edit;
using FileCat.Core.FileSystem;
using FileCat.Core.Resources;

namespace FileCat.Tests;

internal static class ArchiveEditLegacyAdoptionCrashCases
{
    internal static async Task RunAsync(string point,IFileSystemOperations operations,
        Func<string,bool,CancellationToken,EditDirectoryLease> nativeOpen,
        Func<string,EditSessionRecord,CancellationToken,ILegacyEditSource> legacyOpen,string platformAssembly)
    {
        var ct=TestContext.Current.CancellationToken;
        string parent=Path.GetFullPath(Environment.GetEnvironmentVariable("FILECAT_EDIT_NATIVE_ROOT")??Path.GetTempPath());
        if(OperatingSystem.IsMacOS()&&parent.StartsWith("/var/",StringComparison.Ordinal))parent="/private"+parent;
        string fixture=Directory.CreateDirectory(Path.Combine(parent,"filecat-adopt-crash-"+Guid.NewGuid().ToString("N"))).FullName;
        if(!OperatingSystem.IsWindows())File.SetUnixFileMode(fixture,
            UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute);
        try
        {
            string oldRoot=Path.Combine(fixture,"legacy"),nativeRoot=Path.Combine(fixture,"native"),marker=Path.Combine(fixture,"paused.txt");
            var oldStore=new EditSessionStore(oldRoot,operations);
            using var content=new MemoryContentSource("old.txt","original remote bytes"u8.ToArray());
            var old=oldStore.CreateRemote("server","server","/old.txt",content,content.GetRevision()!.Value,null,ct);
            byte[] oldRecord=File.ReadAllBytes(Path.Combine(oldRoot,old.Id,"session.json"));
            var checkout=new DirectoryInfo(AppContext.BaseDirectory);
            while(checkout is not null&&!File.Exists(Path.Combine(checkout.FullName,"FileCat.slnx")))checkout=checkout.Parent;
            Assert.NotNull(checkout);
            string config=new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
            string harness=Path.Combine(checkout.FullName,"tests","FileCat.CoreCrashHarness","bin",config,"net10.0","FileCat.CoreCrashHarness.dll");
            string runtimeRoot=new DirectoryInfo(RuntimeEnvironment.GetRuntimeDirectory()).Parent!.Parent!.Parent!.FullName;
            var start=new ProcessStartInfo(Path.Combine(runtimeRoot,OperatingSystem.IsWindows()?"dotnet.exe":"dotnet"))
            {UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
            foreach(string arg in new[]{harness,"--edit-adoption-crash",oldRoot,nativeRoot,platformAssembly,point,marker})start.ArgumentList.Add(arg);
            using var child=Process.Start(start)!;
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(45));
            try
            {
                while(!File.Exists(marker)&&!child.HasExited)
                {timeout.Token.ThrowIfCancellationRequested();await Task.Delay(25,timeout.Token);}
                if(!File.Exists(marker))
                {
                    string error=await child.StandardError.ReadToEndAsync(timeout.Token);
                    Assert.Fail("The child exited before its adoption boundary: "+error);
                }
                Assert.Equal(point,await ReadMarker(marker,timeout.Token));
            }
            finally
            {
                if(!child.HasExited)child.Kill(entireProcessTree:true);
                await child.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(15));
            }
            Assert.NotEqual(0,child.ExitCode);
            var store=new EditSessionStore(oldRoot,operations,nativeOpen,null,null,nativeRoot);
            var rows=store.LoadAll();
            Assert.Equal(2,rows.Count);
            Assert.True(Assert.Single(rows,row=>row.Id==old.Id).IsSupported);
            var copy=Assert.Single(rows,row=>row.Id!=old.Id);
            Assert.Equal(point=="ready",copy.IsSupported);
            Assert.Equal(oldRecord,File.ReadAllBytes(Path.Combine(oldRoot,old.Id,"session.json")));
            Assert.Equal("original remote bytes"u8.ToArray(),File.ReadAllBytes(old.WorkingPath));
            if(point=="ready")
            {
                var link=Assert.Single(store.ReviewLegacyAdoptions(legacyOpen,ct));
                Assert.Equal(EditLegacyAdoptionCatalogState.LinkedRetainedCopy,link.State);
                Assert.Equal(old.Id,link.ActiveLegacy!.Id);
            }
        }
        finally{TestCleanup.DeleteTree(fixture);}
    }

    // The child closes its marker before publishing the name, but another program (an indexer or a malware scanner) may
    // still hold the new file for a moment on Windows; a sharing violation is retried, anything else is not.
    private static async Task<string> ReadMarker(string marker,CancellationToken ct)
    {
        for(int attempt=0;;attempt++)
        {
            try{return File.ReadAllText(marker);}
            catch(IOException error)when(OperatingSystem.IsWindows()&&(error.HResult&0xFFFF)==32&&attempt<200)
            {await Task.Delay(25,ct);}
        }
    }
}
