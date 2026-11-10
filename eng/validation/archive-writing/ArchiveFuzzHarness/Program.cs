using System.Security.Cryptography;
using System.Reflection;
using ReflectionEmit=System.Reflection.Emit;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.Json;
using FileCat.Core.Archives;
using FileCat.Core.Jobs;
using SharpFuzz;
using dnlib.DotNet;
using dnlib.DotNet.Emit;

// This executable is test-only. Canary edits and instrumentation apply solely to disposable assembly copies.
if(args is ["patch-canary",var originalPath,var patchedPath])
{
    if(Path.GetFullPath(originalPath)==Path.GetFullPath(patchedPath)||File.Exists(patchedPath))
        throw new ArgumentException("Canary output must be a new, separate assembly copy.");
    using var module=ModuleDefMD.Load(originalPath);
    var method=module.Find("FileCat.Core.Archives.ArchiveWritePolicy",false).Methods.Single(m=>m.Name=="ValidateDigest");
    var returns=method.Body.Instructions.Where(i=>i.OpCode==OpCodes.Ret).ToArray();
    if(returns.Length!=1)throw new InvalidOperationException("The canary recipe needs review for this production method body.");
    var last=returns[0];var importer=new Importer(module);
    var index=importer.Import(typeof(string).GetProperty("Chars")!.GetMethod!);
    var error=importer.Import(typeof(InvalidOperationException).GetConstructor([typeof(string)])!);
    var code=new List<Instruction>();
    const string magic="FC";
    for(int i=0;i<magic.Length;i++)code.AddRange([Instruction.Create(OpCodes.Ldarg_0),Instruction.Create(OpCodes.Ldc_I4,i),
        Instruction.Create(OpCodes.Callvirt,index),Instruction.Create(OpCodes.Ldc_I4,(int)magic[i]),Instruction.Create(OpCodes.Bne_Un,last)]);
    code.AddRange([Instruction.Create(OpCodes.Ldstr,"FILECAT_VALIDATED_DIGEST_CANARY"),Instruction.Create(OpCodes.Newobj,error),Instruction.Create(OpCodes.Throw)]);
    // Keep every original validation check. Only a valid 64-character digest can reach this intentional defect.
    foreach(var instruction in method.Body.Instructions)
    {
        if(ReferenceEquals(instruction.Operand,last))instruction.Operand=code[0];
        if(instruction.Operand is Instruction[] targets)
            for(int i=0;i<targets.Length;i++)if(ReferenceEquals(targets[i],last))targets[i]=code[0];
    }
    int insertion=method.Body.Instructions.IndexOf(last);
    for(int i=0;i<code.Count;i++)method.Body.Instructions.Insert(insertion+i,code[i]);
    module.Write(patchedPath);
    Console.WriteLine("Inserted test-only defect after the real production digest validation.");return 0;
}
if(args is ["instrument",var targetAssembly,var typeReport])
{
    var types=Fuzzer.Instrument(targetAssembly,
        name=>name.StartsWith("FileCat.Core.Archives.ArchiveWritePolicy",StringComparison.Ordinal)||
            name.StartsWith("FileCat.Core.Edit.EditSessionStore",StringComparison.Ordinal)||
            name.StartsWith("FileCat.Core.Edit.EditSaveCopyIntent",StringComparison.Ordinal)||
            name.StartsWith("FileCat.Core.Edit.EditSaveCopyRecord",StringComparison.Ordinal)||
            name.StartsWith("FileCat.Core.Jobs.JobJournal",StringComparison.Ordinal)||
            name.StartsWith("FileCat.Core.Archives.ArchiveTransactionCollector",StringComparison.Ordinal)||
            name.StartsWith("FileCat.Core.Archives.ArchiveAssessmentWire",StringComparison.Ordinal)||
            name.StartsWith("FileCat.Core.Archives.ArchiveWriteBudget",StringComparison.Ordinal)||
            name.StartsWith("FileCat.Core.Archives.ArchiveWorkerProtocol",StringComparison.Ordinal)||
            name.StartsWith("FileCat.Core.Archives.ArchiveWorkerRecords",StringComparison.Ordinal)||
            name.StartsWith("FileCat.Core.Archives.ArchiveWorkerSession",StringComparison.Ordinal)||
            name.StartsWith("FileCat.Core.Archives.ArchiveAssessmentClient",StringComparison.Ordinal)||
            name.StartsWith("FileCat.Core.Archives.ArchiveVerificationClient",StringComparison.Ordinal)||
            name.StartsWith("FileCat.Core.Archives.ArchiveIoAuthority",StringComparison.Ordinal),Options.Value).ToArray();
    File.WriteAllText(typeReport,JsonSerializer.Serialize(types));
    if(!types.Contains("FileCat.Core.Archives.ArchiveWritePolicy")||!types.Contains("FileCat.Core.Edit.EditSessionStore")||
        !types.Contains("FileCat.Core.Edit.EditSaveCopyIntent")||!types.Contains("FileCat.Core.Edit.EditSaveCopyRecord")||
        !types.Contains("FileCat.Core.Jobs.JobJournal")||
        !types.Contains("FileCat.Core.Archives.ArchiveTransactionCollector")||
        !types.Contains("FileCat.Core.Archives.ArchiveAssessmentWire")||
        !types.Contains("FileCat.Core.Archives.ArchiveWriteBudget"))
        throw new InvalidOperationException("Production guard or recovery record parser was not instrumented.");
    Console.WriteLine(JsonSerializer.Serialize(new{instrumented_types=types.Length,production_guard=true,production_recovery=true}));return 0;
}
if(args is ["instrument-tar",var tarAssembly,var tarTypeReport])
{
    var types=Fuzzer.Instrument(tarAssembly,
        name=>name.StartsWith("FileCat.Archives.Writing.StrictTarCatalog",StringComparison.Ordinal)||
            name.StartsWith("FileCat.Archives.TarHeaderGuard",StringComparison.Ordinal)||
            name.StartsWith("FileCat.Archives.Writing.StrictSevenZipCatalog",StringComparison.Ordinal)||
            name.StartsWith("FileCat.Archives.Writing.StrictSevenZipFraming",StringComparison.Ordinal)||
            name.StartsWith("FileCat.Archives.Writing.SevenZipStrictMetadata",StringComparison.Ordinal)||
            name.StartsWith("FileCat.Archives.Writing.SevenZipStrictHeaderReader",StringComparison.Ordinal)||
            name.StartsWith("FileCat.Archives.Writing.StrictSevenZipFolderDecoder",StringComparison.Ordinal)||
            name.StartsWith("FileCat.Archives.Writing.StrictSevenZipHeaderDecoder",StringComparison.Ordinal)||
            name.StartsWith("FileCat.Archives.SevenZipReadPasswordSession",StringComparison.Ordinal)||
            name.StartsWith("FileCat.Archives.Writing.SevenZipEncryptionPrimitives",StringComparison.Ordinal)||
            name.StartsWith("FileCat.Archives.Writing.StrictRar5Catalog",StringComparison.Ordinal)||
            name.StartsWith("FileCat.Archives.Writing.StrictRar5Framing",StringComparison.Ordinal)||
            name.StartsWith("FileCat.Archives.Writing.StrictGzipReadStream",StringComparison.Ordinal)||
            name.StartsWith("FileCat.Archives.Writing.StrictBzip2ReadStream",StringComparison.Ordinal)||
            name.StartsWith("FileCat.Archives.Writing.StrictZstdReadStream",StringComparison.Ordinal)||
            name.StartsWith("FileCat.Archives.Writing.ZstdFrameHeader",StringComparison.Ordinal)||
            name.StartsWith("FileCat.Archives.Rar.Rar5ReadPasswordSession",StringComparison.Ordinal)||
            name.StartsWith("FileCat.Archives.Writing.Rar5EncryptionPrimitives",StringComparison.Ordinal)||
            name.StartsWith("FileCat.Archives.Writing.ArchiveCryptoWorkBudget",StringComparison.Ordinal)||
            name.StartsWith("FileCat.Archives.Writing.ArchiveAssessmentWorker",StringComparison.Ordinal)||
            name.StartsWith("FileCat.Archives.Writing.ArchiveTransformWorker",StringComparison.Ordinal),Options.Value).ToArray();
    File.WriteAllText(tarTypeReport,JsonSerializer.Serialize(types));
    if(!types.Contains("FileCat.Archives.Writing.StrictTarCatalog")||!types.Contains("FileCat.Archives.TarHeaderGuard")||
        !types.Contains("FileCat.Archives.Writing.StrictSevenZipCatalog")||
        !types.Contains("FileCat.Archives.Writing.StrictSevenZipFraming")||
        !types.Contains("FileCat.Archives.Writing.SevenZipStrictMetadata")||
        !types.Contains("FileCat.Archives.Writing.SevenZipStrictHeaderReader")||
        !types.Contains("FileCat.Archives.Writing.StrictRar5Catalog")||
        !types.Contains("FileCat.Archives.Writing.StrictRar5Framing")||
        !types.Contains("FileCat.Archives.Writing.StrictGzipReadStream")||
        !types.Contains("FileCat.Archives.Writing.StrictBzip2ReadStream")||
        !types.Contains("FileCat.Archives.Writing.StrictZstdReadStream")||
        !types.Contains("FileCat.Archives.Writing.ZstdFrameHeader")||
        !types.Contains("FileCat.Archives.SevenZipReadPasswordSession")||
        !types.Contains("FileCat.Archives.Rar.Rar5ReadPasswordSession")||
        !types.Contains("FileCat.Archives.Writing.ArchiveCryptoWorkBudget"))
        throw new InvalidOperationException("Production strict TAR/7z/RAR5 scanners were not instrumented.");
    Console.WriteLine(JsonSerializer.Serialize(new{instrumented_types=types.Length,production_tar=true,production_sevenzip=true,production_rar5=true}));return 0;
}
if(args is ["replay-format",var formatTarget,var formatInput])
{
    if(formatTarget is not ("tar-strict" or "sevenzip-strict" or "rar5-strict" or "sevenzip-password" or "rar5-password" or
        "gzip-strict" or "bzip2-strict" or "zstd-strict"))throw new ArgumentException("Unknown format replay target.");
    using var file=new FileStream(formatInput,FileMode.Open,FileAccess.Read,FileShare.Read);
    if(file.Length>1048576)throw new ArgumentException("Format replay exceeds 1 MiB.");
    byte[] bytes=new byte[checked((int)file.Length)];file.ReadExactly(bytes);
    if(file.ReadByte()!=-1)throw new IOException("Format replay file changed during its bounded read.");
    Format.Bind(formatTarget);
    bool accepted=Format.Exercise(bytes,formatTarget);
    Console.WriteLine(JsonSerializer.Serialize(new{target=formatTarget,accepted}));return 0;
}
if(args is ["replay-record",var recordTarget,var inputPath])
{
    if(recordTarget is not ("session-record" or "capture-record" or "save-copy-intent" or "save-copy-receipt" or
        "journal-record" or "journal-state" or "manifest-member"))
        throw new ArgumentException("Unknown recovery-record replay target.");
    using var file=new FileStream(inputPath,FileMode.Open,FileAccess.Read,FileShare.Read);
    if(file.Length>1048576)throw new ArgumentException("Recovery-record replay exceeds 1 MiB.");
    byte[] bytes=new byte[checked((int)file.Length)];file.ReadExactly(bytes);
    if(file.ReadByte()!=-1)throw new IOException("Recovery-record replay file changed during its bounded read.");
    bool accepted=recordTarget switch
    {
        "session-record"=>Session.Exercise(bytes),
        "capture-record"=>Capture.Exercise(bytes),
        "save-copy-intent"=>Intent.Exercise(bytes),
        "save-copy-receipt"=>Receipt.Exercise(bytes),
        "journal-record"=>Journal.Exercise(bytes),
        "journal-state"=>JournalState.Exercise(bytes),
        _=>ManifestMember.Exercise(bytes),
    };
    Console.WriteLine(JsonSerializer.Serialize(new{target=recordTarget,accepted}));return 0;
}
if(args is ["replay-worker",var workerTarget,var workerInput])
{
    if(workerTarget is not ("worker-frames" or "verify-output" or "create-inputs" or "update-changes" or "stream-faults" or
        "member-keys" or "repeated-cancel" or "standalone-streams"))
        throw new ArgumentException("Unknown worker replay target.");
    using var file=new FileStream(workerInput,FileMode.Open,FileAccess.Read,FileShare.Read);
    if(file.Length>1048576)throw new ArgumentException("Worker replay exceeds 1 MiB.");
    byte[] bytes=new byte[checked((int)file.Length)];file.ReadExactly(bytes);
    if(file.ReadByte()!=-1)throw new IOException("Worker replay file changed during its bounded read.");
    bool accepted;
    try{accepted=workerTarget switch
    {
        "worker-frames"=>WorkerFrames.Exercise(bytes),"verify-output"=>VerifyOutput.Exercise(bytes),
        "create-inputs"=>CreateInputs.Exercise(bytes),"update-changes"=>UpdateChanges.Exercise(bytes),"stream-faults"=>StreamFaults.Exercise(bytes),
        "member-keys"=>MemberKeys.Exercise(bytes),"standalone-streams"=>StandaloneStreams.Exercise(bytes),_=>RepeatedCancellation.Exercise(bytes),
    };}
    catch(ArchiveWriteException){accepted=false;} // A typed refusal, as in the native and structured lanes.
    Console.WriteLine(JsonSerializer.Serialize(new{target=workerTarget,accepted}));return 0;
}
if(args is ["make-worker-seeds",var workerSeedDirectory])
{
    // Real transcripts of the actual in-process worker roles (FZ08) and the exact fixture archives (FZ07).
    if(!Path.IsPathFullyQualified(workerSeedDirectory)||Directory.Exists(workerSeedDirectory))throw new ArgumentException("Seeds need a new absolute folder.");
    Directory.CreateDirectory(Path.Combine(workerSeedDirectory,"worker-frames"));Directory.CreateDirectory(Path.Combine(workerSeedDirectory,"verify-output"));
    foreach(var(name,seed)in WorkerFrames.Seeds())File.WriteAllBytes(Path.Combine(workerSeedDirectory,"worker-frames",name),seed);
    for(int i=0;i<WorkerFixtures.Archives.Length;i++)
        File.WriteAllBytes(Path.Combine(workerSeedDirectory,"verify-output",$"stage-{i}.bin"),[(byte)i,..WorkerFixtures.Archives[i]]);
    foreach(var(folder,seeds)in new[]{("create-inputs",WriterSeeds.Create()),("update-changes",WriterSeeds.Update()),("stream-faults",WriterSeeds.Faults()),
        ("member-keys",StatefulSeeds.Keys()),("repeated-cancel",StatefulSeeds.Repeats()),("standalone-streams",StandaloneStreams.Seeds())})
    {
        Directory.CreateDirectory(Path.Combine(workerSeedDirectory,folder));
        foreach(var(name,seed)in seeds)File.WriteAllBytes(Path.Combine(workerSeedDirectory,folder,name),seed);
    }
    Console.WriteLine(JsonSerializer.Serialize(Directory.GetDirectories(workerSeedDirectory).ToDictionary(d=>Path.GetFileName(d),d=>Directory.GetFiles(d).Length)));return 0;
}
if(args is ["structured-mutation",var mutationTarget,var mutationSeeds,var mutationSeconds,var mutationSeed,var mutationReport])
    return StructuredMutation.Run(mutationTarget,mutationSeeds,mutationSeconds,mutationSeed,mutationReport);
