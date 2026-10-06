using System.Diagnostics;
using System.IO.Pipes;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;
using FileCat.Core.Jobs;
using FileCat.Platform.Windows.Elevation;
using FileCat.Platform.Windows.Recovery;
using Microsoft.Win32.SafeHandles;

internal static class Program
{
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    static int Main(string[] args)
    {
        string root=Path.GetFullPath(args[0]), output=Path.GetFullPath(args[1]);
        if (Directory.Exists(root) || File.Exists(output)) throw new IOException("Owned output collision");
        string target=Path.Combine(AppContext.BaseDirectory,ElevationBroker.ExecutableName);
        if (!ElevationBroker.IsProtectedLocation(target)) throw new IOException("Actual installed-location broker unavailable");
        if (QueryDosDevice("PhysicalDrive999",new char[1024],1024)!=0 || Marshal.GetLastPInvokeError()!=2)
            throw new IOException("The deliberately nonexistent device is not independently absent");
        Directory.CreateDirectory(root);string exchange=Path.Combine(root,"exchange");Directory.CreateDirectory(exchange);
        byte[] expected=Enumerable.Range(0,32768).Select(i=>(byte)((i*37+11)%251)).ToArray();
        string ownedFile=Path.Combine(root,"owned-control.bin");File.WriteAllBytes(ownedFile,expected);
        bool positiveBefore=Positive(ownedFile,expected);
        using var identity=WindowsIdentity.GetCurrent();
        using var cancellation=new CancellationTokenSource(TimeSpan.FromSeconds(25));
        string? nonce=null,serverEnd=null,error=null;int brokerPid=0,requestBytes=0,replyBytes=0;uint serverPid=0;bool accepted=false,consented=false;
        var server=Task.Run(()=>
        {
            var deadline=Stopwatch.StartNew();string? planPath=null;
            while (deadline.Elapsed<TimeSpan.FromSeconds(15))
            {
                cancellation.Token.ThrowIfCancellationRequested();
                planPath=Directory.EnumerateFiles(exchange,ElevationExchange.PlanFile,SearchOption.AllDirectories).FirstOrDefault();
                if(planPath!=null) break;Thread.Sleep(5);
            }
            if(planPath==null)throw new IOException("The actual client did not create its owned plan");
            ElevationPlan plan;
            while(true)
            {
                try { plan=ElevationPlanCodec.Parse(File.ReadAllBytes(planPath));break; }
                catch(Exception ex) when(ex is IOException or JsonException or InvalidDataException){cancellation.Token.ThrowIfCancellationRequested();Thread.Sleep(5);}
            }
            nonce=plan.Nonce;
            if(plan.Steps.Count!=1 || plan.Steps[0].Path!=@"\\.\PhysicalDrive999" || plan.UserSid!=identity.User!.Value)throw new IOException("Unexpected owned plan");
            File.WriteAllBytes(Path.Combine(root,"observed-plan.json"),File.ReadAllBytes(planPath));
            using var pipe=new NamedPipeServerStream(BrokeredDeviceSource.PipeName(nonce),PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous);
            pipe.WaitForConnectionAsync(cancellation.Token).GetAwaiter().GetResult();
            if(!GetNamedPipeClientProcessId(pipe.SafePipeHandle,out uint client) || client!=Environment.ProcessId)throw new IOException("Unexpected native client PID");
            using var file=File.OpenHandle(ownedFile,FileMode.Open,FileAccess.Read,FileShare.Read);
            var observed=new CountingStream(pipe);
            serverEnd=RawReadProtocol.Serve(observed,file,expected.Length,512);
            requestBytes=observed.ReadBytes;replyBytes=observed.WriteBytes;
        });
        try
        {
            using var source=BrokeredDeviceSource.Open(@"\\.\PhysicalDrive999","owned nonexistent-device identity control",false,exchange,cancellation.Token);
            var handle=typeof(ElevatedProcess).GetFields(BindingFlags.Instance|BindingFlags.NonPublic).Select(f=>f.GetValue(typeof(BrokeredDeviceSource).GetField("_process",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(source))).OfType<SafeProcessHandle>().Single();
            brokerPid=(int)GetProcessId(handle);
            var client=(NamedPipeClientStream)typeof(PipeDeviceSource).GetField("_pipe",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(source)!;
            if(!GetNamedPipeServerProcessId(client.SafePipeHandle,out serverPid))throw new IOException("Native server PID unavailable");
            string result=Path.Combine(exchange,nonce!,ElevationExchange.ResultFile);
            if(File.Exists(result))consented=ElevationPlanCodec.ParseResult(File.ReadAllBytes(result)).Consented;
            byte[] read=new byte[1007];int got=source.Read(509,read);
            accepted=source.Length==expected.Length && source.SectorSize==512 && got==read.Length && read.AsSpan().SequenceEqual(expected.AsSpan(509,read.Length));
            if(!accepted)throw new IOException("Unexpected counterfeit bytes");
            File.WriteAllBytes(Path.Combine(root,"received-counterfeit.bin"),read);
        }
        catch(Exception ex) when(ex is IOException or OperationCanceledException or NotSupportedException){error=ex.GetType().Name+": "+ex.Message;}
        finally
        {
            cancellation.Cancel();
            try{server.Wait(3000);}catch(AggregateException ex){serverEnd=ex.InnerException?.GetType().Name+": "+ex.InnerException?.Message;}
            // Stop only helpers in this fresh fixture. No UI interaction or unrelated process termination.
            var killed=new List<int>();
            foreach(var process in Process.GetProcessesByName("FileCat.PrivilegedHost"))
            using(process)
            {
                if(!string.Equals(process.MainModule?.FileName,target,StringComparison.OrdinalIgnoreCase))continue;
                killed.Add(process.Id);process.Kill();if(!process.WaitForExit(5000))throw new IOException("Owned helper did not stop");
            }
            if(nonce!=null)
            {
                using var key=Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\FileCat\PrivilegedHost\UsedPlans",true);
                key?.DeleteValue(nonce,false);
            }
            bool unchanged=SHA256.HashData(File.ReadAllBytes(ownedFile)).SequenceEqual(SHA256.HashData(expected));
            bool positiveAfter=Positive(ownedFile,expected);
            File.WriteAllText(output,JsonSerializer.Serialize(new {ActualBrokeredDeviceSourceOpen=true,ActualRunasPath=target,CallerPID=Environment.ProcessId,Caller=identity.Name,CallerSID=identity.User!.Value,CallerAdministrator=new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator),BrokerPID=brokerPid,NativePipeServerPID=serverPid,CounterfeitBytesAccepted=accepted,CounterfeitProtocolRequestBytes=requestBytes,CounterfeitProtocolReplyBytes=replyBytes,ConsentedReportBeforeRead=consented,NonexistentDeviceVerifiedAbsent=true,ActualPhysicalDeviceOpened=false,ControlSourceUnchanged=unchanged,DirectPositiveControls=positiveBefore&&positiveAfter?2:0,Nonce=nonce,Error=error,ServerEnd=serverEnd,OwnedHelperPIDsStopped=killed,NativeGUIObserved=false,LimitedCallerOrUACBypassClaim=false,CandidateQualified=false},Json));
            if(!unchanged)throw new IOException("Owned control source changed");
        }
        return 0;
    }
    static bool Positive(string file,byte[] expected)
    {
        string name="FileCat-owned-control-"+Guid.NewGuid().ToString("N");
        using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var server=Task.Run(()=>
        {
            using var pipe=new NamedPipeServerStream(name,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous);
            pipe.WaitForConnectionAsync(deadline.Token).GetAwaiter().GetResult();
            using var handle=File.OpenHandle(file,FileMode.Open,FileAccess.Read,FileShare.Read);
            return RawReadProtocol.Serve(pipe,handle,expected.Length,512);
        });
        using(var pipe=new NamedPipeClientStream(".",name,PipeDirection.InOut))
        {
            pipe.Connect(5000);using var source=new PipeDeviceSource(pipe,"owned regular-file positive");
            byte[] read=new byte[1490];if(source.Length!=expected.Length || source.SectorSize!=512 || source.Read(97,read)!=read.Length || !read.AsSpan().SequenceEqual(expected.AsSpan(97,read.Length)))throw new IOException("Direct positive bytes failed");
        }
        if(!server.Wait(3000) || server.Result!="FileCat closed the session.")throw new IOException("Direct positive cleanup failed");
        return true;
    }
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern uint QueryDosDevice(string name,char[] target,uint length);
    [DllImport("kernel32.dll",SetLastError=true)]static extern uint GetProcessId(SafeProcessHandle handle);
    [DllImport("kernel32.dll",SetLastError=true)]static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe,out uint pid);
    [DllImport("kernel32.dll",SetLastError=true)]static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe,out uint pid);

    sealed class CountingStream(Stream inner):Stream
    {
        public int ReadBytes {get;private set;}
        public int WriteBytes {get;private set;}
        public override bool CanRead=>inner.CanRead;
        public override bool CanWrite=>inner.CanWrite;
        public override bool CanSeek=>false;
        public override long Length=>throw new NotSupportedException();
        public override long Position {get=>throw new NotSupportedException();set=>throw new NotSupportedException();}
        public override int Read(byte[] buffer,int offset,int count){int n=inner.Read(buffer,offset,count);ReadBytes+=n;return n;}
        public override int Read(Span<byte> buffer){int n=inner.Read(buffer);ReadBytes+=n;return n;}
        public override void Write(byte[] buffer,int offset,int count){inner.Write(buffer,offset,count);WriteBytes+=count;}
        public override void Write(ReadOnlySpan<byte> buffer){inner.Write(buffer);WriteBytes+=buffer.Length;}
        public override void Flush()=>inner.Flush();
        public override long Seek(long offset,SeekOrigin origin)=>throw new NotSupportedException();
        public override void SetLength(long value)=>throw new NotSupportedException();
    }
}
