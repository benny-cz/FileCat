using System.Security.Cryptography;
using System.Text.Json;
using FileCat.Core.Content;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

public sealed class PagedReaderLoadCancellationFailureTests(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _output = output;
    public static IEnumerable<object[]> Cases()
    {
        foreach (bool pendingClose in new[] { false, true })
        foreach (string primary in new[] { "none", "io", "denied", "damaged", "disposed", "cancelled" })
        foreach (string secondary in new[] { "none", "io", "denied", "disposed" })
            yield return [pendingClose, primary, secondary];
    }

[Theory]
    [MemberData(nameof(Cases))]
    public void Retiring_an_active_page_keeps_unhandled_cancellation_and_the_handled_read_policy(bool pendingClose, string primary, string secondary)
{
    string folder = Path.Combine(Path.GetTempPath(), "filecat-load-retirement-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(folder);
    string path = Path.Combine(folder, "owned.bin");
    byte[] input = Enumerable.Range(0, 4096).Select(n => (byte)((n * 31 + 5) & 255)).ToArray();
    File.WriteAllBytes(path, input);
    string originalHash = Convert.ToHexString(SHA256.HashData(input));
    Exception? original = Failure(primary, "owned original blocking page failure");
    Exception? closeFailure = Failure(secondary, "owned secondary source close failure");
    var source = new OwnedSource(path, original, closeFailure);
    var budget = new PageCacheBudget(PagedReader.PageSize);
    var reader = new PagedReader(source, budget: budget);
    try
    {
        source.Armed = true;
        byte[] destination = new byte[16];
        Task<(Exception? Error, int? Read)> work = Task.Run(() =>
        {
            try { return ((Exception?)null, (int?)reader.Read(0, destination)); }
            catch (Exception error) { return (error, (int?)null); }
        });
        if (!source.Entered.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("Owned source did not reach its fence.");
        Exception? atFence = null, laterClose = null;
        bool? openAtFence = null;
        int? closesAtFence = null;
        long? bytesAtFence = null;
        int? readersAtFence = null;
        if (pendingClose)
        {
            try { reader.Dispose(); } catch (Exception error) { atFence = error; }
            openAtFence = !source.HandleClosed;
            closesAtFence = source.ProductCloses;
            bytesAtFence = budget.UsedBytes;
            readersAtFence = budget.Readers;
        }
        source.Release.Set();
        var observed = work.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
        string? readError = reader.ReadError;
        long budgetAfterWork = budget.UsedBytes;
        if (!pendingClose)
        {
            try { reader.Dispose(); } catch (Exception error) { laterClose = error; }
        }
        Exception? repeated = null;
        try { reader.Dispose(); } catch (Exception error) { repeated = error; }
        string finalHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        bool unhandled = primary == "cancelled";
        bool readErrorWasHandled = primary is "io" or "denied" or "damaged" or "disposed";
        bool cancellationRetained = !unhandled || ReferenceEquals(original, observed.Error);
        Exception? expectedOther = pendingClose ? closeFailure : null;
        bool otherContract = unhandled || ReferenceEquals(expectedOther, observed.Error);
        bool readCountCorrect = observed.Error is not null || observed.Read == (primary == "none" ? 16 : 0);
        string? expectedVisibleReadError = readErrorWasHandled && primary != "disposed" && !pendingClose ? original!.Message : null;
        bool actualVisibleReadErrorCorrect = readError == expectedVisibleReadError;
        bool bytesExact = source.ActualPrefix.SequenceEqual(input[..16]) && originalHash == finalHash;
        bool resourcesExact = source.HandleClosed && source.ProductCloses == 1 && budget.UsedBytes == 0 && budget.Readers == 0 && repeated is null;
        bool closeOrderExact = pendingClose ? atFence is null && openAtFence == true && closesAtFence == 0 && bytesAtFence == 0 && readersAtFence == 0 : ReferenceEquals(laterClose, closeFailure);
        if (!otherContract || !readCountCorrect || !actualVisibleReadErrorCorrect || !bytesExact || !resourcesExact || !closeOrderExact)
            throw new InvalidOperationException("An independent healthy order/resource/handled-error control failed.");
        _output.WriteLine(JsonSerializer.Serialize(new
        {
            PendingClose = pendingClose, Primary = primary, Secondary = secondary,
            ControlPassed = cancellationRetained, UnhandledCancellation = unhandled,
            OriginalUnhandledCancellationRetained = cancellationRetained,
            OriginalType = original?.GetType().Name, OriginalMessage = original?.Message, OriginalStack = original?.StackTrace,
            ObservedType = observed.Error?.GetType().Name, ObservedMessage = observed.Error?.Message,
            SecondaryType = closeFailure?.GetType().Name,
            ActualHandledReadError = readErrorWasHandled, VisibleReadError = readError,
            VisibleReadErrorMatchesExistingPolicy = actualVisibleReadErrorCorrect,
            ActualReadCount = observed.Read, ActualDestinationHex = Convert.ToHexString(destination),
            BudgetBytesAfterWork = budgetAfterWork, BudgetBytesAfterClose = budget.UsedBytes, BudgetReadersAfterClose = budget.Readers,
            SourceHandleClosed = source.HandleClosed, ActualSourceCloseCount = source.ProductCloses,
            HandleOpenAtFence = openAtFence, ClosesAtFence = closesAtFence, BudgetBytesAtFence = bytesAtFence, BudgetReadersAtFence = readersAtFence,
            ActualLateCloseObjectRetained = ReferenceEquals(laterClose, closeFailure),
            RepeatedDisposeHasNoError = repeated is null,
            ActualPrefixHex = Convert.ToHexString(source.ActualPrefix),
            InputOriginalSHA256 = originalHash, InputFinalSHA256 = finalHash,
            NativeFilePath = path, OwnedFaultAdaptersOnly = true,
            NoHandledReadPolicyChanges = true,
        }));
        Assert.True(cancellationRetained, "Secondary source retirement must not replace original page cancellation.");
    }
    finally
    {
        source.Release.Set();
        try { reader.Dispose(); } catch { }
        source.EmergencyFixtureClose();
        Directory.Delete(folder, recursive: true);
    }
}

static Exception? Failure(string type, string message) => type switch
{
    "io" => new IOException(message), "denied" => new UnauthorizedAccessException(message),
    "damaged" => new InvalidDataException(message), "cancelled" => new OperationCanceledException(message),
    "disposed" => new ObjectDisposedException(message), _ => null,
};

sealed class OwnedSource : IContentSource
{
    private readonly FileStream file;
    private readonly string path;
    private readonly Exception? failure, closeFailure;
    public readonly ManualResetEventSlim Entered = new(), Release = new();
    public bool Armed;
    public byte[] ActualPrefix = [];
    public int ProductCloses;
    public bool HandleClosed => file.SafeFileHandle.IsClosed;
    public OwnedSource(string path, Exception? failure, Exception? closeFailure)
    {
        this.path = path; this.failure = failure; this.closeFailure = closeFailure;
        file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
    }
    public string DisplayName => path;
    public long Length => file.Length;
    public bool CanSeek => true;
    public string? LocalPath => path;
    public int Read(long offset, Span<byte> buffer)
    {
        int count = RandomAccess.Read(file.SafeFileHandle, buffer, offset);
        if (Armed)
        {
            ActualPrefix = new byte[16];
            if (RandomAccess.Read(file.SafeFileHandle, ActualPrefix, 0) != 16) throw new IOException("Owned prefix control failed.");
            Entered.Set();
            if (!Release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("Owned release fence expired.");
            if (failure is not null) throw failure;
        }
        return count;
    }
    public ContentRevision? GetRevision() => new(file.Length, File.GetLastWriteTimeUtc(path).Ticks);
    public void Dispose()
    {
        ProductCloses++;
        file.Dispose();
        if (closeFailure is not null) throw closeFailure;
    }
    public void EmergencyFixtureClose()
    {
        file.Dispose(); Entered.Dispose(); Release.Dispose();
    }
}

}