if(args.Length!=0)throw new ArgumentException("Fuzz targets receive input through the bounded native driver, not command-line paths.");

Stats.Target=Environment.GetEnvironmentVariable("FILECAT_FUZZ_TARGET")??"digest";
if(Stats.Target is not ("digest" or "tar-strict" or "session-record" or "capture-record" or "save-copy-intent" or
    "save-copy-receipt" or "journal-record" or "journal-state" or "manifest-member" or "sevenzip-strict" or "rar5-strict" or
    "sevenzip-password" or "rar5-password" or "gzip-strict" or "bzip2-strict" or "zstd-strict" or "worker-frames" or "verify-output" or
    "create-inputs" or "update-changes" or "stream-faults" or "member-keys" or "repeated-cancel" or "standalone-streams"))
    throw new ArgumentException("Unknown fuzz target.");
Stats.CanaryPrefixMode=Environment.GetEnvironmentVariable("FILECAT_FUZZ_CANARY_PREFIX_MODE")=="1";
if(Stats.CanaryPrefixMode&&Stats.Target!="digest")throw new ArgumentException("The canary input transform is digest-only.");
Stats.Module=typeof(ArchiveWritePolicy).Assembly.Location;
Stats.ModuleSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Stats.Module)));
if(Stats.Target is "tar-strict" or "sevenzip-strict" or "rar5-strict" or "sevenzip-password" or "rar5-password" or
    "gzip-strict" or "bzip2-strict" or "zstd-strict")Format.Bind(Stats.Target);
