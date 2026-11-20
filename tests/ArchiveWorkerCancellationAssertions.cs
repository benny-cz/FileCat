using System.Diagnostics;
using FileCat.Core.Archives;

/// <summary>Same native cancellation acceptance on each launch boundary; no fallback factory is supplied here.</summary>
internal static class ArchiveWorkerCancellationAssertions
{
    internal static async Task Run(Func<Guid, TimeSpan, IArchiveWorkerProcess> launch, string role)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var queue = new ArchiveWorkerQueue(); var elapsed = new Stopwatch(); IArchiveWorkerProcess? actual = null;
        bool ignore = role == "archive-cancel-ignore", flood = role == "archive-cancel-output";
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ArchiveWorkerSession.RunAsync(
            (nonce, cpu) => actual = launch(nonce, cpu), queue, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(1),
            (nonce, process, _) =>
            {
                if (ignore || flood)
                {
                    byte[] marker = System.Text.Encoding.ASCII.GetBytes(ignore ? "CANCELLATION-IGNORE-READY\n" : "CANCELLATION-OUTPUT-READY\n");
                    byte[] received = new byte[marker.Length]; process.Output.ReadExactly(received); Assert.Equal(marker, received);
                }
                else Assert.Equal(ArchiveWorkerMessage.Hello, new ArchiveWorkerProtocol(nonce, 1 << 20).Read(process.Output).Kind);
                elapsed.Start(); cancellation.Cancel();
                while (!process.HasExited && elapsed.Elapsed < TimeSpan.FromSeconds(5)) Thread.Sleep(5);
                Assert.True(process.HasExited, "The worker could not exit without the parent draining its output pipe.");
                // No protocol request, input grant or stage authority was issued. A saturated output can contain
                // the one already in-flight bounded write; cancellation must still let the parser/process exit.
                byte[] drain = new byte[65_524]; int count, total = 0;
                while ((count = process.Output.Read(drain)) > 0)
                { total += count; Assert.True(flood && total <= 16 << 20, "Unexpected output after cancellation."); }
                return 7;
            }, cancellation.Token).WaitAsync(TimeSpan.FromSeconds(25), TestContext.Current.CancellationToken));
        Assert.NotNull(actual); Assert.True(actual.HasExited);
        if (ignore)
        {
            Assert.NotEqual(0u, actual.ExitCode);
            Assert.InRange(elapsed.Elapsed, TimeSpan.FromSeconds(1.9), TimeSpan.FromSeconds(5));
        }
        else
        {
            Assert.Equal(0u, actual.ExitCode);
            Assert.InRange(elapsed.Elapsed, TimeSpan.Zero, TimeSpan.FromSeconds(3));
        }
        using var reservation = await queue.EnterAsync(TestContext.Current.CancellationToken);
        TestContext.Current.TestOutputHelper?.WriteLine($"{role} native cancellation through reap: {elapsed.Elapsed.TotalMilliseconds:F1} ms; exit {actual.ExitCode}.");
    }
}
