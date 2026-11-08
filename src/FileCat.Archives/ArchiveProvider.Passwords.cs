using FileCat.Core.Archives;
using FileCat.Core.Resources;

namespace FileCat.Archives;

/// <summary>A password question, without retaining or displaying parser exception details.</summary>
public sealed class ArchiveReadPasswordRequiredException() : IOException("Enter the password to open this archive.");

public sealed partial class ArchiveProvider
{
    internal Func<Location, CancellationToken, Task<PreparedLocation>>? PreparePasswordNavigation { get; set; }

    public override IDisposable? RetainRead(Location location)
    {
        var root = location.WithPath("");
        var index = _cache.Values.FirstOrDefault(index => index.PasswordUnlocked && index.ContainerLocation == root);
        return index?.AcquireView() == true ? new ReadViewLease(this, index) : null;
    }

    public override Task<PreparedLocation>? PrepareNavigationAsync(Location location, CancellationToken ct)
        => HasPasswordPreparation(location) && PreparePasswordNavigation is { } prepare
            ? prepare(location, ct) : base.PrepareNavigationAsync(location, ct);

    internal bool HasPasswordPreparation(Location location)
    {
        string name = location.Container?.Path ?? "";
        var kind = location.Session is { Length: > 0 } forced && Enum.TryParse<ArchiveKind>(forced, out var value)
            ? value : ArchiveFormats.ByName(name);
        return kind is ArchiveKind.Rar or ArchiveKind.SevenZip;
    }

    /// <summary>
    /// Opens a pinned, bounded reader off the UI thread. The returned runtime lease owns an unlocked view;
    /// leaving its last tab evicts credentials, while content already borrowed can finish before zeroing keys.
    /// </summary>
    internal PreparedLocation OpenReadView(Location location, ArchiveJobPassword? password, CancellationToken ct, bool requireRevision = false)
    {
        ct.ThrowIfCancellationRequested();
        var root = location.WithPath("");
        using var source = ArchiveFile(root, 0);
        lock (_cacheLock)
        {
            ArchiveIndex index;
            try { index = GetIndex(root, source, ct, password, requireRevision); }
            catch (InvalidDataException error) when (password is null && NeedsPassword(error))
            { throw new ArchiveReadPasswordRequiredException(); }
            try
            {
                ct.ThrowIfCancellationRequested();
                if (!index.PasswordUnlocked)
                {
                    if (index.Children.Values.Any(nodes => nodes.Any(node => node.Tag?.Encrypted == true)))
                        throw new ArchiveReadPasswordRequiredException();
                    return new(location);
                }
                // A credential-bearing index must be tied to the exact opened file on every reuse.
                if (!index.CanReuse)
                    throw new IOException("This location cannot retain a stable password session. Extract the archive to a local folder and open it there.");
                if (!index.AcquireView()) throw new IOException("The password session was closed. Open the archive again.");
                return new(location, new ReadViewLease(this, index));
            }
            catch
            {
                if (index.PasswordUnlocked && index.ViewCount == 0) ReleaseIndexes(source.Path);
                throw;
            }
        }
    }

    private static bool NeedsPassword(Exception error)
    {
        for (Exception? current = error; current is not null; current = current.InnerException)
            if (current is SharpCompress.Common.CryptographicException ||
                current is InvalidDataException && current.Message == Rar.RarArchive.EncryptedListMessage) return true;
        return false;
    }

    private sealed class ReadViewLease(ArchiveProvider owner, ArchiveIndex index) : IDisposable
    {
        private ArchiveProvider? _owner = owner;
        public void Dispose()
        {
            var provider = Interlocked.Exchange(ref _owner, null);
            if (provider is null) return;
            if (index.ReturnView() != 0) return;
            // Unpublish immediately, without waiting for another reader's I/O or KDF on the UI thread.
            foreach (var pair in provider._cache.Where(pair => ReferenceEquals(pair.Value, index)).ToArray())
                ((ICollection<KeyValuePair<string, ArchiveIndex>>)provider._cache).Remove(pair);
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try { index.Dispose(); }
                catch (Exception error) { FileCat.Core.Diagnostics.AppLog.Warn("Password reader cleanup failed: " + error.GetType().Name); }
            });
        }
    }
}