Stats.Report=Environment.GetEnvironmentVariable("FILECAT_FUZZ_REPORT");
if(Stats.Report is not null&&(!Path.IsPathFullyQualified(Stats.Report)||!Directory.Exists(Path.GetDirectoryName(Stats.Report))))
    throw new ArgumentException("The supervisor must select an existing absolute report directory.");
SharpFuzz.Common.Trace.OnBranch=(_,name)=>{if(Stats.Methods.Count<128)Stats.Methods.Add(name);};
try{Fuzzer.LibFuzzer.Run(Exercise);return 0;}
finally{Stats.Publish();}

static void Exercise(ReadOnlySpan<byte> data)
{
    if(data.Length>1048576)throw new InvalidOperationException("Native fuzz input exceeded its configured 1 MiB boundary.");
    Stats.Calls++;
    try
    {
        if(Stats.Target is "session-record" or "capture-record" or "save-copy-intent" or "save-copy-receipt" or
            "journal-record" or "journal-state" or "manifest-member" or "worker-frames" or "verify-output" or
            "create-inputs" or "update-changes" or "stream-faults" or "member-keys" or "repeated-cancel" or "standalone-streams")
        {
            bool accepted=Stats.Target switch
            {
                "worker-frames"=>WorkerFrames.Exercise(data),
                "verify-output"=>VerifyOutput.Exercise(data),
                "create-inputs"=>CreateInputs.Exercise(data),
                "update-changes"=>UpdateChanges.Exercise(data),
                "stream-faults"=>StreamFaults.Exercise(data),
                "member-keys"=>MemberKeys.Exercise(data),
                "repeated-cancel"=>RepeatedCancellation.Exercise(data),
                "standalone-streams"=>StandaloneStreams.Exercise(data),
                "session-record"=>Session.Exercise(data),
                "capture-record"=>Capture.Exercise(data),
                "save-copy-intent"=>Intent.Exercise(data),
                "save-copy-receipt"=>Receipt.Exercise(data),
                "journal-record"=>Journal.Exercise(data),
                "journal-state"=>JournalState.Exercise(data),
                _=>ManifestMember.Exercise(data),
            };
            if(accepted)Stats.Accepted++;else Stats.Refused++;
            if(Stats.Calls==1||(Stats.Calls&4095)==0)Stats.Publish();
            return;
        }
        if(Stats.Target is "tar-strict" or "sevenzip-strict" or "rar5-strict" or "sevenzip-password" or "rar5-password" or
            "gzip-strict" or "bzip2-strict" or "zstd-strict")
        {if(!Format.Exercise(data,Stats.Target))Stats.Refused++;else Stats.Accepted++;}
        else
        {
            string digest=Encoding.Latin1.GetString(data);
            if(Stats.CanaryPrefixMode&&data.Length==2)digest+=new string('0',62);
            ArchiveWritePolicy.ValidateDigest(digest);
        }
        if(Stats.Target is not ("tar-strict" or "sevenzip-strict" or "rar5-strict" or "sevenzip-password" or "rar5-password" or
            "gzip-strict" or "bzip2-strict" or "zstd-strict"))Stats.Accepted++;
    }
    catch(ArchiveWriteException){Stats.Refused++;}
    catch{Stats.Publish();throw;}
    if(Stats.Calls==1||(Stats.Calls&4095)==0)Stats.Publish();
}

