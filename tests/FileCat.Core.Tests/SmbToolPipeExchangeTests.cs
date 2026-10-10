using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FileCat.Core.Network;
using FileCat.Core.Tools;

namespace FileCat.Core.Tests;

[CollectionDefinition("Owned SMB pipe exchanges", DisableParallelization = true)]
public sealed class SmbToolPipeCollection;

[Collection("Owned SMB pipe exchanges")]
public sealed class SmbToolPipeExchangeTests(ITestOutputHelper output)
{
    public static TheoryData<string, string, int, int> Exchanges => new()
    {
        { "small-both", "stdout", 1024, 1024 },
        { "slow-owned-startup", "stdout", 1024, 1024 },
        { "large-input-only", "stdout", 0, 1048576 },
        { "large-output-only", "stdout", 131072, 0 },
        { "large-stderr-only", "stderr", 131072, 0 },
        { "large-both-stdout", "stdout", 131072, 1048576 },
        { "large-both-stderr", "stderr", 131072, 1048576 },
    };

    [Theory]
    [MemberData(nameof(Exchanges))]
    public Task Child_input_and_output_are_serviced_together(string name, string channel, int prefixBytes, int inputBytes) =>
        Observe(name, channel, prefixBytes, inputBytes, "normal");

    [Fact]
    public Task The_original_output_cap_still_drains_the_child() => Observe("bounded-output", "stdout", 2097152, 0, "cap");

    [Fact]
    public Task The_original_deadline_still_ends_an_owned_child() => Observe("deadline", "stderr", 1024, 1024, "deadline");

    [Fact]
    public Task User_cancellation_still_ends_an_owned_child() => Observe("cancel", "stdout", 1024, 1024, "cancel");

    [Fact]
    public Task Delayed_owned_startup_does_not_spend_the_cancellation_window() =>
        Observe("slow-cancellation-startup", "stdout", 1024, 1024, "cancel");

