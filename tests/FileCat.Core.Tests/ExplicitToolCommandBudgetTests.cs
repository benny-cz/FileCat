using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FileCat.Core.State;
using FileCat.Core.Tools;
namespace FileCat.Core.Tests;
public sealed class ExplicitToolCommandBudgetTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(false,"short")]
    [InlineData(true,"short")]
    [InlineData(false,"boundary")]
    [InlineData(true,"boundary")]
    [InlineData(false,"one-over")]
    [InlineData(true,"one-over")]
    [InlineData(false,"large")]
    [InlineData(true,"large")]
    public void An_explicit_command_processor_plan_fits_its_actual_native_budget(bool upper, string mode)
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "Windows explicit-command-processor native control is unavailable on this platform.");
        string root=Directory.CreateTempSubdirectory("filecat-explicit-tool-budget-").FullName;
        try
        {
            string exe=Path.Combine(Environment.SystemDirectory,upper?"CMD.EXE":"cmd.exe");string input=Path.Combine(root,"owned.bin");File.WriteAllBytes(input,[1,2,3]);
            string prefix="> receipt.txt echo ";int overhead=ToolLauncher.CommandLineLength(exe,["/d","/v:off","/s","/c",prefix]);int count=mode=="short"?64:mode=="large"?9000:8192-overhead+(mode=="one-over"?1:0);
            string[] args=["/d","/v:off","/s","/c",prefix+new string('X',count)];var tool=new ToolDefinition{Name="Owned explicit command processor",Executable=exe,Arguments=args.ToList()};var context=new ToolContext([input],root);
            IReadOnlyList<(string Executable,IReadOnlyList<string> Arguments)>? plan=null;Exception? failure=null;string? warning=null;
            try{plan=ToolLauncher.Plan(tool,context,root,out warning);}catch(Exception ex){failure=ex;}
            bool refused=mode is "one-over" or "large";NativeResult? native=null;byte[]? receipt=null;
            if(!refused&&plan is {Count:1}){native=Run(plan[0].Executable,plan[0].Arguments,root);receipt=File.Exists(Path.Combine(root,"receipt.txt"))?File.ReadAllBytes(Path.Combine(root,"receipt.txt")):null;}
            byte[] expected=Encoding.ASCII.GetBytes(new string('X',count)+"\r\n");
            output.WriteLine(JsonSerializer.Serialize(new{Case=mode,UppercaseExecutable=upper,ActualExpectedRefusal=refused,ActualCommandLineLength=ToolLauncher.CommandLineLength(exe,args),ActualPlanCount=plan?.Count??0,ActualErrorType=failure?.GetType().FullName,ActualErrorMessage=failure?.Message,ActualErrorStack=failure?.StackTrace,ActualWarning=warning,ActualNative=native,ActualReceiptBytes=receipt?.Length,ActualReceiptSHA256=receipt is null?null:Hash(receipt),ActualExpectedReceiptBytes=expected.Length,ActualExpectedReceiptSHA256=Hash(expected),ActualFullBytesExact=receipt is not null&&receipt.SequenceEqual(expected),ActualInputCharacters=count,ActualInputUnchanged=File.ReadAllBytes(input).SequenceEqual(new byte[]{1,2,3}),ActualCommandProcessor=exe,ActualOwnedFixtureRoot=root,NoOrdinaryToolUIInjectionSecurityOrCandidateClaim=true}));
            if(refused){Assert.IsType<ToolLaunchException>(failure);Assert.Null(plan);Assert.Null(native);Assert.False(File.Exists(Path.Combine(root,"receipt.txt")));return;}
            Assert.Null(failure);Assert.NotNull(plan);Assert.Single(plan);Assert.Equal(args,plan[0].Arguments);Assert.Null(warning);Assert.NotNull(native);Assert.Equal(0,native.Exit);Assert.True(native.Exited);Assert.Equal(expected,receipt);
            if(mode=="boundary")Assert.Equal(8192,ToolLauncher.CommandLineLength(exe,args));
        }
        finally{Directory.Delete(root,true);}
    }
    [Theory]
    [InlineData("cmd.exe")]
    [InlineData("CMD.EXE")]
    public void Explicit_processor_plans_keep_the_existing_portable_policy(string name)
    {
        string root=Directory.CreateTempSubdirectory("filecat-portable-explicit-tool-").FullName;
        try
        {
            string exe=Path.Combine(root,name);File.WriteAllBytes(exe,[1,2,3]);var tool=new ToolDefinition{Name="Owned executable shape",Executable=exe,Arguments=[new string('X',9000)]};var context=new ToolContext([],root);Exception? error=null;int? planned=null;
            try{planned=ToolLauncher.Plan(tool,context,root,out _).Count;}catch(Exception ex){error=ex;}
            output.WriteLine(JsonSerializer.Serialize(new{Case="portable-explicit",Name=name,ActualWindows=OperatingSystem.IsWindows(),ActualPlanCount=planned,ActualErrorType=error?.GetType().FullName,ActualErrorMessage=error?.Message,ActualOwnedFixtureRoot=root,ActualInputUnchanged=File.ReadAllBytes(exe).SequenceEqual(new byte[]{1,2,3}),NoNativeProcessStarted=true}));
            if(OperatingSystem.IsWindows()){Assert.IsType<ToolLaunchException>(error);Assert.Null(planned);}else{Assert.Null(error);Assert.Equal(1,planned);}
        }
        finally{Directory.Delete(root,true);}
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void An_ordinary_executable_keeps_the_existing_32767_budget(bool over)
    {
        string root=Directory.CreateTempSubdirectory("filecat-ordinary-tool-budget-").FullName;
        try
        {
            string exe=Path.Combine(root,"owned.exe");File.WriteAllBytes(exe,[1,2,3]);int count=32767-exe.Length-4+(over?1:0);var tool=new ToolDefinition{Name="Owned ordinary executable shape",Executable=exe,Arguments=[new string('X',count)]};Exception? error=null;int? planned=null;
            try{planned=ToolLauncher.Plan(tool,new ToolContext([],root),root,out _).Count;}catch(Exception ex){error=ex;}
            output.WriteLine(JsonSerializer.Serialize(new{Case="ordinary-executable",Over=over,ActualCommandLineLength=ToolLauncher.CommandLineLength(exe,tool.Arguments),ActualPlanCount=planned,ActualErrorType=error?.GetType().FullName,ActualErrorMessage=error?.Message,ActualOwnedFixtureRoot=root,ActualInputUnchanged=File.ReadAllBytes(exe).SequenceEqual(new byte[]{1,2,3}),NoNativeProcessStarted=true}));
            if(over){Assert.IsType<ToolLaunchException>(error);Assert.Null(planned);}else{Assert.Null(error);Assert.Equal(1,planned);}
        }
        finally{Directory.Delete(root,true);}
    }
    [Theory]
    [InlineData(".cmd",false)]
    [InlineData(".cmd",true)]
    [InlineData(".bat",false)]
    [InlineData(".bat",true)]
    public void A_batch_named_cmd_keeps_its_implicit_processor_allowance(string extension,bool over)
    {
        string root=Directory.CreateTempSubdirectory("filecat-named-cmd-batch-").FullName;
        try
        {
            string exe=Path.Combine(root,"cmd"+extension);File.WriteAllBytes(exe,[1,2,3]);string processor=Environment.GetEnvironmentVariable("ComSpec") is {Length:>0} configured?configured:Path.Combine(Environment.SystemDirectory,"cmd.exe");int limit=OperatingSystem.IsWindows()?8191-processor.Length-5:32767;
            int count=limit-exe.Length-4+(over?1:0);var tool=new ToolDefinition{Name="Owned named-cmd batch shape",Executable=exe,Arguments=[new string('X',count)]};Exception? error=null;int? planned=null;
            try{planned=ToolLauncher.Plan(tool,new ToolContext([],root),root,out _).Count;}catch(Exception ex){error=ex;}
            output.WriteLine(JsonSerializer.Serialize(new{Case="named-batch",Extension=extension,Over=over,ActualWindows=OperatingSystem.IsWindows(),ActualCommandProcessor=processor,ActualExpectedLimit=limit,ActualCommandLineLength=ToolLauncher.CommandLineLength(exe,tool.Arguments),ActualPlanCount=planned,ActualErrorType=error?.GetType().FullName,ActualErrorMessage=error?.Message,ActualOwnedFixtureRoot=root,ActualInputUnchanged=File.ReadAllBytes(exe).SequenceEqual(new byte[]{1,2,3}),NoNativeProcessStarted=true}));
            if(over){Assert.IsType<ToolLaunchException>(error);Assert.Null(planned);}else{Assert.Null(error);Assert.Equal(1,planned);}
        }
        finally{Directory.Delete(root,true);}
    }
    private static string Hash(byte[] b)=>Convert.ToHexString(SHA256.HashData(b));
    private sealed record NativeResult(int PID,string CreationUTC,int Exit,bool Exited,string Stdout,string Stderr);
    private static NativeResult Run(string exe,IReadOnlyList<string> args,string root)
    {
        var psi=new ProcessStartInfo(exe){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=root,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};foreach(string a in args)psi.ArgumentList.Add(a);
        using var child=Process.Start(psi)??throw new InvalidOperationException("No owned processor returned");int pid=child.Id;string created=child.StartTime.ToUniversalTime().ToString("O");var stdout=child.StandardOutput.ReadToEndAsync();var stderr=child.StandardError.ReadToEndAsync();child.StandardInput.Close();
        if(!child.WaitForExit(10000)){child.Kill();child.WaitForExit();throw new TimeoutException("Owned command processor exceeded ten seconds");}
        return new(pid,created,child.ExitCode,child.HasExited,stdout.GetAwaiter().GetResult(),stderr.GetAwaiter().GetResult());
    }
}