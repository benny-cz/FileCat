using System.Security.Cryptography;
using System.Text.Json;
using FileCat.Core.Content;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

public sealed class PagedReaderRetirementFailureTests(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _output = output;

    public static IEnumerable<object[]> Cases()
    {
        foreach (string api in new[] { "borrow", "refresh" })
        foreach (bool pendingClose in new[] { false, true })
        foreach (string primary in new[] { "none", "io", "denied", "damaged", "cancelled" })
        foreach (string secondary in new[] { "none", "io", "denied", "disposed" })
            yield return [api, pendingClose, primary, secondary];
    }

    [Theory]
        [MemberData(nameof(Cases))]
        public void A_retired_source_keeps_the_original_active_error_and_releases_its_resources(string api, bool pendingClose, string primary, string secondary)
    {
        string folder = Path.Combine(Path.GetTempPath(), "filecat-retirement-failure-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "owned.bin");
        byte[] input = Enumerable.Range(0, 4096).Select(n => (byte)((n * 31 + 5) & 255)).ToArray();
        File.WriteAllBytes(path, input);
        string originalHash = Convert.ToHexString(SHA256.HashData(input));
        Exception? original = Failure(primary, "owned original active " + api + " failure");
        Exception? closeFailure = Failure(secondary, "owned secondary source close failure");
        var source = new OwnedSource(path, original, closeFailure);
        var budget = new PageCacheBudget(PagedReader.PageSize);
        var reader = new PagedReader(source, budget: budget);
        try
        {
            byte[] prime = new byte[16];
            int primed = reader.Read(0, prime);
            long beforeBytes = budget.UsedBytes;
            int beforeReaders = budget.Readers;
            source.Armed = true;
            var work = Task.Run(() =>
            {
                try
                {
                    if (api == "borrow") reader.WithSource(s => s.Read(0, new byte[16]));
                    else reader.Refresh();
                    return (Exception?)null;
                }
                catch (Exception error) { return error; }
            });
            if (!source.Entered.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("Owned source did not reach its fence.");
            Exception? disposeAtFence = null, laterClose = null;
            bool? openAtFence = null;
            int? closesAtFence = null;
            long? bytesAtFence = null;
            int? readersAtFence = null;
            if (pendingClose)
            {
                try { reader.Dispose(); } catch (Exception error) { disposeAtFence = error; }
                openAtFence = !source.HandleClosed;
                closesAtFence = source.ProductCloses;
                bytesAtFence = budget.UsedBytes;
                readersAtFence = budget.Readers;
            }
            source.Release.Set();
            Exception? observed = work.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            if (!pendingClose)
            {
                try { reader.Dispose(); } catch (Exception error) { laterClose = error; }
            }
            Exception? repeated = null;
            try { reader.Dispose(); } catch (Exception error) { repeated = error; }
            string finalHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
            Exception? expectedWork = original ?? (pendingClose ? closeFailure : null);
            bool workRetained = ReferenceEquals(expectedWork, observed);
            bool closeCorrect = pendingClose ? disposeAtFence is null : ReferenceEquals(laterClose, closeFailure);
            bool ownedBytes = primed == 16 && prime.SequenceEqual(input[..16]) && source.ActualPrefix.SequenceEqual(input[..16]);
            bool retireCorrect = source.HandleClosed && source.ProductCloses == 1 && budget.UsedBytes == 0 && budget.Readers == 0 && repeated is null;
            bool fenceCorrect = !pendingClose || openAtFence == true && closesAtFence == 0 && bytesAtFence == 0 && readersAtFence == 0;
            bool ok = workRetained && closeCorrect && ownedBytes && retireCorrect && fenceCorrect && originalHash == finalHash;
            _output.WriteLine(JsonSerializer.Serialize(new
            {
                Api = api, PendingClose = pendingClose, Primary = primary, Secondary = secondary,
                Passed = ok, ActualOriginalWorkErrorRetained = workRetained,
                OriginalType = original?.GetType().Name, OriginalMessage = original?.Message, OriginalStack = original?.StackTrace,
                ObservedType = observed?.GetType().Name, ObservedMessage = observed?.Message,
                SecondaryType = closeFailure?.GetType().Name, ActualSecondaryCloseObjectRetained = ReferenceEquals(laterClose, closeFailure),
                DisposeAtFenceType = disposeAtFence?.GetType().Name, LaterCloseType = laterClose?.GetType().Name,
                HandleOpenAtFence = openAtFence, ProductClosesAtFence = closesAtFence, BudgetBytesAtFence = bytesAtFence, BudgetReadersAtFence = readersAtFence,
                ActualProductSourceHandleClosed = source.HandleClosed, ActualProductSourceCloseCount = source.ProductCloses,
                BudgetBytesBefore = beforeBytes, BudgetReadersBefore = beforeReaders, BudgetBytesAfter = budget.UsedBytes, BudgetReadersAfter = budget.Readers,
                RepeatedDisposeHasNoError = repeated is null, ActualPrefixHex = Convert.ToHexString(source.ActualPrefix), PrimeHex = Convert.ToHexString(prime),
                InputOriginalSHA256 = originalHash, InputFinalSHA256 = finalHash, NativeFilePath = path,
                NativeProtocolHardwareOrArchiveFaultIncidenceNotClaimed = true,
            }));
            Assert.True(ok, "The actual original work error, close ordering, source handle, budget and owned bytes must all be retained.");
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
            if (Armed) Active();
            return count;
        }
        public ContentRevision? GetRevision()
        {
            if (Armed) Active();
            return new(file.Length, File.GetLastWriteTimeUtc(path).Ticks);
        }
        private void Active()
        {
            ActualPrefix = new byte[16];
            int count = RandomAccess.Read(file.SafeFileHandle, ActualPrefix, 0);
            if (count != 16) throw new IOException("Owned control file did not return its prefix.");
            Entered.Set();
            if (!Release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("Owned release fence expired.");
            if (failure is not null) throw failure;
        }
        public void Dispose()
        {
            ProductCloses++;
            file.Dispose();
            if (closeFailure is not null) throw closeFailure;
        }
        public void EmergencyFixtureClose()
        {
            file.Dispose();
            Entered.Dispose();
            Release.Dispose();
        }
    }
}
