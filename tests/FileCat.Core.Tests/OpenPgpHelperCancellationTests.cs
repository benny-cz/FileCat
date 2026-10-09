using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FileCat.Core.Tools;
using FileCat.Core.Verification;

namespace FileCat.Core.Tests;

[Collection(nameof(GnuPgToolOverride))]
public sealed class OpenPgpHelperCancellationTests(ITestOutputHelper output)
{
    [Fact]
    public Task Healthy_owned_helper_status_and_pipes_are_preserved() => Observe("healthy");

    [Fact]
    public Task Cancellation_after_status_does_not_wait_for_helper_exit() => Observe("latecancel");

    [Fact]
    public Task Precancelled_verification_does_not_start_the_selected_helper() => Observe("precancel");

    private async Task Observe(string mode)
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "This owned executable-script control requires Unix; native Windows apphost evidence is separate.");
        string? configured = Environment.GetEnvironmentVariable("FILECAT_PYTHON");
        string? python = configured is not null && Path.IsPathFullyQualified(configured) && File.Exists(configured)
            ? configured : ToolLauncher.FindOnPath("python3");
        Assert.SkipWhen(python is null || python.Any(char.IsWhiteSpace), "No absolute Python interpreter suitable for an owned Unix shebang is available.");
        string root = Directory.CreateTempSubdirectory("filecat-owned-gpg-cancel-").FullName;
        string helper = Path.Combine(root, "owned-gpg"), ledger = Path.Combine(root, "child.jsonl");
        string signature = Path.Combine(root, "owned.sig"), signed = Path.Combine(root, "owned.bin");
        File.WriteAllText(helper, "#!" + python + "\n" + Child, new UTF8Encoding(false));
        File.SetUnixFileMode(helper, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        byte[] sigBytes = Encoding.UTF8.GetBytes("owned synthetic helper fixture\n");
        byte[] dataBytes = Enumerable.Range(0, 4096).Select(n => (byte)(n * 29 + 7)).ToArray();
        File.WriteAllBytes(signature, sigBytes); File.WriteAllBytes(signed, dataBytes);
        File.WriteAllText(Path.Combine(root, "mode.txt"), mode, new UTF8Encoding(false));
        var tool = typeof(OpenPgp).GetField("_tool", BindingFlags.NonPublic | BindingFlags.Static)!;
        var looked = typeof(OpenPgp).GetField("_looked", BindingFlags.NonPublic | BindingFlags.Static)!;
        object? priorTool = tool.GetValue(null), priorLooked = looked.GetValue(null);
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        long cancelled = 0;
        try
        {
            OpenPgp.UseTool(helper);
            if (mode == "precancel") { cancelled = Stopwatch.GetTimestamp(); cancel.Cancel(); }
            long started = Stopwatch.GetTimestamp();
            var pending = Task.Run(() => OpenPgp.Verify(signature, signed, cancel.Token, root), CancellationToken.None);
            if (mode == "latecancel")
            {
                Stopwatch fence = Stopwatch.StartNew();
                while (!ReadPhases(ledger).Any(v => v.GetProperty("Phase").GetString() == "streams-written"))
                {
                    Assert.True(fence.Elapsed < TimeSpan.FromSeconds(4), "The owned helper did not complete its pipe fence.");
                    await Task.Delay(10, TestContext.Current.CancellationToken);
                }
                cancelled = Stopwatch.GetTimestamp(); cancel.Cancel();
            }
            SignatureResult? result = null; Exception? error = null;
            try { result = await pending.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken); }
            catch (Exception failure) { error = failure; }
            long returned = Stopwatch.GetTimestamp();
            var phases = ReadPhases(ledger);
            int? pid = phases.Length > 0 ? phases[0].GetProperty("PID").GetInt32() : null;
            bool exited = pid is null || await ChildExited(pid.Value);
            output.WriteLine(JsonSerializer.Serialize(new
            {
                Case = mode, ErrorType = error?.GetType().Name, ErrorMessage = error?.Message,
                State = result?.State.ToString(), Text = result?.Text, Signer = result?.Signer,
                CancellationToReturnMilliseconds = cancelled > 0 ? (returned - cancelled) * 1000.0 / Stopwatch.Frequency : (double?)null,
                CallStartedMonotonic = started, CancellationRequestedMonotonic = cancelled, CallReturnedMonotonic = returned,
                ActualMonotonicFrequency = Stopwatch.Frequency, ActualChildPID = pid, ActualKnownChildExited = exited,
                ActualChildPhases = phases, ActualChildNaturalExitObserved = phases.Any(v => v.GetProperty("Phase").GetString() == "natural-exit"),
                SignatureOriginalSHA256 = Hash(sigBytes), SignatureFinalSHA256 = Hash(File.ReadAllBytes(signature)),
                SignedOriginalSHA256 = Hash(dataBytes), SignedFinalSHA256 = Hash(File.ReadAllBytes(signed)),
                ChildScriptSHA256 = Hash(File.ReadAllBytes(helper)), ActualSelectedInterpreter = python,
                ActualOwnedFixtureFolder = root, OwnedSyntheticGpgHelper = true, NativeCryptographicOrOrdinaryGpgIncidenceNotClaimed = true,
            }));
            Assert.True(exited, "The exact owned child must exit before fixture cleanup.");
            Assert.DoesNotContain(phases, v => v.GetProperty("Phase").GetString() == "owned-watchdog");
            Assert.Equal(sigBytes, File.ReadAllBytes(signature)); Assert.Equal(dataBytes, File.ReadAllBytes(signed));
            if (mode == "healthy")
            {
                Assert.Null(error); Assert.NotNull(result); Assert.Equal(VerificationState.SignatureGood, result.State);
                Assert.Equal("Owned Synthetic Signer", result.Signer);
                Assert.Contains(phases, v => v.GetProperty("Phase").GetString() == "natural-exit");
            }
            else
            {
                Assert.IsAssignableFrom<OperationCanceledException>(error); Assert.Null(result);
                if (mode == "precancel") Assert.Empty(phases);
                else
                {
                    Assert.InRange((returned - cancelled) * 1000.0 / Stopwatch.Frequency, 0, 2000);
                    Assert.DoesNotContain(phases, v => v.GetProperty("Phase").GetString() == "natural-exit");
                }
            }
            if (phases.Length > 0)
            {
                string[] arguments = ["--homedir=" + root, "--batch", "--no-tty", "--no-auto-key-retrieve", "--status-fd", "1", "--verify", "--", signature, signed];
                Assert.All(phases, v => Assert.Equal(arguments, v.GetProperty("Arguments").EnumerateArray().Select(x => x.GetString()).ToArray()));
                var inputs = Assert.Single(phases, v => v.GetProperty("Phase").GetString() == "inputs-read");
                Assert.Equal(Hash(sigBytes), inputs.GetProperty("SignatureSHA256").GetString());
                Assert.Equal(Hash(dataBytes), inputs.GetProperty("SignedSHA256").GetString());
                var streams = Assert.Single(phases, v => v.GetProperty("Phase").GetString() == "streams-written");
                Assert.Equal(128 * 1024, streams.GetProperty("ActualErrorBytes").GetInt32());
                Assert.Equal(Hash(Enumerable.Repeat((byte)'P', 128 * 1024).ToArray()), streams.GetProperty("ActualErrorSHA256").GetString());
            }
        }
        finally
        {
            cancel.Cancel();
            tool.SetValue(null, priorTool); looked.SetValue(null, priorLooked);
            Assert.Equal(priorTool, tool.GetValue(null)); Assert.Equal(priorLooked, looked.GetValue(null));
            var phases = ReadPhases(ledger);
            if (phases.Length > 0) Assert.True(await ChildExited(phases[0].GetProperty("PID").GetInt32()));
            string parent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) + Path.DirectorySeparatorChar;
            Assert.StartsWith(parent, Path.GetFullPath(root));
            Directory.Delete(root, recursive: true); Assert.False(Directory.Exists(root));
        }
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static JsonElement[] ReadPhases(string path)
    {
        if (!File.Exists(path)) return [];
        try { return File.ReadAllLines(path).Where(x => x.Length > 0).Select(x => JsonSerializer.Deserialize<JsonElement>(x)).ToArray(); }
        catch (IOException) { return []; }
        catch (JsonException) { return []; }
    }
    private static async Task<bool> ChildExited(int pid)
    {
        try { using var child = Process.GetProcessById(pid); await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(6)); return child.HasExited; }
        catch (ArgumentException) { return true; }
        catch (TimeoutException) { return false; }
    }

    private const string Child = """
import hashlib,json,os,sys,threading,time
home=next(v[len('--homedir='):] for v in sys.argv[1:] if v.startswith('--homedir='))
ledger=os.path.join(home,'child.jsonl')
def note(phase,**extra):
    with open(ledger,'a',encoding='utf-8') as f:
        f.write(json.dumps(dict(Phase=phase,PID=os.getpid(),Arguments=sys.argv[1:],**extra))+'\n')
def watchdog():
    time.sleep(8);note('owned-watchdog');os._exit(90)
threading.Thread(target=watchdog,daemon=True).start()
note('started')
signature=open(sys.argv[-2],'rb').read();signed=open(sys.argv[-1],'rb').read()
note('inputs-read',SignatureSHA256=hashlib.sha256(signature).hexdigest().upper(),SignedSHA256=hashlib.sha256(signed).hexdigest().upper())
mode=open(os.path.join(home,'mode.txt'),encoding='utf-8').read()
stdout=b'[GNUPG:] GOODSIG 1234567890ABCDEF Owned Synthetic Signer\n[GNUPG:] TRUST_FULLY\n'
stderr=b'' if mode=='precancel' else b'P'*(128*1024)
sys.stdout.buffer.write(stdout);sys.stdout.buffer.flush()
sys.stderr.buffer.write(stderr);sys.stderr.buffer.flush()
note('streams-written',ActualOutputBytes=len(stdout),ActualErrorBytes=len(stderr),ActualOutputSHA256=hashlib.sha256(stdout).hexdigest().upper(),ActualErrorSHA256=hashlib.sha256(stderr).hexdigest().upper())
time.sleep(0.08 if mode=='healthy' else 1.2 if mode=='precancel' else 4)
note('natural-exit')
""";
}