static class Receipt
{
    // Reflection Invoke cannot box ReadOnlySpan<byte>; this test-only thunk calls the actual internal decoder.
    private static readonly Func<byte[],object> Decode=Bind();
    private static Func<byte[],object> Bind()
    {
        var type=typeof(FileCat.Core.Edit.EditSessionStore).Assembly.GetType("FileCat.Core.Edit.EditSaveCopyRecord",throwOnError:true)!;
        var method=type.GetMethod("Decode",BindingFlags.Static|BindingFlags.NonPublic)
            ??throw new InvalidOperationException("Production saved-copy receipt parser is unavailable.");
        if(method.GetParameters() is not [{ParameterType:var parameter}]||parameter!=typeof(ReadOnlySpan<byte>))
            throw new InvalidOperationException("Production saved-copy receipt parser signature changed.");
        var constructor=typeof(ReadOnlySpan<byte>).GetConstructor([typeof(byte[])])
            ??throw new InvalidOperationException("Bounded receipt input adapter is unavailable.");
        var thunk=new ReflectionEmit.DynamicMethod("FileCatFuzzDecodeReceipt",typeof(object),[typeof(byte[])],typeof(Receipt).Module,true);
        var il=thunk.GetILGenerator();il.Emit(ReflectionEmit.OpCodes.Ldarg_0);il.Emit(ReflectionEmit.OpCodes.Newobj,constructor);
        il.Emit(ReflectionEmit.OpCodes.Call,method);il.Emit(ReflectionEmit.OpCodes.Ret);
        return thunk.CreateDelegate<Func<byte[],object>>();
    }
    internal static bool Exercise(ReadOnlySpan<byte> data)
    {try{return Decode(data.ToArray()) is not null;}catch(IOException){return false;}}
}

