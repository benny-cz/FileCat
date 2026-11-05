using FileCat.Core.FileSystem;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Core.Archives.Unix;

internal enum UnixArchiveArtifactKind { Stage, Manifest, Provenance }
internal sealed class UnixArchiveArtifactCreationException(string retainedPath, Exception inner)
    : IOException("The created private archive artifact requires review: " + retainedPath, inner)
{ internal string RetainedPath { get; } = retainedPath; } // Evidence, never pathname cleanup authority.

/// <summary>Unregistered parent storage. Held directory-relative children, private-at-create artifacts, no path deletion.</summary>
internal sealed class UnixArchiveDirectory : IDisposable
{
    internal readonly object Gate = new();
    private readonly SafeFileHandle directory;
    private readonly uint user;
    private int children;
    private bool disposed;
    internal string DisplayPath { get; }
    internal FileIdentity Identity { get; }
    internal UnixArchiveVolume Volume { get; }
    internal SafeFileHandle Handle => directory; // Only under Gate while a live owner/child exists.

    private UnixArchiveDirectory(string path)
    {
        user = UnixArchiveNative.RequireOrdinaryIdentity(); DisplayPath = path; directory = UnixArchiveNative.PinDirectory(path);
        try
        {
            Identity = UnixFiles.Stat(directory)?.Identity ?? throw UnixArchiveNative.Unavailable();
            Volume = UnixArchiveVolume.Observe(directory);
        }
        catch { directory.Dispose(); throw; }
    }
    internal static UnixArchiveDirectory Open(string path) => new(path);
    internal UnixArchiveFile CreateArtifact(Guid nonce, UnixArchiveArtifactKind kind, long maximumLength)
    {
        if (nonce == Guid.Empty || !Enum.IsDefined(kind)) throw new ArgumentException("Invalid Unix archive artifact scope.");
        Limit(kind, maximumLength);
        lock (Gate)
        {
            RequireCapacity(); VerifyNamespace();
            string suffix = kind switch { UnixArchiveArtifactKind.Stage => "stage", UnixArchiveArtifactKind.Manifest => "manifest", _ => "origin" };
            string leaf = $".filecat-archive-{nonce:N}-{Guid.NewGuid():N}.{suffix}";
            var held = UnixArchiveNative.Create(directory, Path.Combine(DisplayPath, leaf), leaf);
            try { return Adopt(held, leaf, maximumLength, artifact: true, writable: true); }
            catch (Exception ex) { throw new UnixArchiveArtifactCreationException(Path.Combine(DisplayPath, leaf), ex); }
        }
    }
    internal UnixArchiveFile OpenSource(string leaf, long maximumLength = ArchiveWritePolicy.MaxPhysicalBytes) => OpenExisting(leaf, maximumLength, null);
    internal UnixArchiveFile OpenRecoveryArtifact(string leaf, FileIdentity expected, UnixArchiveArtifactKind kind, long maximumLength)
    { if (!Enum.IsDefined(kind)) throw new ArgumentException("Invalid artifact kind."); Limit(kind, maximumLength); return OpenExisting(leaf, maximumLength, expected); }
    private UnixArchiveFile OpenExisting(string leaf, long maximumLength, FileIdentity? expected)
    {
        UnixArchiveNative.Leaf(leaf); Limit(UnixArchiveArtifactKind.Stage, maximumLength);
        lock (Gate)
        {
            RequireCapacity(); VerifyNamespace(); var held = UnixArchiveNative.Child(directory, leaf, write: expected.HasValue);
            return Adopt(held, leaf, maximumLength, artifact: expected.HasValue, writable: false, expected);
        }
    }
    private UnixArchiveFile Adopt(SafeFileHandle held, string leaf, long maximumLength, bool artifact, bool writable, FileIdentity? expected = null)
    {
        try
        {
            var facts = UnixFiles.Stat(held) ?? throw UnixArchiveNative.Unavailable();
            if (!facts.IsRegular) throw UnixArchiveNative.Unavailable();
            if (facts.Size < 0 || facts.Size > maximumLength)
                throw new ArchiveWriteException(ArchiveWriteReason.BudgetExceeded, "The Unix archive file exceeds its physical bound.");
            if (expected is { } identity && facts.Identity != identity) throw UnixArchiveNative.Unavailable();
            if (artifact) UnixArchiveNative.RequirePrivate(held, user);
            VerifyNamespace(); RequireName(leaf, facts.Identity);
            var file = new UnixArchiveFile(this, held, leaf, facts.Identity, maximumLength, artifact, writable); children++; return file;
        }
        catch { held.Dispose(); throw; }
    }
    internal void VerifyNamespace()
    {
        using var fresh = UnixArchiveNative.PinDirectory(DisplayPath);
        if (UnixFiles.Stat(fresh)?.Identity != Identity || UnixFiles.Stat(directory)?.Identity != Identity)
            throw new ArchiveWriteException(ArchiveWriteReason.NeedsReview, "The archive destination directory changed; retain its artifacts.");
        if (UnixArchiveVolume.Observe(directory) != Volume) throw UnixArchiveNative.Unavailable();
    }
    internal void RequireName(string leaf, FileIdentity identity)
    {
        using var named = UnixArchiveNative.Child(directory, leaf);
        if (UnixFiles.Stat(named)?.Identity != identity)
            throw new ArchiveWriteException(ArchiveWriteReason.NeedsReview, "The archive name identifies a different object; retain both objects.");
    }
    internal void CheckPrivate(SafeFileHandle held) => UnixArchiveNative.RequirePrivate(held, user);
    private void RequireCapacity()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (children >= 16) throw new ArchiveWriteException(ArchiveWriteReason.BudgetExceeded, "Too many Unix archive file leases are open in this directory.");
    }
    private static void Limit(UnixArchiveArtifactKind kind, long maximum)
    {
        if (maximum < 0 || maximum > (kind == UnixArchiveArtifactKind.Stage ? ArchiveWritePolicy.MaxPhysicalBytes : ArchiveWritePolicy.MaxMetadataBytes))
            throw new ArgumentOutOfRangeException(nameof(maximum));
    }
    internal void ReleaseChild() { children--; if (disposed && children == 0) directory.Dispose(); }
    public void Dispose() { lock (Gate) { if (disposed) return; disposed = true; if (children == 0) directory.Dispose(); } }
}
