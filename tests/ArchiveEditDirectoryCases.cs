using System.Security.Cryptography;
using FileCat.Core.Edit;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Tests;

internal static class ArchiveEditDirectoryCases
{
    internal static void Run(string scenario,Func<string,bool,CancellationToken,EditDirectoryLease> open,Func<SafeFileHandle,string> identity)
    {
        var ct=TestContext.Current.CancellationToken;
        string baseRoot=Environment.GetEnvironmentVariable("FILECAT_EDIT_NATIVE_ROOT")??System.IO.Path.GetTempPath();
        if(OperatingSystem.IsMacOS()&&baseRoot.StartsWith("/var/",StringComparison.Ordinal)&&
            new DirectoryInfo("/var").ResolveLinkTarget(true)?.FullName=="/private/var")baseRoot="/private"+baseRoot;
        baseRoot=System.IO.Path.GetFullPath(baseRoot);Directory.CreateDirectory(baseRoot);
        string fixture=System.IO.Path.Combine(baseRoot,"filecat-edit-ns-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixture);string path=Directory.CreateDirectory(System.IO.Path.Combine(fixture,"root")).FullName;
        string outside=Directory.CreateDirectory(System.IO.Path.Combine(fixture,"outside")).FullName;
        string witness=System.IO.Path.Combine(outside,"witness");File.WriteAllText(witness,"unrelated owned witness");
        byte[] before=SHA256.HashData(File.ReadAllBytes(witness));
        try
        {
            if(scenario=="inherited-acl")
            {
                if(OperatingSystem.IsWindows())RunTool("icacls.exe",path,"/grant","*S-1-1-0:(OI)(CI)F","/Q");
                else if(OperatingSystem.IsMacOS())RunTool("/bin/chmod","+a","everyone allow read,write,execute,file_inherit,directory_inherit",path);
                else RunTool("/usr/bin/setfacl","-m","d:u::rwx,d:g::rwx,d:o::rwx",path);
            }
            using var root=open(path,false,ct);
            if(scenario=="public-parent")
            { Assert.Throws<UnauthorizedAccessException>(()=>root.CreateFile("never-created",ct));Assert.Empty(Directory.GetFileSystemEntries(path));return; }
            using var session=root.CreateDirectory("session",ct);
            using var files=session.CreateDirectory("files",ct,removable:scenario.StartsWith("remove-",StringComparison.Ordinal));
            if(scenario.StartsWith("remove-",StringComparison.Ordinal))
            {
                if(scenario=="remove-root")Assert.ThrowsAny<IOException>(()=>root.RemoveEmpty(ct));
                else if(scenario=="remove-child-held")Assert.ThrowsAny<IOException>(()=>session.RemoveEmpty(ct));
                else if(scenario=="remove-nonempty")
                {
                    using(var stream=files.CreateFile("unrelated",ct))stream.Write("keep the unrelated child"u8);
                    Assert.ThrowsAny<IOException>(()=>files.RemoveEmpty(ct));
                    Assert.Equal("keep the unrelated child",File.ReadAllText(System.IO.Path.Combine(files.Path,"unrelated")));
                }
                else if(OperatingSystem.IsWindows())
                {
                    files.RemoveEmpty(ct);Assert.False(Directory.Exists(files.Path));
                    Directory.CreateDirectory(files.Path);File.WriteAllText(System.IO.Path.Combine(files.Path,"later"),"later directory occupant");
                    files.Dispose();Assert.Equal("later directory occupant",File.ReadAllText(System.IO.Path.Combine(files.Path,"later")));
                }
                else{Assert.ThrowsAny<IOException>(()=>files.RemoveEmpty(ct));Assert.True(Directory.Exists(files.Path));}
            }
            else if(scenario=="bounded-inspection")
            {
                byte[] bytes=Enumerable.Range(0,64).Select(value=>(byte)value).ToArray();
                using var stream=files.CreateFile("bounded-record",ct);stream.Write(bytes);stream.Flush(true);
                Assert.ThrowsAny<IOException>(()=>files.Inspect(stream,ct,63));
                Assert.ThrowsAny<IOException>(()=>files.ObserveSnapshot(stream,"bounded-record",ct,63));
                Assert.ThrowsAny<IOException>(()=>files.Inspect(stream,ct,-1));
                Assert.ThrowsAny<IOException>(()=>files.Inspect(stream,ct,EditSessionStore.MaxMemberBytes+1));
                Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)),files.ObserveSnapshot(stream,"bounded-record",ct,64).Version.Sha256);
                Assert.Equal(bytes,File.ReadAllBytes(System.IO.Path.Combine(files.Path,"bounded-record")));
            }
            else if(scenario=="bad-leaves")
            {
                foreach(string bad in new[]{"", ".", "..", "../escape", "folder/escape", "folder\\escape", "name:stream", "bad\ud800",new string('x',256)})
                { Assert.Throws<ArgumentException>(()=>files.CreateFile(bad,ct));Assert.Throws<ArgumentException>(()=>files.CreateDirectory(bad,ct)); }
                if(OperatingSystem.IsWindows())Assert.Throws<ArgumentException>(()=>files.CreateFile("CON",ct));
            }
            else if(scenario=="wrong-directory-id")
            { Assert.ThrowsAny<IOException>(()=>session.OpenDirectory("files","not-the-owned-directory",ct)); }
            else if(scenario=="parent-disposed")
            {
                root.Dispose();session.Dispose();
                using(var stream=files.CreateFile("retained-owner",ct))stream.Write("retained namespace"u8);
                Assert.Equal("retained namespace",File.ReadAllText(System.IO.Path.Combine(files.Path,"retained-owner")));
            }
            else if(scenario=="canceled")
            {
                using var canceled=CancellationTokenSource.CreateLinkedTokenSource(ct);canceled.Cancel();
                Assert.ThrowsAny<OperationCanceledException>(()=>files.CreateFile("never-created",canceled.Token));
                Assert.ThrowsAny<OperationCanceledException>(()=>files.CreateDirectory("never-created",canceled.Token));
            }
            else if(scenario=="stream-outlives")
            {
                string dataPath=System.IO.Path.Combine(files.Path,"held-stream");
                using(var stream=files.CreateFile("held-stream",ct))
                {files.Dispose();session.Dispose();root.Dispose();stream.Write("held stream survives owner release"u8);stream.Flush(true);}
                Assert.Equal("held stream survives owner release",File.ReadAllText(dataPath));
            }
            else if(scenario.StartsWith("publish",StringComparison.Ordinal))
            {
                using var stream=files.CreateFile("new-record",ct);stream.Write("complete new record"u8);var expected=files.Inspect(stream,ct);
                if(scenario=="publish-collision")
                {
                    using(var target=files.CreateFile("record.json",ct))target.Write("keep previous record"u8);
                    Assert.ThrowsAny<IOException>(()=>files.PublishNewFile(stream,"new-record","record.json",expected,ct));
                    Assert.Equal("keep previous record",File.ReadAllText(System.IO.Path.Combine(files.Path,"record.json")));
                }
                else if(scenario=="publish-wrong-source")
                {
                    using(var other=files.CreateFile("different-source",ct))other.Write("keep different source"u8);
                    Assert.ThrowsAny<IOException>(()=>files.PublishNewFile(stream,"different-source","record.json",expected,ct));
                    Assert.Equal("keep different source",File.ReadAllText(System.IO.Path.Combine(files.Path,"different-source")));
                    Assert.False(File.Exists(System.IO.Path.Combine(files.Path,"record.json")));
                }
                else if(scenario=="publish-wrong-bytes")
                {
                    Assert.ThrowsAny<IOException>(()=>files.PublishNewFile(stream,"new-record","record.json",expected with{Sha256=new('A',64)},ct));
                    Assert.False(File.Exists(System.IO.Path.Combine(files.Path,"record.json")));
                }
                else
                {
                    files.PublishNewFile(stream,"new-record","record.json",expected,ct);
                    Assert.False(File.Exists(System.IO.Path.Combine(files.Path,"new-record")));
                    stream.Dispose();
                    using var published=files.OpenFile("record.json",expected.Identity,ct);Assert.Equal(expected,files.Inspect(published,ct));
                }
            }
            else if(scenario=="parent-redirect")
            {
                string retained=System.IO.Path.Combine(fixture,"retained");
                if(OperatingSystem.IsWindows())Assert.ThrowsAny<IOException>(()=>Directory.Move(path,retained));
                else
                {
                    Directory.Move(path,retained);Directory.CreateSymbolicLink(path,outside);
                    Assert.Throws<IOException>(()=>files.CreateFile("never-created",ct));
                    Assert.False(File.Exists(System.IO.Path.Combine(outside,"never-created")));
                }
            }
            else
            {
                const string name="notes-界-é.txt";string id;
                using(var stream=files.CreateFile(name,ct))
                { stream.Write("exact native edit bytes"u8);stream.Flush(true);id=identity(stream.SafeFileHandle); }
                string dataPath=System.IO.Path.Combine(files.Path,name);
                if(scenario=="collision")Assert.Throws<IOException>(()=>files.CreateFile(name,ct));
                else if(scenario=="wrong-file-id")Assert.ThrowsAny<IOException>(()=>files.OpenFile(name,"not-the-owned-file",ct));
                else if(scenario=="file-substitution")
                {
                    File.Move(dataPath,dataPath+".retained");File.WriteAllText(dataPath,"unreviewed replacement");
                    var error=Record.Exception(()=>files.OpenFile(name,id,ct));
                    Assert.True(error is IOException or UnauthorizedAccessException,error?.ToString());
                    Assert.Equal("unreviewed replacement",File.ReadAllText(dataPath));
                    Assert.Equal("exact native edit bytes",File.ReadAllText(dataPath+".retained"));
                    return;
                }
                else
                {
                    using var reopened=session.OpenDirectory("files",files.Identity,ct);
                    using var stream=reopened.OpenFile(name,id,ct);using var reader=new StreamReader(stream);
                    Assert.Equal("exact native edit bytes",reader.ReadToEnd());
                }
                Assert.Equal("exact native edit bytes",File.ReadAllText(dataPath));
                if(!OperatingSystem.IsWindows())
                {
                    Assert.Equal(0,(int)File.GetUnixFileMode(session.Path)&0x3f);
                    Assert.Equal(0,(int)File.GetUnixFileMode(files.Path)&0x3f);
                    Assert.Equal(0,(int)File.GetUnixFileMode(dataPath)&0x3f);
                }
            }
            Assert.Equal(before,SHA256.HashData(File.ReadAllBytes(witness)));
            Assert.Equal(new[]{"witness"},Directory.GetFileSystemEntries(outside).Select(System.IO.Path.GetFileName));
        }
        finally
        {
            // Only this generated fixture, beneath the explicitly selected native test root, is disposable.
            string full=System.IO.Path.GetFullPath(fixture);
            if(!full.StartsWith(System.IO.Path.TrimEndingDirectorySeparator(baseRoot)+System.IO.Path.DirectorySeparatorChar,StringComparison.Ordinal)||
                !System.IO.Path.GetFileName(full).StartsWith("filecat-edit-ns-",StringComparison.Ordinal))throw new IOException("Unexpected test cleanup root.");
            Directory.Delete(full,recursive:true);
        }
    }
    private static void RunTool(string executable,params string[] args)
    {
        var start=new System.Diagnostics.ProcessStartInfo(executable){UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true,CreateNoWindow=true};
        foreach(string arg in args)start.ArgumentList.Add(arg);
        using var process=System.Diagnostics.Process.Start(start)!;
        var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();
        if(!process.WaitForExit(10000)){process.Kill(true);process.WaitForExit();throw new TimeoutException("Owned ACL fixture setup timed out.");}
        Assert.True(process.ExitCode==0,output.GetAwaiter().GetResult()+error.GetAwaiter().GetResult());
    }
}
