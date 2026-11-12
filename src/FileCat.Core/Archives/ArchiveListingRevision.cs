using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Core.Archives;

/// <summary>
/// What binds a native member command to the listing the user acted on. A listing of an archive whose writing route is
/// composed carries this revision's opaque token. As soon as the listing is shown, the archive is read once in the
/// background to its SHA-256: through the listing's own held handle, while the listing's change token holds, outside
/// every provider lock, at low I/O priority, and stopped when the listing's index closes. A command that needs the
/// revision waits for that read. The rows therefore appear at once and nothing else waits, while the binding window is,
/// as when the listing itself read the whole archive, only the time of that read; a change made to the archive after it
/// is refused by the command's native review, which compares the SHA-256 of what it assesses (independent reviews of
/// 12 November 2026). An archive larger than the provider's background limit is read when the first change needs it
/// instead; until then only its change token binds the command to the listing, so a writer that restores the change time
/// on an NTFS volume without a change journal is not noticed before the review. A resolved revision outlives the
/// listing's index (its provider remembers it by token).
/// </summary>
internal sealed class ArchiveListingRevision
{
    internal const string TokenPrefix = "listing:";
    /// <summary>Listings of archives up to this size read their revision in the background as soon as they are shown.</summary>
    internal const long DefaultBackgroundLimit = 1L << 30;
    internal const string Changed = "The archive changed since it was listed. Refresh it and try again.";
    internal const string Closed = "The archive changed or its listing was closed since these members were selected. Refresh it and try again.";
    internal const string TooLarge = "This archive is larger than FileCat changes (80 GiB), so its members are read-only here. Copy (extract) what you need with F5.";
    private const string Unreadable = "The complete archive could not be read for member changes. Refresh it and try again.";
    private readonly SafeFileHandle handle;
    private readonly Func<bool> unchanged;
    private readonly Func<IDisposable?> lease;
    private readonly Action<string, string>? resolved;
    private readonly CancellationTokenSource lifetime = new();
    private readonly object gate = new();
    private Task<string>? reading;
    private volatile bool urgent;

    /// <param name="handle">The listing's own handle, kept open by <paramref name="lease"/> while it is read.</param>
    /// <param name="unchanged">Whether the listing's change token still holds for that handle.</param>
    /// <param name="lease">Keeps the listing's index (and so the handle) open; null once the index is closing.</param>
    /// <param name="problem">Why this listing cannot bind member changes at all, known while listing; null when it can.</param>
    /// <param name="resolved">Told the token and revision once it is read (the provider remembers it beyond the index).</param>
    internal ArchiveListingRevision(SafeFileHandle handle, Func<bool> unchanged, Func<IDisposable?> lease, string? problem,
        Action<string, string>? resolved = null)
    {
        ArgumentNullException.ThrowIfNull(handle); ArgumentNullException.ThrowIfNull(unchanged); ArgumentNullException.ThrowIfNull(lease);
        this.handle = handle; this.unchanged = unchanged; this.lease = lease; this.resolved = resolved;
        // An archive past the write profile's size is never changed: its members say so at once, rather than at each command.
        if (problem is null && Length() > ArchiveWritePolicy.MaxPhysicalBytes) problem = TooLarge;
        Problem = problem;
    }

    /// <summary>What listing rows carry: it names this listing, never an archive's content.</summary>
    internal string Token { get; } = TokenPrefix + Guid.NewGuid().ToString("N");
    /// <summary>Why this listing's members cannot be changed, or null.</summary>
    internal string? Problem { get; }
    /// <summary>The revision once it was read; null before.</summary>
    internal string? Known => Volatile.Read(ref reading) is { IsCompletedSuccessfully: true } done ? done.Result : null;

    /// <summary>Whether a listing row's revision is a token to resolve (rather than a revision a provider already read).</summary>
    internal static bool IsToken(string? revision) => revision is not null && revision.StartsWith(TokenPrefix, StringComparison.Ordinal);

    /// <summary>The listing was shown: read the revision now in the background, unless the archive is larger than <paramref name="limit"/>.</summary>
    internal void ReadInBackground(long limit)
    {
        if (Problem is not null) return;
        long length = Length();
        if (length >= 0 && length <= limit) Begin(retry: false);
    }

    /// <summary>The SHA-256 of the archive this listing showed: read once, then kept. Throws with the reason it cannot be.</summary>
    internal string Resolve(CancellationToken ct)
    {
        if (Problem is { } problem) throw new IOException(problem);
        ct.ThrowIfCancellationRequested();
        urgent = true; // A command waits now: a background read continues at normal priority.
        var task = Begin(retry: true);
        try { return task.WaitAsync(ct).GetAwaiter().GetResult(); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new IOException(Closed); }
    }

    /// <summary>The listing's index closes: a read in progress stops, and this listing is not read again.</summary>
    internal void Close()
    {
        try { lifetime.Cancel(); } catch (ObjectDisposedException) { }
    }