static class Journal
{
    private static readonly MethodInfo Lines=typeof(JobJournal).GetMethods(BindingFlags.Static|BindingFlags.NonPublic)
        .Single(method=>method.Name=="ReadBoundedLines"&&method.GetParameters() is
            [{ParameterType:var first},_ ]&&first==typeof(Stream));
    private static readonly MethodInfo Records=typeof(JobJournal).GetMethods(BindingFlags.Static|BindingFlags.NonPublic)
        .Single(method=>method.Name=="ReadRecords"&&method.GetParameters() is
            [{ParameterType:var first},_ ]&&first==typeof(IEnumerable<string>));
    internal static bool Exercise(ReadOnlySpan<byte> data)
    {
        using var input=new MemoryStream(data.ToArray(),writable:false);
        bool damaged=false;Action report=()=>damaged=true;
        try
        {
            var lines=(IEnumerable<string>)Lines.Invoke(null,[input,false])!;
            var records=(IEnumerable<JournalRecord>)Records.Invoke(null,[lines,report])!;
            int count=0;
            foreach(var record in records)
            {
                if(++count>4096)return false;
                if(record.Type.Length==0)throw new InvalidOperationException("A journal record lacks its type.");
            }
            return count>0&&!damaged;
        }
        catch(TargetInvocationException error)when(error.InnerException is IOException){return false;}
        catch(IOException){return false;}
    }
}