    private async Task Observe(string name, string channel, int prefixBytes, int inputBytes, string mode)
    {
        string? configured = Environment.GetEnvironmentVariable("FILECAT_PYTHON");
        string? python = configured is not null && Path.IsPathFullyQualified(configured) && File.Exists(configured)
            ? configured : ToolLauncher.FindOnPath(OperatingSystem.IsWindows() ? "python" : "python3");
        Assert.SkipWhen(python is null, "No Python interpreter is available for the owned pipe exchange control.");
        string root = Directory.CreateTempSubdirectory("filecat-owned-smb-pipe-").FullName;
        string script = Path.Combine(root, "child.py"), ledger = Path.Combine(root, "child.jsonl");
        File.WriteAllText(script, Child, new UTF8Encoding(false));
        string? input = inputBytes == 0 ? null : new string('I', inputBytes);
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        JsonElement[] phases = [];
        Task<(int Code, string Output, string Errors)>? pending = null;
        var clock = Stopwatch.StartNew();
        bool inputReady = false;
        long? cancellationRequestedMilliseconds = null;
        try
        {
            int startupDelaySeconds = name is "slow-owned-startup" or "slow-cancellation-startup" ? 6 : 0;
            string[] arguments = [script, ledger, channel, prefixBytes.ToString(), mode is "deadline" or "cancel" ? "hang" : "normal", startupDelaySeconds.ToString()];
            // A healthy pipe exchange includes interpreter startup on the hosted runner.
            // Keep the adverse deadline/cancellation controls short and independent.
            int toolTimeoutSeconds = mode is "normal" or "cap" ? 30 : mode == "deadline" ? 2 : 30;
            pending = SmbTools.RunAsync(python!, arguments, TimeSpan.FromSeconds(toolTimeoutSeconds), cancel.Token, input);
            if (mode == "cancel")
            {
                while (!ReadPhases(ledger).Any(v => v.GetProperty("Phase").GetString() == "input-read"))
                {
                    Assert.True(clock.Elapsed < TimeSpan.FromSeconds(25), "The owned child did not finish its input fence.");
                    await Task.Delay(10, TestContext.Current.CancellationToken);
                }
                inputReady = true;
                cancellationRequestedMilliseconds = clock.ElapsedMilliseconds;
                cancel.Cancel();
            }
            (int Code, string Output, string Errors)? observed = null;
            Exception? error = null;
            try { observed = await pending.WaitAsync(TimeSpan.FromSeconds(35), TestContext.Current.CancellationToken); }
            catch (Exception failure) { error = failure; }
            clock.Stop();
            phases = ReadPhases(ledger);
            bool childExited = phases.Length > 0 && await ChildExited(phases[0].GetProperty("PID").GetInt32());
            byte[] actualOutput = Encoding.UTF8.GetBytes(observed?.Output ?? ""), actualErrors = Encoding.UTF8.GetBytes(observed?.Errors ?? "");
            output.WriteLine(JsonSerializer.Serialize(new
            {
                Case = name, Channel = channel, PrefixBytes = prefixBytes, InputBytes = inputBytes, Mode = mode,
                ControlledStartupDelaySeconds = startupDelaySeconds, ToolTimeoutSeconds = toolTimeoutSeconds,
                CancellationRequestedMilliseconds = cancellationRequestedMilliseconds,
                CancellationCompletionMilliseconds = cancellationRequestedMilliseconds is { } requested ? clock.ElapsedMilliseconds - requested : (long?)null,
                ErrorType = error?.GetType().Name, ErrorMessage = error?.Message, ActualExitCode = observed?.Code,
                ElapsedMilliseconds = clock.ElapsedMilliseconds, ActualOutputBytes = actualOutput.Length, ActualErrorBytes = actualErrors.Length,
                ActualOutputSHA256 = Convert.ToHexString(SHA256.HashData(actualOutput)), ActualErrorsSHA256 = Convert.ToHexString(SHA256.HashData(actualErrors)),
                ActualInputSHA256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input ?? ""))),
                ActualChildPhases = phases, ActualKnownChildExited = childExited, ChildScriptSHA256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(script))),
                OwnedProcessOnly = true, NoSMBServerPasswordOrSecurityBoundaryIncidenceClaim = true,
            }));
            Assert.True(childExited, "The exact owned child must exit before fixture cleanup.");
            Assert.DoesNotContain(phases, v => v.GetProperty("Phase").GetString() == "watchdog-exit");
            if (mode == "deadline") Assert.IsType<TimeoutException>(error);
            else if (mode == "cancel")
            {
                Assert.IsAssignableFrom<OperationCanceledException>(error);
                Assert.True(cancellationRequestedMilliseconds is { } requestedAt && clock.ElapsedMilliseconds - requestedAt < 5000,
                    "Cancellation must end the owned child promptly after the input fence, independently of startup.");
            }
            else
            {
                Assert.Null(error);
                Assert.Equal(0, observed!.Value.Code);
                byte[] selected = channel == "stderr" ? actualErrors : actualOutput;
                byte[] other = channel == "stderr" ? actualOutput : actualErrors;
                Assert.Empty(other);
                if (mode == "cap") Assert.InRange(selected.Length, 1 << 20, (1 << 20) + 8191);
                else Assert.Equal(prefixBytes, selected.Length);
                Assert.Equal(Enumerable.Repeat((byte)'P', selected.Length).ToArray(), selected);
                var received = Assert.Single(phases, p => p.GetProperty("Phase").GetString() == "input-read");
                Assert.Equal(inputBytes, received.GetProperty("InputBytes").GetInt32());
                Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input ?? ""))), received.GetProperty("InputSHA256").GetString());
            }
        }
        finally
        {
            cancel.Cancel();
            if (pending is not null)
                try { await pending.WaitAsync(TimeSpan.FromSeconds(35)); }
                catch (Exception ex) when (ex is OperationCanceledException or TimeoutException or IOException) { }
            Assert.True(pending is null || pending.IsCompleted, "Owned work must finish before its fixture is removed.");
            var current = ReadPhases(ledger);
            output.WriteLine("SMB_CANCELLATION_RESTORATION " + JsonSerializer.Serialize(new
            {
                name, mode, inputReady, elapsedMilliseconds = clock.ElapsedMilliseconds,
                pendingCompleted = pending?.IsCompleted, phases = current, root,
                ownedProcessOnly = true, noHistoricalHostedCauseClaim = true,
            }));
            if (current.Length > 0) Assert.True(await ChildExited(current[0].GetProperty("PID").GetInt32()));
            string parent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) + Path.DirectorySeparatorChar;
            Assert.StartsWith(parent, Path.GetFullPath(root));
            Directory.Delete(root, recursive: true);
            Assert.False(Directory.Exists(root));
            output.WriteLine("SMB_PIPE_FIXTURE_REMOVED " + JsonSerializer.Serialize(new { root, absent = !Directory.Exists(root), pendingCompleted = pending?.IsCompleted }));
        }
    }

    private static JsonElement[] ReadPhases(string ledger)
    {
        if (!File.Exists(ledger)) return [];
        try { return File.ReadAllLines(ledger).Where(v => v.Length > 0).Select(v => JsonSerializer.Deserialize<JsonElement>(v)).ToArray(); }
        catch (IOException) { return []; }
        catch (JsonException) { return []; } // the only writer may be between its bounded phase writes
    }

    private static async Task<bool> ChildExited(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2));
            return true;
        }
        catch (ArgumentException) { return true; }
        catch (InvalidOperationException) { return true; }
        catch (TimeoutException) { return false; }
    }

    private const string Child = """
        import hashlib,json,os,sys,threading,time
        ledger,channel,prefix,mode,startup_delay=sys.argv[1:]
        time.sleep(int(startup_delay))
        prefix=int(prefix)
        def note(**values):
            values['PID']=os.getpid()
            with open(ledger,'a',encoding='utf-8') as output:
                output.write(json.dumps(values)+'\n')
        def watchdog():
            note(Phase='watchdog-exit')
            os._exit(7)
        timer=threading.Timer(8,watchdog)
        timer.daemon=True
        timer.start()
        note(Phase='started',Channel=channel,PrefixBytes=prefix)
        stream=sys.stderr.buffer if channel=='stderr' else sys.stdout.buffer
        raw=b'P'*prefix
        note(Phase='before-prefix-write')
        stream.write(raw)
        stream.flush()
        note(Phase='prefix-written',PrefixBytes=len(raw),PrefixSHA256=hashlib.sha256(raw).hexdigest().upper())
        received=sys.stdin.buffer.read()
        note(Phase='input-read',InputBytes=len(received),InputSHA256=hashlib.sha256(received).hexdigest().upper())
        if mode=='hang':
            time.sleep(20)
        """;
}