    // The one read of this listing's revision. A failed read (a locked region, an unreadable sector) is tried again by the
    // next command that asks; the background never retries, and nothing reads a closed listing.
    private Task<string> Begin(bool retry)
    {
        lock (gate)
        {
            if (reading is { } current && !(retry && current.IsFaulted && !lifetime.IsCancellationRequested)) return current;
            if (lifetime.IsCancellationRequested) return Task.FromCanceled<string>(lifetime.Token);
            var held = lease();
            if (held is null) return Task.FromCanceled<string>(new CancellationToken(canceled: true));
            var started = Task.Factory.StartNew(() => { using (held) return Read(); }, CancellationToken.None,
                TaskCreationOptions.LongRunning | TaskCreationOptions.DenyChildAttach, TaskScheduler.Default);
            Volatile.Write(ref reading, started);
            return started;
        }
    }

    private string Read()
    {
        bool lowered = !urgent && BackgroundIo.Lower();
        if (!unchanged()) throw new IOException(Changed);
        string value;
        try
        {
            value = Hash(handle, lifetime.Token, () =>
            {
                if (lowered && urgent) { BackgroundIo.Restore(); lowered = false; }
            });
        }
        catch (ArchiveChangedWhileReadException) { throw new IOException(Changed); }
        catch (ObjectDisposedException error) { throw new IOException(Closed, error); }
        catch (IOException error) { throw new IOException(Unreadable, error); }
        if (!unchanged()) throw new IOException(Changed);
        resolved?.Invoke(Token, value);
        return value;
    }

    private long Length()
    {
        try { return RandomAccess.GetLength(handle); }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or UnauthorizedAccessException) { return -1; }
    }

    /// <summary>The SHA-256 of the whole file behind <paramref name="witness"/>, read at explicit offsets.</summary>
    internal static string Hash(SafeFileHandle witness, CancellationToken ct, Action? between = null)
    {
        ArgumentNullException.ThrowIfNull(witness);
        long length = RandomAccess.GetLength(witness);
        if (length < 0 || length > ArchiveWritePolicy.MaxPhysicalBytes) throw new ArchiveChangedWhileReadException();
        using var hash = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
        byte[] bytes = System.Buffers.ArrayPool<byte>.Shared.Rent(ArchiveIoAuthority.MaximumReadBytes);
        try
        {
            long offset = 0;
            while (offset < length)
            {
                ct.ThrowIfCancellationRequested(); between?.Invoke();
                int read = RandomAccess.Read(witness, bytes.AsSpan(0, (int)Math.Min(bytes.Length, length - offset)), offset);
                if (read <= 0) throw new ArchiveChangedWhileReadException();
                hash.AppendData(bytes, 0, read); offset += read;
            }
            ct.ThrowIfCancellationRequested();
            if (RandomAccess.GetLength(witness) != length) throw new ArchiveChangedWhileReadException();
            return Convert.ToHexString(hash.GetHashAndReset());
        }
        finally { System.Buffers.ArrayPool<byte>.Shared.Return(bytes, clearArray: true); }
    }

    // A short read or a length that moved: the file changed under the read, not a read error.
    private sealed class ArchiveChangedWhileReadException() : IOException("The archive changed while its listing revision was read.");
}

/// <summary>
/// Lowers, and restores, the I/O priority of the calling thread for a read nothing waits for yet (a listing's revision):
/// Windows background mode, macOS's throttled disk policy, Linux's idle I/O class. Best effort: where the system offers
/// none, the read runs at normal priority.
/// </summary>
internal static partial class BackgroundIo
{
    internal static bool Lower() => Set(background: true);
    internal static void Restore() => Set(background: false);

    private static bool Set(bool background)
    {
        try
        {
            if (OperatingSystem.IsWindows())
                return SetThreadPriority(GetCurrentThread(), background ? 0x0001_0000 /* THREAD_MODE_BACKGROUND_BEGIN */ : 0x0002_0000 /* _END */);
            if (OperatingSystem.IsMacOS())
                return SetIoPolicy(0 /* IOPOL_TYPE_DISK */, 1 /* IOPOL_SCOPE_THREAD */, background ? 3 /* IOPOL_THROTTLE */ : 0 /* IOPOL_DEFAULT */) == 0;
            if (OperatingSystem.IsLinux() && RuntimeInformation.ProcessArchitecture is Architecture.X64 or Architecture.Arm64)
                // ioprio_set(IOPRIO_WHO_PROCESS, 0 = this thread, IOPRIO_CLASS_IDLE or IOPRIO_CLASS_NONE, the default).
                return Linux.LinuxArchiveNative.Syscall(RuntimeInformation.ProcessArchitecture == Architecture.X64 ? 251 : 30,
                    1, 0, background ? 3 << 13 : 0, 0, 0, 0) == 0;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { }
        return false;
    }

    [LibraryImport("kernel32.dll")]
    private static partial nint GetCurrentThread();
    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetThreadPriority(nint thread, int priority);
    [LibraryImport("libc", EntryPoint = "setiopolicy_np")]
    private static partial int SetIoPolicy(int type, int scope, int policy);
}
