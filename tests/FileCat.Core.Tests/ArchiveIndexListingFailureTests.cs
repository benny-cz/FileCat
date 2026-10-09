using System.Formats.Tar;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text.Json;
using FileCat.Archives;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

public sealed class ArchiveIndexListingFailureTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (string primary in new[] { "io", "denied", "invalid-data", "unexpected", "cancelled" })
        foreach (bool late in new[] { false, true })
        foreach (string secondary in new[] { "none", "io", "denied", "disposed" })
            yield return [primary, late, secondary];
        foreach (string secondary in new[] { "none", "io", "denied", "disposed" })
            yield return ["none", false, secondary];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void A_failed_listing_keeps_its_first_error_after_retiring_the_actual_source(string primary, bool late, string secondary)
    {
        string root = Directory.CreateTempSubdirectory("filecat-index-listing-").FullName;
        string path = Path.Combine(root, "owned.tar");
        byte[] expected = Enumerable.Range(0, 4096).Select(n => (byte)(n * 29 + 7)).ToArray();
        IMemberReader? inner = null; ListingReader? wrapper = null; object? index = null;
        try
        {
            using (var file = File.Create(path))
            using (var writer = new TarWriter(file, TarEntryFormat.Ustar, leaveOpen: true))
            using (var bytes = new MemoryStream(expected, writable: false))
                writer.WriteEntry(new UstarTarEntry(TarEntryType.RegularFile, "owned-member.bin") { DataStream = bytes, ModificationTime = DateTimeOffset.UnixEpoch });
            string beforeHash = Hash(File.ReadAllBytes(path));
            inner = ArchiveFormats.Open(path, ArchiveKind.Tar, "owned.tar");
            var firstListing = inner.List(_ => { }, CancellationToken.None).ToArray();
            using var positive = new MemoryStream();
            using (var member = inner.Open(0, CancellationToken.None)) member.CopyTo(positive);
            Assert.Single(firstListing); Assert.Equal(expected, positive.ToArray());
            var source = (FileStream)inner.GetType().GetField("_plain", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(inner)!;
            bool initiallyOpen = !source.SafeFileHandle.IsClosed && source.CanRead;
            bool? exclusiveBefore = OperatingSystem.IsWindows() ? Exclusive(path) : null;
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            Exception? first = Failure(primary, cancelled.Token), cleanup = Failure(secondary, CancellationToken.None);
            wrapper = new ListingReader(inner, first, cleanup, late);
            Type type = typeof(ArchiveFormats).Assembly.GetType("FileCat.Archives.ArchiveIndex", throwOnError: true)!;
            Exception? error = null, closeError = null;
            try { index = Invoke(type.GetMethod("Build", BindingFlags.Public | BindingFlags.Static)!, null, wrapper, new FileInfo(path).Length, CancellationToken.None); }
            catch (Exception failure) { error = failure; }
            bool shouldRetainIndex = primary == "none" || late && primary != "cancelled";
            int closesAtBuild = wrapper.CloseCalls;
            bool sourceClosedAtBuild = source.SafeFileHandle.IsClosed;
            bool originalRetained = first is null ? error is null : primary == "unexpected" && !late
                ? error is InvalidDataException && ReferenceEquals(error.InnerException, first) : ReferenceEquals(error, first);
            string[] warnings = index is null ? [] : ((List<string>)type.GetProperty("Warnings")!.GetValue(index)!).ToArray();
            byte[]? retainedBytes = null;
            if (index is not null)
            {
                var tag = new ArchiveMemberTag("owned-member.bin", 0, 4096, -1, false, 0, null, MemberKind.File, null);
                using var content = (IContentSource)Invoke(type.GetMethod("Extract")!, index, tag, root)!;
                retainedBytes = new byte[4096]; int offset = 0;
                while (offset < retainedBytes.Length) { int count = content.Read(offset, retainedBytes.AsSpan(offset)); if (count <= 0) break; offset += count; }
                Assert.Equal(4096, offset);
                try { ((IDisposable)index).Dispose(); }
                catch (Exception failure) { closeError = failure; }
            }
            bool allClosed = source.SafeFileHandle.IsClosed && !source.CanRead;
            bool? exclusiveAfter = OperatingSystem.IsWindows() ? Exclusive(path) : null;
            bool unchanged = beforeHash == Hash(File.ReadAllBytes(path));
            output.WriteLine(JsonSerializer.Serialize(new
            {
                Primary = primary, Late = late, Secondary = secondary, ExpectedIndexRetained = shouldRetainIndex,
                ActualIndexRetained = index is not null, ActualOriginalListingErrorObjectOrInnerRetained = originalRetained,
                ActualListingErrorType = error?.GetType().FullName, ActualListingErrorMessage = error?.Message, ActualListingErrorStack = error?.StackTrace,
                ActualInnerErrorType = error?.InnerException?.GetType().FullName, ActualCancellationTokenRetained = error is OperationCanceledException oce && oce.CancellationToken == cancelled.Token,
                ActualClosesAtBuild = closesAtBuild, ActualSourceClosedAtBuild = sourceClosedAtBuild,
                ActualWarnings = warnings, ActualRetainedBytes = retainedBytes?.Length, ActualRetainedSHA256 = retainedBytes is null ? null : Hash(retainedBytes),
                ActualInitialMemberBytes = positive.Length, ActualInitialMemberSHA256 = Hash(positive.ToArray()), ActualExpectedMemberSHA256 = Hash(expected),
                ActualSourceInitiallyOpen = initiallyOpen, ActualAllSourceHandlesClosedBeforeFixtureCleanup = allClosed, ActualCloseCalls = wrapper.CloseCalls,
                ActualLaterCleanupErrorType = closeError?.GetType().FullName, ActualLaterCleanupErrorObjectRetained = ReferenceEquals(closeError, cleanup),
                ActualWindowsExclusiveBefore = exclusiveBefore, ActualWindowsExclusiveAfter = exclusiveAfter,
                ActualInputArchiveSHA256 = beforeHash, ActualFullInputUnchanged = unchanged, ActualOwnedFixtureRoot = root, ActualOwnedArchivePath = path,
                ActualTarConstructorListingAndCompleteDecoderPositive = true, ControlledListingAndPostActualCloseErrorsOnly = true, NoNativeLibraryFailureOrPhysicalSourceClaim = true
            }));
            Assert.True(initiallyOpen && allClosed && unchanged); Assert.Equal(1, wrapper.CloseCalls);
            Assert.Equal(shouldRetainIndex, index is not null);
            if (shouldRetainIndex)
            {
                Assert.Null(error); Assert.Equal(0, closesAtBuild); Assert.False(sourceClosedAtBuild); Assert.Equal(expected, retainedBytes);
                Assert.Same(cleanup, closeError);
                if (first is null) Assert.Empty(warnings); else Assert.Contains(warnings, w => w.Contains(first.Message, StringComparison.Ordinal));
            }
            else
            {
                Assert.True(originalRetained); Assert.Equal(1, closesAtBuild); Assert.True(sourceClosedAtBuild);
                if (primary == "cancelled") Assert.Equal(cancelled.Token, Assert.IsType<OperationCanceledException>(error).CancellationToken);
            }
            if (OperatingSystem.IsWindows()) { Assert.False(exclusiveBefore); Assert.True(exclusiveAfter); }
        }
        finally
        {
            if (index is IDisposable retained) try { retained.Dispose(); } catch { }
            if (wrapper is not null) try { wrapper.Dispose(); } catch { }
            if (inner is not null) try { inner.Dispose(); } catch { }
            Directory.Delete(root, recursive: true);
        }
    }

    private static Exception? Failure(string kind, CancellationToken token) => kind switch
    {
        "none" => null, "io" => new IOException("owned listing boundary failure"), "denied" => new UnauthorizedAccessException("owned listing boundary failure"),
        "invalid-data" => new InvalidDataException("owned listing boundary failure"), "unexpected" => new InvalidOperationException("owned listing boundary failure"),
        "cancelled" => new OperationCanceledException("owned listing boundary failure", token), "disposed" => new ObjectDisposedException("owned listing boundary failure"),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
    private static string Hash(byte[] value) => Convert.ToHexStringLower(SHA256.HashData(value));
    private static object? Invoke(MethodInfo method, object? target, params object?[] args)
    {
        try { return method.Invoke(target, args); }
        catch (TargetInvocationException error) when (error.InnerException is not null) { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
    }
    private static bool Exclusive(string path)
    {
        try { using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None); return true; }
        catch (IOException) { return false; }
    }
    private sealed class ListingReader(IMemberReader inner, Exception? first, Exception? cleanup, bool late) : IMemberReader
    {
        private bool _closed;
        public int CloseCalls { get; private set; }
        public string Format => inner.Format;
        public bool SharesCursor => inner.SharesCursor;
        public Stream Open(int index, CancellationToken ct) => inner.Open(index, ct);
        public IEnumerable<FileCat.Archives.MemberInfo> List(Action<string> warn, CancellationToken ct)
        {
            if (first is null) { foreach (var member in inner.List(warn, ct)) yield return member; yield break; }
            if (late) { using var cursor = inner.List(warn, ct).GetEnumerator(); if (!cursor.MoveNext()) throw new InvalidOperationException("Missing actual positive member"); yield return cursor.Current; }
            throw first;
        }
        public void Dispose()
        {
            if (_closed) return; _closed = true; CloseCalls++; inner.Dispose(); if (cleanup is not null) throw cleanup;
        }
    }
}
