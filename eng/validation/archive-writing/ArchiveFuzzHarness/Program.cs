using System.Security.Cryptography;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.Json;
using FileCat.Core.Archives;
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
            name.StartsWith("FileCat.Core.Edit.EditSessionStore",StringComparison.Ordinal),Options.Value).ToArray();
    File.WriteAllText(typeReport,JsonSerializer.Serialize(types));
    if(!types.Contains("FileCat.Core.Archives.ArchiveWritePolicy")||!types.Contains("FileCat.Core.Edit.EditSessionStore"))
        throw new InvalidOperationException("Production guard or session parser was not instrumented.");
    Console.WriteLine(JsonSerializer.Serialize(new{instrumented_types=types.Length,production_guard=true,production_session=true}));return 0;
}
if(args is ["instrument-tar",var tarAssembly,var tarTypeReport])
{
    var types=Fuzzer.Instrument(tarAssembly,
        name=>name.StartsWith("FileCat.Archives.Writing.StrictTarCatalog",StringComparison.Ordinal)||
            name.StartsWith("FileCat.Archives.TarHeaderGuard",StringComparison.Ordinal),Options.Value).ToArray();
    File.WriteAllText(tarTypeReport,JsonSerializer.Serialize(types));
    if(!types.Contains("FileCat.Archives.Writing.StrictTarCatalog")||!types.Contains("FileCat.Archives.TarHeaderGuard"))
        throw new InvalidOperationException("Production strict TAR scanner/decoder guard was not instrumented.");
    Console.WriteLine(JsonSerializer.Serialize(new{instrumented_types=types.Length,production_tar=true}));return 0;
}
if(args.Length!=0)throw new ArgumentException("Fuzz targets receive input through the bounded native driver, not command-line paths.");

Stats.Target=Environment.GetEnvironmentVariable("FILECAT_FUZZ_TARGET")??"digest";
if(Stats.Target is not ("digest" or "tar-strict" or "session-record"))throw new ArgumentException("Unknown fuzz target.");
Stats.CanaryPrefixMode=Environment.GetEnvironmentVariable("FILECAT_FUZZ_CANARY_PREFIX_MODE")=="1";
if(Stats.CanaryPrefixMode&&Stats.Target!="digest")throw new ArgumentException("The canary input transform is digest-only.");
Stats.Module=typeof(ArchiveWritePolicy).Assembly.Location;
Stats.ModuleSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Stats.Module)));
if(Stats.Target=="tar-strict")
{
    var assembly=Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory,"FileCat.Archives.dll"));
    Stats.SecondaryModule=assembly.Location;
    Stats.SecondaryModuleSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Stats.SecondaryModule)));
    var type=assembly.GetType("FileCat.Archives.Writing.StrictTarCatalog",throwOnError:true)!;
    Tar.Read=type.GetMethod("Read",BindingFlags.Static|BindingFlags.NonPublic)!;
    Tar.Compare=type.GetMethod("CompareDecoder",BindingFlags.Static|BindingFlags.NonPublic)!;
    if(Tar.Read is null||Tar.Compare is null)throw new InvalidOperationException("Production TAR scanner/decoder entry is absent.");
}
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
        if(Stats.Target=="session-record")
        {
            if(Session.Exercise(data))Stats.Accepted++;else Stats.Refused++;
            if(Stats.Calls==1||(Stats.Calls&4095)==0)Stats.Publish();
            return;
        }
        if(Stats.Target=="tar-strict")Tar.Exercise(data);
        else
        {
            string digest=Encoding.Latin1.GetString(data);
            if(Stats.CanaryPrefixMode&&data.Length==2)digest+=new string('0',62);
            ArchiveWritePolicy.ValidateDigest(digest);
        }
        Stats.Accepted++;
    }
    catch(ArchiveWriteException){Stats.Refused++;}
    catch{Stats.Publish();throw;}
    if(Stats.Calls==1||(Stats.Calls&4095)==0)Stats.Publish();
}

static class Session
{
    private const string Directory="/tmp/filecat-fuzz-session/0123456789abcdef0123456789abcdef";
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
