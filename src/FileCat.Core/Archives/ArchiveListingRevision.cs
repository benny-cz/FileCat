using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Core.Archives;

/// <summary>
/// What binds a native member command to the listing the user acted on (independent reviews of 12 November 2026).
/// A listing of an archive whose writing route is composed carries this revision's opaque token; resolving it gives the
/// SHA-256 of the archive the listing showed, read through the listing's own file while the listing's change token holds.
/// The command's native review then refuses the change when the archive it assesses has another SHA-256.
/// <para>Two kinds of read, never shared. While a panel shows the listing, and the archive is at most the provider's
/// background limit, one read runs in the background on a thread of its own, at low I/O priority, through a file object of
/// its own on Windows (so member reads never queue behind it); it stops a grace period after no panel shows the listing
/// any more (a panel moving between the archive's folders keeps it going), when the listing's index closes, or when a
/// command starts reading, and none starts while a command reads. It binds a later command to what the listing showed as
/// tightly as when the listing itself read the whole archive. A command that finds no revision read yet reads the archive
/// itself, on its own thread, at normal priority, and stops with the command; its caller keeps the listing's file open.
/// Above the background limit only that command's read binds it, and until then the change token alone (a writer that
/// restores the change time on an NTFS volume without a change journal is not noticed before the review).</para>
/// <para>A change a read sees is final for this listing, and its index is marked stale, so a refresh lists the archive
/// anew with a new token; a change token that cannot be read at the moment is an error to try again, not a change. A
/// revision once read outlives the listing's index (its provider remembers it by token).</para>
/// </summary>
internal sealed class ArchiveListingRevision
{
    internal const string TokenPrefix = "listing:";
    /// <summary>Shown listings of archives up to this size read their revision in the background.</summary>
    internal const long DefaultBackgroundLimit = 1L << 30;
    internal const string Changed = "The archive changed since it was listed. Refresh it and try again.";
    internal const string Closed = "The archive changed or its listing was closed since these members were selected. Refresh it and try again.";
    internal const string TooLarge = "This archive is larger than FileCat changes (80 GiB), so its members are read-only here. Copy (extract) what you need with F5.";
    internal const string Unreadable = "The complete archive could not be read for member changes. Refresh it and try again.";
    private readonly SafeFileHandle handle;
    private readonly Func<bool> unchanged;
    private readonly Func<IDisposable?> lease;
    private readonly Action<string, string>? resolved;
    private readonly Action? stale;
    private readonly object gate = new();
    private readonly SemaphoreSlim commands = new(1, 1);
    private string? known, seenChange;
    private int shown, reading, backgroundReads, graceGeneration;
    private bool closed, began;
    private CancellationTokenSource? background;
    private ITimer? grace;

    /// <param name="handle">The listing's own handle. A background read keeps it open with <paramref name="lease"/>; a command's caller keeps it open.</param>
    /// <param name="unchanged">Whether the listing's change token still holds for that handle; throws IOException when it cannot be read now.</param>
    /// <param name="lease">Keeps the listing's index (and so the handle) open; null once the index is closing.</param>
    /// <param name="problem">Why this listing cannot bind member changes at all, known while listing; null when it can.</param>
    /// <param name="resolved">Told the token and revision once it is read (the provider remembers it beyond the index).</param>
    /// <param name="stale">Marks the listing's index stale once a read saw the archive change, so a refresh lists it anew.</param>
    internal ArchiveListingRevision(SafeFileHandle handle, Func<bool> unchanged, Func<IDisposable?> lease, string? problem,
        Action<string, string>? resolved = null, Action? stale = null)
    {
        ArgumentNullException.ThrowIfNull(handle); ArgumentNullException.ThrowIfNull(unchanged); ArgumentNullException.ThrowIfNull(lease);
        this.handle = handle; this.unchanged = unchanged; this.lease = lease; this.resolved = resolved; this.stale = stale;
        // An archive past the write profile's size is never changed: its members say so at once, rather than at each command.
        if (problem is null && Length() > ArchiveWritePolicy.MaxPhysicalBytes) problem = TooLarge;
        Problem = problem;
    }

    /// <summary>What listing rows carry: it names this listing, never an archive's content.</summary>
    internal string Token { get; } = TokenPrefix + Guid.NewGuid().ToString("N");
    /// <summary>Why this listing's members cannot be changed, or null.</summary>
    internal string? Problem { get; }
    /// <summary>The revision once it was read; null before.</summary>
    internal string? Known => Volatile.Read(ref known);
    /// <summary>Whether any read of this listing's revision began (tests and diagnostics).</summary>
    internal bool ReadBegan { get { lock (gate) return began; } }
    /// <summary>How many background reads were started (tests and diagnostics).</summary>
    internal int BackgroundReads { get { lock (gate) return backgroundReads; } }
    /// <summary>Whether a background read was told to stop and has not ended yet (tests and diagnostics).</summary>
    internal bool BackgroundStopping { get { lock (gate) return background is { IsCancellationRequested: true }; } }

