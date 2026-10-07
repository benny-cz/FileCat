using FileCat.Core.FileSystem;
using FileCat.Core.Listing;
using FileCat.Core.Resources;
using FileCat.Core.Threading;

namespace FileCat.App.ViewModels;

public sealed partial class MainViewModel
{
    /// <summary>Preparation belongs to this listing generation until the user sees its result.</summary>
    private sealed class PreparationScope : IDisposable
    {
        private readonly TabViewModel _tab;
        private readonly int _generation;
        private readonly DeviceIoScheduler _io;
        private readonly IReadOnlyList<ItemRef>? _selection;
        public readonly CancellationTokenSource Stop = new();
        public bool SelectionTransferred;
        public PreparationScope(TabViewModel tab, DeviceIoScheduler io, IReadOnlyList<ItemRef>? selection = null)
        {
            _tab = tab; _generation = tab.Listing.Generation; _io = io; _selection = selection;
            tab.Listing.Changed += Changed; tab.Closed += Closed;
        }
        public bool Current => !Stop.IsCancellationRequested && !_io.IsStopped && !_tab.Listing.IsDisposed && _tab.Listing.Generation == _generation;
        private void Changed(object? sender, ListingChange change) { if (!Current) Stop.Cancel(); }
        private void Closed() => Stop.Cancel();
        public void Check() { if (!Current) throw new OperationCanceledException("Preparation no longer belongs to the current listing."); }
        public void Dispose()
        {
            _tab.Listing.Changed -= Changed; _tab.Closed -= Closed;
            if (!SelectionTransferred) ItemSources.Release(_selection);
            Stop.Dispose();
        }
    }

    // Summaries retain constant metadata space even for a large captured selection.
    private sealed class AttributeMetadata
    {
        public int Count;
        public FileSystemItemInfo? Single;
        public bool HasFolders;
        public FileAttributes AllAttributes = (FileAttributes)(-1), AnyAttributes;
        public UnixFileMode AllModes = (UnixFileMode)(-1), AnyModes;
        public bool? State(FileAttributes bit) => (AllAttributes & bit) != 0 ? true : (AnyAttributes & bit) == 0 ? false : null;
        public bool? State(UnixFileMode bit) => (AllModes & bit) != 0 ? true : (AnyModes & bit) == 0 ? false : null;
        public bool ModesChange(UnixFileMode set, UnixFileMode clear) => (AllModes & set) != set || (AnyModes & clear) != 0;
    }

    private async Task<AttributeMetadata> ReadAttributeMetadataAsync(IReadOnlyList<ItemRef> selection, PreparationScope scope)
    {
        var summary = new AttributeMetadata(); var files = Services.Platform.FileOperations;
        foreach (var item in selection)
        {
            scope.Check();
            if (item.FileSystemPath is not { } path) throw new NotSupportedException("Attributes can be changed for file-system items.");
            var provider = Services.Providers.For(item.Parent);
            var data = await Services.Io.Run(provider.GetDeviceKey(item.Parent), IoPriority.Normal, _ =>
            {
                scope.Check();
                var info = files.TryGetInfo(path) ?? throw new IOException("The marked items no longer exist or cannot be read.");
                UnixFileMode mode = 0;
                if (!OperatingSystem.IsWindows()) mode = UnixPermissions.Stat(path)?.Mode ?? throw new IOException("The marked items no longer exist or cannot be read.");
                return (Info: info, Mode: mode);
            }); // An active synchronous call keeps its owner until it returns.
            scope.Check();
            summary.Count++; summary.Single = summary.Count == 1 ? data.Info : null;
            summary.HasFolders |= item.IsContainer;
            summary.AllAttributes &= data.Info.Attributes; summary.AnyAttributes |= data.Info.Attributes;
            summary.AllModes &= data.Mode; summary.AnyModes |= data.Mode;
        }
        return summary;
    }

    /// <summary>Borrowed random-access source adapted only during the bounded signature probe.</summary>
    private sealed class SignatureStream(IContentSource source, Action check) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => source.CanSeek;
        public override bool CanWrite => false;
        public override long Length { get { check(); return source.Length; } }
        public override long Position { get; set; }
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer)
        {
            check(); int n = source.Read(Position, buffer);
            if (n < 0 || n > buffer.Length) throw new InvalidDataException("The signature source returned an invalid byte count.");
            Position += n; return n;
        }
        public override long Seek(long offset, SeekOrigin origin) => Position = origin switch
        { SeekOrigin.Begin => offset, SeekOrigin.Current => Position + offset, SeekOrigin.End => Length + offset, _ => throw new ArgumentOutOfRangeException(nameof(origin)) };
        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
