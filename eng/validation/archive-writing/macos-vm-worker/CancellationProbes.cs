using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using FileCat.Core.Archives;

internal static class CancellationProbes
{
    internal static async Task<int> Run(string native, string kernel, string initrd, string results)
    {
        Directory.CreateDirectory(results);
        var rows = new List<object>();
        try
        {
            // Exercise the actual session callback and disposal/reaping path while its source IO is outstanding.
            using var cancel = new CancellationTokenSource();
            using var requested = new ManualResetEventSlim();
            byte[] source = new byte[1024];
            VmProcess? worker = null;
            var assessment = new ArchiveAssessmentClient((nonce, cpu) => worker = new VmProcess(native, kernel, initrd, nonce, 1, Path.Combine(results, "core-assessment.log")))
                .AssessAsync(new(new(ArchiveWriteFormat.Tar), source.Length, Convert.ToHexString(SHA256.HashData(source))),
                    () => new GateReadStream(source, cancel.Token, requested), cancel.Token);
            if (!await Task.Run(() => requested.Wait(TimeSpan.FromSeconds(15)))) throw new IOException("The actual source request did not arrive.");
            var elapsed = Stopwatch.StartNew(); cancel.Cancel();
            bool canceled = false;
            try { await assessment.WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested) { canceled = true; }
            if (!canceled || worker is null || !worker.Reaped || worker.ExitCode != 0) throw new IOException("The actual session did not cancel and reap cooperatively.");
            Check(Path.Combine(results, "core-assessment.log"), forced: false, canceledMarker: true);
            rows.Add(new { kind = "actual-assessment-session", worker.Id, worker.Reaped, worker.ExitCode, seconds = elapsed.Elapsed.TotalSeconds });
            foreach (var probe in new[] { (Role: (byte)1, Kind: "blocked-input", Marker: ""), (Role: (byte)5, Kind: "blocked-output", Marker: "FCVMFLOD"), (Role: (byte)4, Kind: "uncooperative-spin", Marker: "FCVMBUSY") })
            {
                string log = Path.Combine(results, probe.Kind + ".log");
                await using var process = new VmProcess(native, kernel, initrd, Guid.NewGuid(), probe.Role, log);
                if (probe.Role == 1)
                {
                    byte[] hello = new byte[36]; await process.Output.ReadExactlyAsync(hello).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
                    int size = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(hello.AsSpan(28));
                    if (!hello.AsSpan(0, 4).SequenceEqual("FCAW"u8) || size is <= 0 or > 4096) throw new IOException("No worker hello.");
                    await process.Output.ReadExactlyAsync(new byte[size]).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
                }
                else
                {
                    byte[] marker = new byte[8]; await process.Output.ReadExactlyAsync(marker).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
                    if (System.Text.Encoding.ASCII.GetString(marker) != probe.Marker) throw new IOException("No controlled workload marker.");
                }
                await Task.Delay(200); // In the output case, deliberately stop draining its serial/protocol pipe.
                elapsed.Restart(); process.RequestCancellation(); process.RequestCancellation();
                while (!process.HasExited && elapsed.Elapsed < TimeSpan.FromSeconds(2)) await Task.Delay(5);
                if (!process.HasExited) process.Kill();
                await process.DisposeAsync();
                if (process.ExitCode != 0 || elapsed.Elapsed > TimeSpan.FromSeconds(3)) throw new IOException("Cancellation did not stop/reap the helper within its measured bound.");
                // A full output path may need the plan's bounded hard-stop fallback. Its console can also
                // delay the cancellation diagnostic; verify either complete permitted outcome, never a hang.
                Check(log, forced: probe.Role == 5 ? null : probe.Role == 4, canceledMarker: probe.Role == 5 ? null : probe.Role != 4);
                rows.Add(new { kind = probe.Kind, process.Id, process.Reaped, process.ExitCode, seconds = elapsed.Elapsed.TotalSeconds });
            }
            File.WriteAllText(Path.Combine(results, "facts.json"), JsonSerializer.Serialize(new { experimentPassed = true, productionBackendQualified = false, cases = rows }, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }
        catch (Exception ex) { File.WriteAllText(Path.Combine(results, "failure.txt"), ex.ToString()); return 1; }
    }

    private static void Check(string log, bool? forced, bool? canceledMarker)
    {
        string text = File.ReadAllText(log);
        string[] lines = text.Split('\n').Where(line => line.StartsWith("FILECAT_HOST_FACTS ", StringComparison.Ordinal)).ToArray();
        if (lines.Length != 1) throw new IOException("Missing native stop facts.");
        using var facts = JsonDocument.Parse(lines[0]["FILECAT_HOST_FACTS ".Length..]);
        var f = facts.RootElement;
        bool actualForced = f.GetProperty("forcedStop").GetBoolean();
        if (!f.GetProperty("cancellationRequested").GetBoolean() || !f.GetProperty("cancellationSent").GetBoolean() || f.GetProperty("cancellationMalformed").GetBoolean() ||
            f.GetProperty("parentExited").GetBoolean() || f.GetProperty("parentMonitorFailed").GetBoolean() || f.GetProperty("stopReason").GetString() != "cancel" ||
            f.GetProperty("state").GetInt32() != 0 || forced.HasValue && actualForced != forced.Value || f.GetProperty("stopError").GetString() != "" ||
            canceledMarker.HasValue && text.Contains("FILECAT_GUEST_CANCELED", StringComparison.Ordinal) != canceledMarker.Value) throw new IOException("Cancellation outcome differs from the native/guest facts.");
        if (actualForced && (!f.GetProperty("stopRequested").GetBoolean() || !f.GetProperty("stopCompleted").GetBoolean()) ||
            !actualForced && (!f.GetProperty("guestStopped").GetBoolean() || !text.Contains("FILECAT_WAIT_STATUS=0000000000000000", StringComparison.Ordinal)))
            throw new IOException("Cancellation did not complete the required VM/guest stop.");
    }

    private sealed class GateReadStream(byte[] source, CancellationToken ct, ManualResetEventSlim requested) : MemoryStream(source, writable: false)
    {
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer)
        {
            requested.Set();
            if (!ct.WaitHandle.WaitOne(TimeSpan.FromSeconds(10))) throw new IOException("The source-request gate timed out.");
            ct.ThrowIfCancellationRequested(); throw new IOException("The gate returned without cancellation.");
        }
    }
}
