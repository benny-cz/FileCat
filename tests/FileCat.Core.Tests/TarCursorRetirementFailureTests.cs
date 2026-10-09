using System.IO.Compression;
using System.Formats.Tar;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using FileCat.Archives;

namespace FileCat.Core.Tests;

public sealed class TarCursorRetirementFailureTests(ITestOutputHelper output)
{
    public static TheoryData<string, string, bool> Cases
    {
        get
        {
            var cases = new TheoryData<string, string, bool>();
            foreach (string fault in new[] { "none", "io", "denied", "disposed" })
            {
                foreach (bool beforeClose in new[] { false, true })
                {
                    cases.Add("dispose", fault, beforeClose);
                    cases.Add("reopen", fault, beforeClose);
                }
                foreach (bool priorMember in new[] { false, true }) cases.Add("factory", fault, priorMember);
            }
            return cases;
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void An_owned_tar_cursor_close_or_open_failure_still_retires_its_file(string action, string fault, bool variant)
    {
        string folder = OwnedFolder();
        string path = Path.Combine(folder, "owned-member.bin");
        byte[] bytes = Enumerable.Range(0, 4096).Select(n => (byte)((n * 29 + 7) & 255)).ToArray();
        WriteTar(path, bytes, compressed: false);
        string originalHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        Exception? failure = fault switch
        {
            "io" => new IOException("owned member lifecycle failure"),
            "denied" => new UnauthorizedAccessException("owned member lifecycle failure"),
            "disposed" => new ObjectDisposedException("owned member lifecycle failure"),
            _ => null,
        };
        var files = new List<FileStream>();
        var streams = new List<CloseFaultStream>();
        int opens = 0;
        int factoryAt = action == "factory" ? (variant ? 2 : 1) : -1;
        Func<Stream, Stream> factory = stream =>
        {
            files.Add((FileStream)stream);
            int call = ++opens;
            if (call == factoryAt && failure is not null) throw failure;
            var wrapped = new CloseFaultStream(stream, action == "factory" || call != 1 ? null : failure, variant);
            streams.Add(wrapped);
            return wrapped;
        };
        Type type = typeof(ArchiveFormats).Assembly.GetType("FileCat.Archives.TarMemberReader", throwOnError: true)!;
        var reader = (IMemberReader)Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null, new object[] { path, factory, "owned TAR control" }, null)!;
        byte[] first = [], recovered = [];
        Exception? observed = null, repeated = null;
        try
        {
            if (action != "factory" || variant) first = First(reader.Open(0, CancellationToken.None));
            observed = action == "dispose" ? Record.Exception(reader.Dispose) :
                Record.Exception(() => { using var next = reader.Open(0, CancellationToken.None); if (failure is null) recovered = First(next); });
            bool originalError = ReferenceEquals(observed, failure);
            bool handlesAtAction = files.All(f => f.SafeFileHandle.IsClosed);
            bool referencesRetired = new[] { "_cursorFile", "_cursorStream", "_cursorReader", "_cursorGuard", "_cursorEntry" }
                .All(name => type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(reader) is null);
            int opensAtAction = opens;
            int closesAtAction = streams.Sum(s => s.CloseCount);
            Exception? recoveryError = null, laterCloseError = null;
            if (action == "dispose") repeated = Record.Exception(reader.Dispose);
            else
            {
                if (failure is not null) recoveryError = Record.Exception(() => recovered = First(reader.Open(0, CancellationToken.None)));
                laterCloseError = Record.Exception(reader.Dispose);
                repeated = Record.Exception(reader.Dispose);
            }
            bool bytesCorrect = first.Length == 0 || first.SequenceEqual(bytes[..16]);
            bytesCorrect &= action == "dispose" || recovered.SequenceEqual(bytes[..16]);
            string finalHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
            bool faultRetired = failure is null || handlesAtAction && referencesRetired;
            output.WriteLine(JsonSerializer.Serialize(new
            {
                Action = action, Fault = fault, Variant = variant,
                OriginalFailureRetained = originalError, OriginalType = failure?.GetType().Name,
                ObservedType = observed?.GetType().Name, ObservedMessage = observed?.Message,
                ActualHandlesClosedAtAction = handlesAtAction, ActualReferencesRetiredAtAction = referencesRetired,
                OpensAtAction = opensAtAction, WrapperClosesAtAction = closesAtAction,
                AllActualFileHandlesClosed = files.All(f => f.SafeFileHandle.IsClosed),
                ActualWrapperCloseCount = streams.Sum(s => s.CloseCount),
                RepeatedDisposeHasNoError = repeated is null, RecoveryErrorType = recoveryError?.GetType().Name, LaterCloseErrorType = laterCloseError?.GetType().Name,
                FirstHex = Convert.ToHexString(first), RecoveredHex = Convert.ToHexString(recovered),
                InputOriginalSHA256 = originalHash, InputFinalSHA256 = finalHash,
                FixturePath = folder, NativeDecompressorFaultIncidenceNotClaimed = true, OwnedTarCursorLifecycleAdapter = true,
            }));
            Assert.True(originalError, "A later close replaced the original failure.");
            Assert.True(faultRetired, "The failed reader retained an actual source handle or retired stream.");
            Assert.Null(repeated);
            Assert.Null(recoveryError);
            Assert.Null(laterCloseError);
            Assert.True(files.All(f => f.SafeFileHandle.IsClosed), "A source file remained open before fixture cleanup.");
            Assert.True(bytesCorrect, "The initial or recovered bytes differ.");
            Assert.Equal(originalHash, finalHash);
        }
        finally
        {
            try { reader.Dispose(); } catch { }
            foreach (var file in files) file.Dispose();
            DeleteOwnedFolder(folder);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_real_plain_or_gzip_tar_member_keeps_its_bytes_and_closes_the_source(bool compressed)
    {
        string folder = OwnedFolder();
        string path = Path.Combine(folder, compressed ? "owned.tar.gz" : "owned.tar");
        byte[] bytes = Enumerable.Range(0, 4096).Select(n => (byte)((n * 29 + 7) & 255)).ToArray();
        try
        {
            WriteTar(path, bytes, compressed);
            string before = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
            using var reader = ArchiveFormats.Open(path, compressed ? ArchiveKind.TarGzip : ArchiveKind.Tar, Path.GetFileName(path));
            var listed = reader.List(message => throw new InvalidDataException(message), CancellationToken.None).ToArray();
            using var member = reader.Open(0, CancellationToken.None);
            using var destination = new MemoryStream();
            var sourceFile = (FileStream)reader.GetType().GetField(compressed ? "_cursorFile" : "_plain", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(reader)!;
            var sourceHandle = sourceFile.SafeFileHandle;
            member.CopyTo(destination);
            reader.Dispose();
            reader.Dispose();
            byte[] actual = destination.ToArray();
            string after = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
            output.WriteLine(JsonSerializer.Serialize(new { Action = compressed ? "native-tar-gzip" : "native-tar", ExactBytesSHA256 = Convert.ToHexString(SHA256.HashData(actual)), ActualByteCount = actual.Length, ActualMemberNames = listed.Select(v => v.Path).ToArray(), ActualListedSize = listed.Single().Size, InputOriginalSHA256 = before, InputFinalSHA256 = after, FixturePath = folder, OwnedNativeFormatPositive = true, ActualSourceFileHandleClosed = sourceHandle.IsClosed }));
            Assert.Equal(new[] { "owned-member.bin" }, listed.Select(v => v.Path));
            Assert.Equal(4096, listed.Single().Size);
            Assert.Equal(bytes, actual);
            Assert.True(sourceHandle.IsClosed);
            Assert.Equal(before, after);
        }
        finally { DeleteOwnedFolder(folder); }
    }

    private static void WriteTar(string path, byte[] bytes, bool compressed)
    {
        using var file = File.Create(path);
        using var gzip = compressed ? new GZipStream(file, CompressionLevel.Fastest, leaveOpen: true) : null;
        using var writer = new TarWriter(gzip is null ? file : gzip, TarEntryFormat.Ustar, leaveOpen: true);
        using var data = new MemoryStream(bytes, writable: false);
        writer.WriteEntry(new UstarTarEntry(TarEntryType.RegularFile, "owned-member.bin") { DataStream = data, ModificationTime = DateTimeOffset.UnixEpoch });
    }

    private static byte[] First(Stream stream)
    {
        byte[] bytes = new byte[16];
        stream.ReadExactly(bytes);
        return bytes;
    }

    private static string OwnedFolder()
    {
        string folder = Path.Combine(Path.GetTempPath(), "filecat-tar-cursor-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static void DeleteOwnedFolder(string folder)
    {
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(folder).StartsWith(root, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(folder).StartsWith("filecat-tar-cursor-", StringComparison.Ordinal))
            throw new InvalidOperationException("Refuse an unowned fixture path.");
        Directory.Delete(folder, recursive: true);
    }

    private sealed class CloseFaultStream(Stream inner, Exception? failure, bool beforeClose) : Stream
    {
        public int CloseCount { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => inner.Position = value; }
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override int Read(Span<byte> buffer) => inner.Read(buffer);
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void Flush() => inner.Flush();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            if (!disposing) return;
            CloseCount++;
            if (beforeClose && failure is not null) throw failure;
            inner.Dispose();
            if (failure is not null) throw failure;
            base.Dispose(disposing);
        }
    }
}