    /// <summary>Whether a listing row's revision is a token to resolve (rather than a revision a provider already read).</summary>
    internal static bool IsToken(string? revision) => revision is not null && revision.StartsWith(TokenPrefix, StringComparison.Ordinal);

    /// <summary>
    /// How long a background read goes on after the last panel stopped showing the listing: long enough for a panel moving to
    /// another folder of the same archive (its new listing shows the same revision again), short of keeping a read running
    /// for a panel that left the archive.
    /// </summary>
    internal TimeSpan HiddenGrace { get; set; } = TimeSpan.FromSeconds(10);
    /// <summary>The clock that ends the grace (tests drive their own).</summary>
    internal TimeProvider Clock { get; set; } = TimeProvider.System;

    /// <summary>
    /// A panel shows this listing: while any does, its revision is read in the background (once, unless the archive is
    /// larger than <paramref name="limit"/>, and not while a command reads it). Dispose the result when the panel stops
    /// showing it.
    /// </summary>
    internal IDisposable Show(long limit)
    {
        long length = Problem is null ? Length() : -1;
        CancellationTokenSource? start = null;
        lock (gate)
        {
            // Shown again within the grace (another folder of the same archive): the read in progress goes on.
            if (shown++ == 0) EndGrace();
            if (background is not { IsCancellationRequested: false } &&
                length >= 0 && length <= limit && known is null && seenChange is null && !closed && reading == 0)
            {
                start = new CancellationTokenSource(); background = start; began = true; backgroundReads++;
            }
        }
        if (start is not null) ReadInBackground(start);
        return new Showing(this);
    }

    /// <summary>The SHA-256 of the archive this listing showed: read once, then kept. Throws with the reason it cannot be.</summary>
    /// <remarks>The caller keeps the listing's file open (an index lease) until this returns.</remarks>
    internal string Resolve(CancellationToken ct)
    {
        if (Problem is { } problem) throw new IOException(problem);
        ct.ThrowIfCancellationRequested();
        if (Volatile.Read(ref seenChange) is { } change) throw new IOException(change);
        if (Volatile.Read(ref known) is { } value) return value;
        commands.Wait(ct);
        try
        {
            if (Volatile.Read(ref seenChange) is { } seen) throw new IOException(seen);
            if (Volatile.Read(ref known) is { } read) return read;
            // A background read gives way, and none starts while this command reads the archive itself, at normal priority.
            lock (gate) { background?.Cancel(); reading++; began = true; }
            try { return Read(ct); }
            finally { lock (gate) reading--; }
        }
        finally { commands.Release(); }
    }

    /// <summary>The listing's index closes: a background read stops, and none begins again. A command's read goes on.</summary>
    internal void Close()
    {
        lock (gate) { closed = true; background?.Cancel(); EndGrace(); }
    }

    // The gate is never held while waiting for anything else: a panel's tab closing (on the UI thread) only takes it briefly.
    private void Hide()
    {
        lock (gate)
        {
            if (--shown > 0 || background is not { IsCancellationRequested: false } running) return;
            if (HiddenGrace <= TimeSpan.Zero) { running.Cancel(); return; }
            EndGrace();
            grace = Clock.CreateTimer(GraceEnded, new Grace(running, graceGeneration), HiddenGrace, Timeout.InfiniteTimeSpan);
        }
    }

    // Under the gate. A grace's timer may already have fired when it is ended; its callback then finds another generation.
    private void EndGrace()
    {
        graceGeneration++;
        grace?.Dispose(); grace = null;
    }

    private sealed record Grace(CancellationTokenSource Read, int Generation);

    // The grace ended: its read stops, unless the listing was shown again or the read is not the one it was for. Decided
    // under the gate, so a timer that fired as the listing was shown again stops nothing.
    private void GraceEnded(object? state)
    {
        var ended = (Grace)state!;
        lock (gate)
            if (ended.Generation == graceGeneration && shown == 0 && ReferenceEquals(background, ended.Read)) ended.Read.Cancel();
    }