static class JournalState
{
    private static readonly Type Collector=typeof(JobJournal).Assembly.GetType(
        "FileCat.Core.Archives.ArchiveTransactionCollector",throwOnError:true)!;
    private static readonly ConstructorInfo Create=Collector.GetConstructor(
        BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,Type.EmptyTypes,null)
        ??throw new InvalidOperationException("Production archive recovery collector is unavailable.");
    private static readonly MethodInfo Read=Collector.GetMethod("Read",BindingFlags.Instance|BindingFlags.Public)
        ??throw new InvalidOperationException("Production archive recovery state reader is unavailable.");
    private static readonly MethodInfo Damage=Collector.GetMethod("DamagedRecord",BindingFlags.Instance|BindingFlags.Public)
        ??throw new InvalidOperationException("Production archive recovery damage marker is unavailable.");
    private static readonly PropertyInfo Snapshot=Collector.GetProperty("Snapshot",BindingFlags.Instance|BindingFlags.Public)
        ??throw new InvalidOperationException("Production archive recovery snapshot is unavailable.");
    private static readonly MethodInfo Lines=typeof(JobJournal).GetMethods(BindingFlags.Static|BindingFlags.NonPublic)
        .Single(method=>method.Name=="ReadBoundedLines"&&method.GetParameters() is
            [{ParameterType:var first},_ ]&&first==typeof(Stream));
    private static readonly MethodInfo Records=typeof(JobJournal).GetMethods(BindingFlags.Static|BindingFlags.NonPublic)
        .Single(method=>method.Name=="ReadRecords"&&method.GetParameters() is
            [{ParameterType:var first},_ ]&&first==typeof(IEnumerable<string>));
    internal static bool Exercise(ReadOnlySpan<byte> data)
    {
        using var input=new MemoryStream(data.ToArray(),writable:false);
        bool damaged=false;Action report=()=>damaged=true;
        try
        {
            object collector=Create.Invoke(null);
            var lines=(IEnumerable<string>)Lines.Invoke(null,[input,false])!;
            var records=(IEnumerable<JournalRecord>)Records.Invoke(null,[lines,report])!;
            int count=0;
            foreach(var record in records)
            {
                if(++count>4096)return false;
                Read.Invoke(collector,[record.Type,record.Node]);
            }
            if(damaged)Damage.Invoke(collector,null);
            var snapshot=(ArchiveTransactionRecovery)Snapshot.GetValue(collector)!;
            if(snapshot.HasRecords&&damaged&&!snapshot.RequiresReview)
                throw new InvalidOperationException("Damaged archive history was accepted without recovery review.");
            if(snapshot.HasRecords&&snapshot.Problem is not null&&!snapshot.RequiresReview)
                throw new InvalidOperationException("Unsupported archive history was accepted without recovery review.");
            return count>0&&!damaged&&!snapshot.RequiresReview;
        }
        catch(TargetInvocationException error)when(error.InnerException is IOException){return false;}
        catch(IOException){return false;}
    }
}

static class ManifestMember
{
    internal static bool Exercise(ReadOnlySpan<byte> data)
    {
        try
        {
            var value=ArchiveAssessmentWire.ReadMember(data,new ArchiveWriteBudget());
            return value.Name.Length>0;
        }
        catch(ArchiveWriteException){return false;}
    }
}

static class Capture
{
    private static readonly MethodInfo Parser=typeof(FileCat.Core.Edit.EditSessionStore).GetMethod("ParseCaptureRecord",BindingFlags.Static|BindingFlags.NonPublic)
        ??throw new InvalidOperationException("Production capture parser is unavailable.");
    internal static bool Exercise(ReadOnlySpan<byte> data)
    {
        if(data.Length<2||data[0] is not ((byte)'0' or (byte)'1'))return false;
        try{return Parser.Invoke(null,[data[1..].ToArray(),data[0]==(byte)'1']) is not null;}
        catch(TargetInvocationException error)when(error.InnerException is IOException or ArgumentException or JsonException or InvalidOperationException)
        {return false;}
        catch(TargetInvocationException error)when(error.InnerException is not null)
        {ExceptionDispatchInfo.Capture(error.InnerException).Throw();throw;}
    }
}

static class Intent
{
    private static readonly MethodInfo Parser=typeof(FileCat.Core.Edit.EditSessionStore).Assembly
        .GetType("FileCat.Core.Edit.EditSaveCopyIntent",throwOnError:true)!
        .GetMethod("Decode",BindingFlags.Static|BindingFlags.NonPublic)
        ??throw new InvalidOperationException("Production saved-copy intent parser is unavailable.");
    internal static bool Exercise(ReadOnlySpan<byte> data)
    {
        try{return Parser.Invoke(null,[data.ToArray()]) is not null;}
        catch(TargetInvocationException error)when(error.InnerException is IOException){return false;}
        catch(TargetInvocationException error)when(error.InnerException is not null)
        {ExceptionDispatchInfo.Capture(error.InnerException).Throw();throw;}
    }
}

