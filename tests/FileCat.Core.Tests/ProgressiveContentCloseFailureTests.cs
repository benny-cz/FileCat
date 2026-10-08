using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using FileCat.Core.Content;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Core.Tests;

/// <summary>Owned archive-stream close faults must still retire content, spools and the archive lease.</summary>
public sealed class ProgressiveContentCloseFailureTests(ITestOutputHelper output)
{
    public static TheoryData<string, string, string, bool> Cases
    {
        get
        {
            var cases = new TheoryData<string, string, string, bool>();
            foreach (string failure in new[] { "none", "io", "denied", "disposed" })
            {
                foreach (string mode in new[] { "spooled", "forward", "in-place" })
                {
                    cases.Add("dispose", mode, failure, false);
                    cases.Add("dispose", mode, failure, true);
                    cases.Add("abandon", mode, failure, false);
                }
                foreach (string mode in new[] { "spooled", "forward" })
                {
                    cases.Add("healthy-end", mode, failure, false);
                    cases.Add("short-end", mode, failure, false);
                    cases.Add("crc-end", mode, failure, false);
                    cases.Add("growth", mode, failure, false);
                }
                cases.Add("kept-limit", "spooled", failure, false);
            }
            return cases;
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void A_failed_member_close_does_not_skip_cleanup_or_hide_damage(string action, string mode, string failure, bool callbackFailure)
    {
        using var rig = new Rig(action, mode, failure, callbackFailure);
        Exception? observed = null, repeated = null, secondClose = null;
        bool bytesCorrect = true, damageCorrect = true, errorCorrect = true;
        int requested = mode == "spooled" ? 256 * 1024 : 16;
        byte[] first = new byte[16];
        string? nextHex = null, restHash = null;
        int? restCount = null;
        if (action is "growth" or "kept-limit")
            observed = Record.Exception(() => rig.Content.Read(0, first));
        else
        {
            int read = rig.Content.Read(0, first);
            bytesCorrect = read == first.Length && first.SequenceEqual(rig.Bytes[..first.Length]);
            rig.CaptureSpool();
            if (action == "dispose") observed = Record.Exception(rig.Content.Dispose);
            else if (action == "abandon")
            {
                observed = Record.Exception(rig.Content.Abandon);
                repeated = Record.Exception(rig.Content.Abandon);
                byte[] next = new byte[16];
                Exception? readError = Record.Exception(() =>
                {
                    int count = rig.Content.Read(requested, next);
                    bytesCorrect &= count == next.Length && next.SequenceEqual(rig.Bytes[requested..(requested + next.Length)]);
                });
                nextHex = Convert.ToHexString(next);
                bytesCorrect &= readError is null;
                errorCorrect &= repeated is null;
            }
            else
            {
                byte[] rest = new byte[rig.Bytes.Length - 16];
                int count = rig.Content.Read(16, rest);
                restHash = Convert.ToHexString(SHA256.HashData(rest));
                restCount = count;
                bytesCorrect &= count == rest.Length && rest.SequenceEqual(rig.Bytes[16..]);
                rig.CaptureSpool();
                observed = Record.Exception(() => rig.Content.Read(rig.Bytes.Length, new byte[1]));
            }
        }
        bool damage = action is "growth" or "kept-limit" or "short-end" or "crc-end";
        if (damage)
        {
            string expected = action switch
            {
                "growth" => "The member expands beyond its declared size; extraction was stopped.",
                "kept-limit" => "The member is larger than FileCat keeps for viewing (0 GiB); extract it with F5 to read all of it.",
                "short-end" => $"The member ended after {rig.Bytes.Length:N0} of {rig.Bytes.Length + 1:N0} bytes; the archive is damaged.",
                _ => "The member is damaged: its checksum does not match its content.",
            };
            repeated = Record.Exception(() => rig.Content.Read(rig.Bytes.Length, new byte[1]));
            damageCorrect = observed is InvalidDataException && observed.Message == expected
                && repeated is InvalidDataException && repeated.Message == expected;
        }
        else
        {
            Exception? expected = rig.SourceFailure ?? (action == "dispose" && callbackFailure ? rig.CallbackFailure : null);
            errorCorrect &= ReferenceEquals(observed, expected);
        }
        int closesAtAction = rig.Streams.Sum(s => s.Closes);
        Exception? finish = Record.Exception(rig.Content.Dispose);
        if (action == "dispose") secondClose = finish;
        else errorCorrect &= finish is null;
        secondClose ??= Record.Exception(rig.Content.Dispose);
        bool closed = rig.SpoolHandle is null || rig.SpoolHandle.IsClosed;
        bool sourceClosed = rig.Streams.All(s => s.Handle.IsClosed);
        byte[] finalInputHash = SHA256.HashData(File.ReadAllBytes(rig.Path));
        bool inputUnchanged = finalInputHash.SequenceEqual(rig.OriginalHash);
        int closes = rig.Streams.Sum(s => s.Closes);
        output.WriteLine(JsonSerializer.Serialize(new
        {
            Action = action, Mode = mode, Failure = failure, CallbackFailure = callbackFailure,
            ObservedError = observed?.GetType().Name, ObservedMessage = observed?.Message,
            OriginalCloseErrorRetained = errorCorrect, ExactDamageRetained = damageCorrect,
            ActualBytesCorrect = bytesCorrect, InputHashUnchanged = inputUnchanged,
            ActualFirstHex = Convert.ToHexString(first), ActualReopenHex = nextHex, ActualRestSHA256 = restHash, ActualRestCount = restCount,
            InputOriginalSHA256 = Convert.ToHexString(rig.OriginalHash), InputFinalSHA256 = Convert.ToHexString(finalInputHash),
            StreamClosesAtAction = closesAtAction, ProductStreamCloses = closes,
            StreamCount = rig.Streams.Count, ProductSourceHandlesClosed = sourceClosed,
            ProductSpoolHandleClosed = closed, ArchiveLeaseCallbackCount = rig.Callbacks,
            RepeatedDisposeNoError = secondClose is null, FixturePath = rig.Directory,
            NativeArchiveReaderCloseFaultIncidenceNotClaimed = true,
        }));
        Assert.True(errorCorrect, "A later cleanup failure replaced the first error or the abandoned source was reused.");
        Assert.True(damageCorrect, "A source-close failure hid the archive damage diagnosis.");
        Assert.True(bytesCorrect);
        Assert.True(inputUnchanged);
        Assert.True(sourceClosed);
        Assert.True(closed, "The owned spool handle remains open after failed content close.");
        Assert.Equal(1, rig.Callbacks);
        Assert.Equal(rig.Streams.Count, closes);
        Assert.Null(secondClose);
    }

    private sealed class Rig : IDisposable
    {
        public string Directory { get; } = System.IO.Directory.CreateTempSubdirectory("filecat-progressive-close-").FullName;
        public string Path { get; }
        public byte[] Bytes { get; } = Enumerable.Range(0, 256 * 1024 + 64).Select(n => (byte)((n * 37 + 11) & 255)).ToArray();
        public byte[] OriginalHash { get; }
        public Exception? SourceFailure { get; }
        public Exception CallbackFailure { get; } = new IOException("owned archive callback close");
        public List<FaultStream> Streams { get; } = [];
        public ProgressiveContent Content { get; }
        public int Callbacks { get; private set; }
        public FileStream? Spool { get; private set; }
        public SafeFileHandle? SpoolHandle { get; private set; }

        public Rig(string action, string mode, string failure, bool callbackFailure)
        {
            Path = System.IO.Path.Combine(Directory, "member.bin");
            File.WriteAllBytes(Path, Bytes);
            OriginalHash = SHA256.HashData(File.ReadAllBytes(Path));
            SourceFailure = failure switch
            {
                "io" => new IOException("owned member stream close"),
                "denied" => new UnauthorizedAccessException("owned member stream close"),
                "disposed" => new ObjectDisposedException("owned member stream close"),
                _ => null,
            };
            var limits = new MemberLimits(Bytes.Length + (action == "short-end" ? 1 : 0), Bytes.Length,
                action == "growth" ? 8 : Bytes.Length + MemberLimits.Slack, 1000, action == "crc-end" ? 1u : null);
            Content = new ProgressiveContent("owned member", new object(), () =>
            {
                var stream = new FaultStream(Path, mode == "in-place", Streams.Count == 0 ? SourceFailure : null);
                Streams.Add(stream);
                return stream;
            }, limits, action == "kept-limit" ? 8 : Bytes.Length + MemberLimits.Slack, Directory, () =>
            {
                Callbacks++;
                if (callbackFailure) throw CallbackFailure;
            });
            if (mode == "forward") Content.ForwardOnly();
        }

        public void CaptureSpool()
        {
            Spool = (FileStream?)typeof(ProgressiveContent).GetField("_spool", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Content);
            SpoolHandle = Spool?.SafeFileHandle;
        }

        public void Dispose()
        {
            // These emergency fixture closes cannot count as product cleanup or run the lease callback.
            foreach (var source in Streams) source.FixtureClose();
            Spool ??= (FileStream?)typeof(ProgressiveContent).GetField("_spool", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Content);
            Spool?.Dispose();
            Record.Exception(Content.Dispose);
            Assert.All(Streams, stream => Assert.True(stream.Handle.IsClosed));
            string temp = System.IO.Path.GetFullPath(System.IO.Path.GetTempPath());
            Assert.StartsWith(temp, System.IO.Path.GetFullPath(Directory), StringComparison.OrdinalIgnoreCase);
            System.IO.Directory.Delete(Directory, true);
            Assert.False(System.IO.Directory.Exists(Directory));
        }
    }

    private sealed class FaultStream : Stream
    {
        private readonly FileStream _inner;
        private readonly bool _canSeek;
        private readonly Exception? _failure;
        public SafeFileHandle Handle { get; }
        public int Closes { get; private set; }
        public FaultStream(string path, bool canSeek, Exception? failure)
        {
            _inner = File.OpenRead(path); Handle = _inner.SafeFileHandle; _canSeek = canSeek; _failure = failure;
        }
        public override bool CanRead => true;
        public override bool CanSeek => _canSeek;
        public override bool CanWrite => false;
        public override long Length => _inner.Length;
        public override long Position { get => _inner.Position; set => _inner.Position = value; }
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override int Read(Span<byte> buffer) => _inner.Read(buffer);
        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
        public override void Flush() => _inner.Flush();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            Closes++;
            _inner.Dispose();
            if (_failure is not null) throw _failure;
            base.Dispose(disposing);
        }
        public void FixtureClose() => _inner.Dispose();
    }
}
