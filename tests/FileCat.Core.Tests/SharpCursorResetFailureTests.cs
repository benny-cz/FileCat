using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text.Json;
using SharpCompress.Archives;
using SharpCompress.Readers;

namespace FileCat.Core.Tests;

public sealed class SharpCursorResetFailureTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (bool factoryFault in new[] { false, true })
        foreach (int fault in Enumerable.Range(0, 4))
        foreach (bool sameMember in new[] { false, true })
            yield return [factoryFault, fault, sameMember];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void A_failed_cursor_reset_does_not_reuse_or_close_the_retired_cursor_again(bool factoryFault, int fault, bool sameMember)
    {
        string root = Path.Combine(Path.GetTempPath(), "filecat-sharp-reset-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "owned.7z");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "TestData", "Archives", "7Zip.solid.7z"), path);
        string originalHash = Hash(File.ReadAllBytes(path));
        var type = Assembly.Load("FileCat.Archives").GetTypes().Single(t => t.Name == "SharpArchiveReader");
        object? target = null;
        IReader? originalCursor = null;
        IArchive? originalArchive = null;
        bool actualCursorClosed = false;
        bool actualArchiveClosed = false;
        try
        {
            target = Call(type, null, "Open", path, false)!;
            originalArchive = (IArchive)Field(type, target, "_archive");
            var entries = (List<IArchiveEntry>)Field(type, target, "_entries");
            var volumes = (List<FileStream>)Field(type, target, "_volumes");
            var expected = new Dictionary<string, (int Bytes, string SHA256)>(StringComparer.Ordinal)
            {
                ["exe/test.exe"] = (45056, "8557928804f57ecc340b3bb38b095a3607474ec8deb0076f316fcfe02b562106"),
                ["jpg/test.jpg"] = (40372, "b251c7501fb0f55dd4a92feabe0a6f5733bc40a02679498155fae9b30138fc53"),
                ["тест.txt"] = (15498, "4d581d93d369f6e1c9b295ff38d82dabd577f927dfaf0c35818c015c85e322d9"),
            };
            // Discover the actual solid-reader order; entry-list ordering is not an oracle for it.
            string firstKey;
            using (var order = originalArchive.ExtractAllEntries())
            {
                while (order.MoveToNextEntry() && (order.Entry.IsDirectory || order.Entry.Size == 0)) { }
                firstKey = order.Entry.Key ?? throw new InvalidDataException("No owned member key.");
            }
            int firstIndex = entries.FindIndex(e => e.Key == firstKey);
            Assert.True(firstIndex >= 0);
            Assert.True(expected.ContainsKey(firstKey));
            byte[] positive = Read(type, target, firstIndex);
            bool positiveExact = positive.Length == expected[firstKey].Bytes && Hash(positive) == expected[firstKey].SHA256;
            Assert.True(positiveExact);
            originalCursor = (IReader)Field(type, target, "_reader");
            var seen = (Dictionary<string, int>)Field(type, target, "_seen");
            int nextIndex = entries.FindIndex(e => e.Key is not null && e.Key != firstKey && expected.ContainsKey(e.Key) && seen.GetValueOrDefault(e.Key) == 0);
            Assert.True(nextIndex >= 0, "A different, not-yet-visited actual member is required.");
            int retryIndex = sameMember ? firstIndex : nextIndex;
            string retryKey = entries[retryIndex].Key!;
            Exception? injected = Fault(fault);
            int oldCloseCalls = 0, factoryCalls = 0, archiveCloseCalls = 0, retiredUseCalls = 0;
            IReader ownedOld = DispatchProxy.Create<IReader, OwnedCursorResetProxy>();
            ((OwnedCursorResetProxy)(object)ownedOld).Handler = (method, args) =>
            {
                if (method.Name == nameof(IDisposable.Dispose))
                {
                    oldCloseCalls++;
                    if (!actualCursorClosed) { originalCursor.Dispose(); actualCursorClosed = true; }
                    if (!factoryFault && injected is not null) throw injected;
                    return null;
                }
                if (actualCursorClosed)
                {
                    retiredUseCalls++;
                    throw new ObjectDisposedException("owned retired cursor guard");
                }
                return Forward(method, originalCursor, args);
            };
            IArchive ownedArchive = DispatchProxy.Create<IArchive, OwnedCursorResetProxy>();
            ((OwnedCursorResetProxy)(object)ownedArchive).Handler = (method, args) =>
            {
                if (method.Name == nameof(IArchive.ExtractAllEntries))
                {
                    factoryCalls++;
                    if (factoryFault && factoryCalls == 1 && injected is not null) throw injected;
                }
                if (method.Name == nameof(IDisposable.Dispose))
                {
                    archiveCloseCalls++;
                    actualArchiveClosed = true;
                }
                return Forward(method, originalArchive, args);
            };
            type.GetField("_reader", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(target, ownedOld);
            type.GetField("_archive", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(target, ownedArchive);
            bool handlesOpenBefore = volumes.All(v => !v.SafeFileHandle.IsClosed && v.CanRead);
            bool? exclusiveBefore = OperatingSystem.IsWindows() ? Exclusive(path) : null;
            Exception? firstError = null, retryError = null, cleanupError = null;
            byte[]? first = null, retry = null;
            try { first = Read(type, target, firstIndex); }
            catch (Exception ex) { firstError = ex; }
            int oldClosesAfterFirst = oldCloseCalls, factoriesAfterFirst = factoryCalls;
            bool retiredAtFirstBoundary = type.GetField("_reader", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(target) is null;
            bool firstErrorRetained = injected is null ? firstError is null : ReferenceEquals(injected, firstError is InvalidDataException ? firstError.InnerException : firstError);
            try { retry = Read(type, target, retryIndex); }
            catch (Exception ex) { retryError = ex; }
            int oldClosesAfterRetry = oldCloseCalls;
            bool retryExact = retry is not null && retry.Length == expected[retryKey].Bytes && Hash(retry) == expected[retryKey].SHA256;
            bool ownedSourceStillOpen = volumes.All(v => !v.SafeFileHandle.IsClosed && v.CanRead);
            try { ((IDisposable)target).Dispose(); }
            catch (Exception ex) { cleanupError = ex; }
            bool allClosed = volumes.All(v => v.SafeFileHandle.IsClosed && !v.CanRead);
            bool? exclusiveAfter = OperatingSystem.IsWindows() ? Exclusive(path) : null;
            bool unchanged = Hash(File.ReadAllBytes(path)) == originalHash;
            TestContext.Current.TestOutputHelper?.WriteLine(JsonSerializer.Serialize(new
            {
                FactoryFault = factoryFault, FaultKind = fault, SameMemberRetry = sameMember,
                ActualFirstMember = firstKey, ActualRetryMember = retryKey,
                ActualInitialPositiveBytes = positive.Length, ActualInitialPositiveSHA256 = Hash(positive), ActualInitialPositiveMatchesIndependentCorpus = positiveExact,
                ActualFirstErrorType = firstError?.GetType().Name, ActualFirstErrorMessage = firstError?.Message, ActualFirstErrorStack = firstError?.StackTrace,
                ActualFirstErrorObjectOrInnerRetained = firstErrorRetained, ActualFirstHealthyReadSHA256 = first is null ? null : Hash(first),
                ActualOldCursorClosesAfterFirst = oldClosesAfterFirst, ActualFactoryCallsAfterFirst = factoriesAfterFirst,
                ActualRetiredCursorFieldNullAtFailureBoundary = injected is null ? (bool?)null : retiredAtFirstBoundary,
                ActualOldCursorClosesAfterRetry = oldClosesAfterRetry, ActualRetiredCursorUseCalls = retiredUseCalls, ActualFactoryCalls = factoryCalls,
                ActualRetryErrorType = retryError?.GetType().Name, ActualRetryErrorMessage = retryError?.Message, ActualRetryErrorStack = retryError?.StackTrace,
                ActualRetryBytes = retry?.Length, ActualRetrySHA256 = retry is null ? null : Hash(retry), ExpectedRetryBytes = expected[retryKey].Bytes, ExpectedRetrySHA256 = expected[retryKey].SHA256,
                ActualRetryAllBytesExact = retryExact, ActualNativeSourceInitiallyOpen = handlesOpenBefore, ActualNativeSourceStillOpenBeforeFullDispose = ownedSourceStillOpen,
                ActualAllNativeHandlesClosedAfterFullDispose = allClosed, ActualArchiveDisposeCalls = archiveCloseCalls,
                ActualCleanupErrorType = cleanupError?.GetType().Name, ActualWindowsExclusiveBefore = exclusiveBefore, ActualWindowsExclusiveAfter = exclusiveAfter,
                ActualOriginalArchiveSHA256 = originalHash, ActualFullArchiveBytesUnchanged = unchanged, ActualOwnedFixtureRoot = root, ActualOwnedArchivePath = path,
                ActualProductConstructorAndSolidMemberDecoderUsed = true, ControlledLibraryCloseOrOneFactoryErrorAndRetiredCursorGuardOnly = true,
                NoNativeLibraryFailureOrPhysicalSourceIncidenceClaim = true,
            }));
            Assert.True(handlesOpenBefore && ownedSourceStillOpen && allClosed && unchanged);
            Assert.Equal(1, archiveCloseCalls);
            Assert.True(firstErrorRetained);
            Assert.Equal(1, oldClosesAfterFirst);
            Assert.Equal(1, oldClosesAfterRetry);
            Assert.Equal(0, retiredUseCalls);
            if (injected is not null) Assert.True(retiredAtFirstBoundary);
            Assert.Null(retryError);
            Assert.True(retryExact);
            if (OperatingSystem.IsWindows()) { Assert.False(exclusiveBefore); Assert.True(exclusiveAfter); }
        }
        finally
        {
            // Fixture cleanup is after the recorded source/cursor boundary and cannot hide a failed retry.
            if (target is IDisposable disposable) { try { disposable.Dispose(); } catch { } }
            if (!actualCursorClosed) originalCursor?.Dispose();
            if (!actualArchiveClosed) originalArchive?.Dispose();
            Directory.Delete(root, true);
        }
    }

    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static object Field(Type type, object target, string name) => type.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(target)!;
    private static Exception? Fault(int kind) => kind switch
    {
        0 => null, 1 => new IOException("owned cursor reset failure"),
        2 => new UnauthorizedAccessException("owned cursor reset failure"),
        3 => new ObjectDisposedException("owned cursor reset failure"),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
    private static byte[] Read(Type type, object target, int index)
    {
        using var input = (Stream)Call(type, target, "Open", index, CancellationToken.None)!;
        using var output = new MemoryStream();
        input.CopyTo(output);
        return output.ToArray();
    }
    private static object? Call(Type type, object? target, string name, params object?[] args)
    {
        var method = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static).Single(m => m.Name == name && m.IsStatic == (target is null));
        return Forward(method, target, args);
    }
    private static object? Forward(MethodInfo method, object? target, object?[]? args)
    {
        try { return method.Invoke(target, args); }
        catch (TargetInvocationException ex) when (ex.InnerException is not null) { ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); throw; }
    }
    private static bool Exclusive(string path)
    {
        try { using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None); return true; }
        catch (IOException) { return false; }
    }
    public class OwnedCursorResetProxy : DispatchProxy
    {
        public Func<MethodInfo, object?[]?, object?> Handler { get; set; } = null!;
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Handler(targetMethod!, args);
    }
}