static class Session
{
    private static readonly string Directory=OperatingSystem.IsWindows()
        ? @"C:\filecat-fuzz-session\0123456789abcdef0123456789abcdef"
        : "/tmp/filecat-fuzz-session/0123456789abcdef0123456789abcdef";
    private static readonly MethodInfo Parser=typeof(FileCat.Core.Edit.EditSessionStore).GetMethod("ParseRecord",BindingFlags.Static|BindingFlags.NonPublic)
        ??throw new InvalidOperationException("Production edit-session parser is unavailable.");
    internal static bool Exercise(ReadOnlySpan<byte> data)
    {
        FileCat.Core.Edit.EditSessionRecord record;
        try{record=(FileCat.Core.Edit.EditSessionRecord)Parser.Invoke(null,[data.ToArray(),Directory])!;}
        catch(TargetInvocationException error)when(error.InnerException is not null)
        {ExceptionDispatchInfo.Capture(error.InnerException).Throw();throw;}
        if(record.IsSupported)
        {
            if(record.Version is not (1 or FileCat.Core.Edit.EditSessionRecord.CurrentVersion or FileCat.Core.Edit.EditSessionRecord.NativeStorageVersion)||
                !Path.GetFullPath(record.WorkingPath).StartsWith(Directory+Path.DirectorySeparatorChar,StringComparison.Ordinal))
                throw new InvalidOperationException("A supported edit record escaped its bounded session namespace.");
            return true;
        }
        if(record.UnsupportedReason is null||record.Id!="0123456789abcdef0123456789abcdef"||
            !Path.GetFullPath(record.WorkingPath).StartsWith(Directory,StringComparison.Ordinal))
            throw new InvalidOperationException("An unsupported edit record lost its recovery location.");
        return false;
    }
}

static class Stats
{
    internal static long Calls,Accepted,Refused;
    internal static readonly HashSet<string> Methods=new(StringComparer.Ordinal);
    internal static string Module="",ModuleSha256="",SecondaryModule="",SecondaryModuleSha256="",Target="";
    internal static bool CanaryPrefixMode;
    internal static string? Report;
    internal static void Publish()
    {
        if(Report is null)return;
        var summary=JsonSerializer.Serialize(new{Calls,Accepted,Refused,Target,CanaryPrefixMode,Module,ModuleSha256,
            SecondaryModule,SecondaryModuleSha256,production_methods=Methods.Order().ToArray()});
        File.WriteAllText(Report+".tmp",summary);File.Move(Report+".tmp",Report,overwrite:true);
    }
}

static class Tar
{
    internal static MethodInfo Read=null!,Compare=null!;
    internal static void Exercise(ReadOnlySpan<byte> data)
    {
        using var stream=new MemoryStream(data.ToArray(),writable:false);
        object catalog=Invoke(Read,stream,CancellationToken.None)!;
        stream.Position=0;
        Invoke(Compare,stream,catalog,CancellationToken.None);
    }
    private static object? Invoke(MethodInfo method,params object[] arguments)
    {
        try{return method.Invoke(null,arguments);}
        catch(TargetInvocationException error)when(error.InnerException is not null)
        {ExceptionDispatchInfo.Capture(error.InnerException).Throw();throw;}
    }
}