    // Weak: a panel still showing a listing whose index has left the cache must not keep that index's catalog in memory. A
    // running read keeps its revision, and so its index, alive by itself.
    private sealed class Showing(ArchiveListingRevision owner) : IDisposable
    {
        private WeakReference<ArchiveListingRevision>? _owner = new(owner);
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _owner, null) is { } weak && weak.TryGetTarget(out var revision)) revision.Hide();
        }
    }

    private void ReadInBackground(CancellationTokenSource cancel)
    {
        try
        {
            Task.Factory.StartNew(() => ReadInBackgroundNow(cancel), CancellationToken.None,
                TaskCreationOptions.LongRunning | TaskCreationOptions.DenyChildAttach, TaskScheduler.Default);
        }
        catch (Exception error) when (error is TaskSchedulerException or OutOfMemoryException or InvalidOperationException)
        {
            Ended(cancel);
            cancel.Dispose();
        }
    }

    // On a thread of its own, which ends with the read (nothing to restore). The lease, which keeps the listing's file open,
    // is taken here: the index's lock may be busy for a while (a viewer decompressing a member), and nothing waits for this.
    private void ReadInBackgroundNow(CancellationTokenSource cancel)
    {
        try
        {
            using var held = lease();
            // Read meanwhile (by an earlier read that ended as this one began, or a command), or seen changed: nothing to do.
            if (held is null || cancel.IsCancellationRequested || Volatile.Read(ref known) is not null ||
                Volatile.Read(ref seenChange) is not null) return;
            BackgroundIo.LowerThisThread();
            Read(cancel.Token);
        }
        catch (Exception error) when (error is IOException or OperationCanceledException) { } // A command reads again.
        finally
        {
            Ended(cancel);
            cancel.Dispose();
        }
    }

    // Before its token source is disposed: nothing reaches it through background or a grace any more.
    private void Ended(CancellationTokenSource cancel)
    {
        lock (gate)
        {
            if (!ReferenceEquals(background, cancel)) return;
            background = null; EndGrace();
        }
    }

    private string Read(CancellationToken cancel)
    {
        using SafeFileHandle? own = BackgroundIo.OwnFileObject(handle);
        Check();
        string value;
        try { value = Hash(own ?? handle, cancel); }
        catch (ArchiveChangedWhileReadException) { throw Seen(); }
        catch (ObjectDisposedException error) { throw new IOException(Closed, error); }
        catch (IOException error) { throw new IOException(Unreadable, error); }
        Check();
        string kept = Interlocked.CompareExchange(ref known, value, null) ?? value;
        resolved?.Invoke(Token, kept);
        return kept;
    }

    // The listing's change token must still hold. One that cannot be read at the moment (a network share's hiccup) is an
    // error the next command tries again, not a change.
    private void Check()
    {
        bool same;
        try { same = unchanged(); }
        catch (IOException error) { throw new IOException(Unreadable, error); }
        if (!same) throw Seen();
    }

    // The listing no longer shows this archive: every later command on it is refused, even if the change token is restored,
    // and its index is stale, so a refresh lists the archive anew with a new token.
    private IOException Seen()
    {
        bool first;
        lock (gate) { first = seenChange is null; seenChange ??= Changed; background?.Cancel(); }
        if (first) stale?.Invoke();
        return new IOException(Changed);
    }

    private long Length()
    {
        try { return RandomAccess.GetLength(handle); }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or UnauthorizedAccessException) { return -1; }
    }

    /// <summary>The SHA-256 of the whole file behind <paramref name="witness"/>, read at explicit offsets.</summary>
    internal static string Hash(SafeFileHandle witness, CancellationToken ct)
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
                ct.ThrowIfCancellationRequested();
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
/// For a read nothing waits for yet (a shown listing's revision), on a thread of its own that ends with the read: lowers
/// that thread's I/O priority (Windows background mode, macOS's throttled disk policy, Linux's idle I/O class), and gives
/// it a file object of its own on Windows. Best effort: where the system offers neither, the read runs at normal priority
/// through the listing's own handle.
/// </summary>
internal static partial class BackgroundIo
{
    internal static void LowerThisThread()
    {
        try
        {
            if (OperatingSystem.IsWindows()) SetThreadPriority(GetCurrentThread(), 0x0001_0000 /* THREAD_MODE_BACKGROUND_BEGIN */);
            else if (OperatingSystem.IsMacOS()) SetIoPolicy(0 /* IOPOL_TYPE_DISK */, 1 /* IOPOL_SCOPE_THREAD */, 3 /* IOPOL_THROTTLE */);
            else if (OperatingSystem.IsLinux() && RuntimeInformation.ProcessArchitecture is Architecture.X64 or Architecture.Arm64)
                // ioprio_set(IOPRIO_WHO_PROCESS, 0 = this thread, IOPRIO_CLASS_IDLE).
                Linux.LinuxArchiveNative.Syscall(RuntimeInformation.ProcessArchitecture == Architecture.X64 ? 251 : 30, 1, 0, 3 << 13, 0, 0, 0);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { }
    }

    /// <summary>
    /// On Windows, a second file object on the same file: synchronous I/O on one file object is serialized, so a slow read
    /// through the listing's own handle would hold up member reads through it. Null elsewhere, or when the system refuses
    /// (the read then uses the listing's handle).
    /// </summary>
    internal static SafeFileHandle? OwnFileObject(SafeFileHandle handle)
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            var own = ReOpenFile(handle, 0x8000_0000 /* GENERIC_READ */, 7 /* read, write and delete sharing */, 0x0800_0000 /* sequential scan */);
            if (!own.IsInvalid) return own;
            own.Dispose();
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or ObjectDisposedException) { }
        return null;
    }

    [LibraryImport("kernel32.dll")]
    private static partial nint GetCurrentThread();
    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetThreadPriority(nint thread, int priority);
    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial SafeFileHandle ReOpenFile(SafeFileHandle original, uint access, uint share, uint flags);
    [LibraryImport("libc", EntryPoint = "setiopolicy_np")]
    private static partial int SetIoPolicy(int type, int scope, int policy);
}