static class Format
{
    internal static MethodInfo SevenRead=null!,RarRead=null!;
    private static ConstructorInfo WrapperRead=null!;
    private static Func<string,IDisposable>? PasswordSession;
    private const string SevenPassword="Public-7z-catalog-暗号-42829323";
    private const string RarPassword="Public-RAR-Transform-暗号-42829316";
    internal static void Bind(string target)
    {
        var assembly=Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory,"FileCat.Archives.dll"));
        Stats.SecondaryModule=assembly.Location;
        Stats.SecondaryModuleSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Stats.SecondaryModule)));
        if(target is "gzip-strict" or "bzip2-strict" or "zstd-strict")
        {
            string name=target switch
            {
                "gzip-strict"=>"StrictGzipReadStream",
                "bzip2-strict"=>"StrictBzip2ReadStream",
                _=>"StrictZstdReadStream",
            };
            var type=assembly.GetType("FileCat.Archives.Writing."+name,throwOnError:true)!;
            WrapperRead=type.GetConstructor(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,
                [typeof(Stream),typeof(CancellationToken)],null)
                ??throw new InvalidOperationException("Production strict wrapper constructor changed.");
            return;
        }
        if(target=="tar-strict")
        {
            var type=assembly.GetType("FileCat.Archives.Writing.StrictTarCatalog",throwOnError:true)!;
            Tar.Read=type.GetMethod("Read",BindingFlags.Static|BindingFlags.NonPublic)!;
            Tar.Compare=type.GetMethod("CompareDecoder",BindingFlags.Static|BindingFlags.NonPublic)!;
            if(Tar.Read is null||Tar.Compare is null)throw new InvalidOperationException("Production TAR scanner/decoder entry is absent.");
        }
        else if(target is "sevenzip-strict" or "sevenzip-password")
        {
            var type=assembly.GetType("FileCat.Archives.Writing.StrictSevenZipCatalog",throwOnError:true)!;
            SevenRead=type.GetMethod("Read",BindingFlags.Static|BindingFlags.NonPublic)!;
            if(SevenRead is null||SevenRead.GetParameters().Length!=3)
                throw new InvalidOperationException("Production 7z catalog entry changed.");
        }
        else
        {
            var type=assembly.GetType("FileCat.Archives.Writing.StrictRar5Catalog",throwOnError:true)!;
            RarRead=type.GetMethod("Read",BindingFlags.Static|BindingFlags.NonPublic)!;
            if(RarRead is null||RarRead.GetParameters().Length!=3)
                throw new InvalidOperationException("Production RAR5 catalog entry changed.");
        }
        if(target is "sevenzip-password" or "rar5-password")PasswordSession=BindPassword(assembly,target=="sevenzip-password");
    }
    private static Func<string,IDisposable> BindPassword(Assembly assembly,bool seven)
    {
        var budget=assembly.GetType("FileCat.Archives.Writing.ArchiveCryptoWorkBudget",throwOnError:true)!;
        var budgetCtor=budget.GetConstructor(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,
            [typeof(long),typeof(long)],null)??throw new InvalidOperationException("Production crypto budget constructor changed.");
        var session=assembly.GetType(seven?"FileCat.Archives.SevenZipReadPasswordSession":"FileCat.Archives.Rar.Rar5ReadPasswordSession",throwOnError:true)!;
        var constructor=session.GetConstructor(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,
            [typeof(ReadOnlySpan<char>),budget,typeof(CancellationToken)],null)
            ??throw new InvalidOperationException("Production password session constructor changed.");
        var asSpan=typeof(MemoryExtensions).GetMethod("AsSpan",[typeof(string)])
            ??throw new InvalidOperationException("String-to-span adapter changed.");
        var thunk=new ReflectionEmit.DynamicMethod("FileCatFuzzPasswordSession",typeof(IDisposable),[typeof(string)],typeof(Format).Module,true);
        var il=thunk.GetILGenerator();var token=il.DeclareLocal(typeof(CancellationToken));
        il.Emit(ReflectionEmit.OpCodes.Ldarg_0);il.Emit(ReflectionEmit.OpCodes.Call,asSpan);
        il.Emit(ReflectionEmit.OpCodes.Ldc_I8,1L<<22);il.Emit(ReflectionEmit.OpCodes.Ldc_I8,2L<<30);
        il.Emit(ReflectionEmit.OpCodes.Newobj,budgetCtor);
        il.Emit(ReflectionEmit.OpCodes.Ldloca_S,token);il.Emit(ReflectionEmit.OpCodes.Initobj,typeof(CancellationToken));
        il.Emit(ReflectionEmit.OpCodes.Ldloc,token);il.Emit(ReflectionEmit.OpCodes.Newobj,constructor);
        il.Emit(ReflectionEmit.OpCodes.Castclass,typeof(IDisposable));il.Emit(ReflectionEmit.OpCodes.Ret);
        return thunk.CreateDelegate<Func<string,IDisposable>>();
    }
    internal static bool Exercise(ReadOnlySpan<byte> data,string target)
    {
        try
        {
            if(target is "gzip-strict" or "bzip2-strict" or "zstd-strict")
            {
                using var source=new MemoryStream(data.ToArray(),writable:false);
                Stream decoder;
                try{decoder=(Stream)WrapperRead.Invoke([source,CancellationToken.None]);}
                catch(TargetInvocationException error)when(error.InnerException is not null)
                {ExceptionDispatchInfo.Capture(error.InnerException).Throw();throw;}
                using(decoder)
                {
                    Span<byte> block=stackalloc byte[4096];long output=0;
                    int count;
                    while((count=decoder.Read(block))!=0)
                    {
                        output+=count;
                        if(output>(16L<<20))return false; // Fuzz output budget; no partial decode counts as accepted.
                    }
                }
            }
            else if(target=="tar-strict")Tar.Exercise(data);
            else
            {
                using var stream=new MemoryStream(data.ToArray(),writable:false);
                using var password=PasswordSession?.Invoke(target=="sevenzip-password"?SevenPassword:RarPassword);
                try{_ = (target.StartsWith("sevenzip",StringComparison.Ordinal)?SevenRead:RarRead).Invoke(null,[stream,CancellationToken.None,password]);}
                catch(TargetInvocationException error)when(error.InnerException is not null)
                {ExceptionDispatchInfo.Capture(error.InnerException).Throw();throw;}
            }
            return true;
        }
        catch(ArchiveWriteException){return false;}
    }
}
