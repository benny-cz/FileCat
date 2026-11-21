using System.Collections.Immutable;
using FileCat.Core.Permissions;
using FileCat.Core.State;
using Renci.SshNet;
using Renci.SshNet.Sftp;

namespace FileCat.Remote.Sftp;

internal sealed record SftpPermissionPathEvidence(string Path, string StartDirectory, RemoteSecurityAttributes StartAttributes,
    ImmutableArray<RemoteSecurityAttributes> Ancestors, RemoteSecurityAttributes Leaf);

internal sealed class SftpPermissionPathConflictException(string message, Exception? inner = null) : IOException(message, inner);

/// <summary>Owns one authenticated lease and the observed path through review and pre-dispatch revalidation.</summary>
internal sealed class SftpPermissionPathReview : IDisposable
{
    private readonly SftpLease _lease;
    private readonly SshNetChannel _channel;
    private readonly RemoteProfile _profile;
    private readonly Func<RemoteProfile?> _currentProfile;
    private readonly Func<CancellationToken, SftpLease> _reconnect;
    private readonly PermissionObjectKind _kind;
    private SftpLease? _recoveryLease;
    private bool _recoveryAttempted;
    private bool _lostReply;
    private bool _disposed;

    internal SftpPermissionPathReview(SftpLease lease, RemoteProfile profile, Func<RemoteProfile?> currentProfile,
        Func<CancellationToken, SftpLease> reconnect,
        PermissionSubject subject, string path, CancellationToken cancellation)
    {
        _lease = lease;
        _channel = lease.Channel as SshNetChannel
            ?? throw new RefusedOperationException("This connection has no exact-path SFTP permissions adapter.");
        _profile = profile;
        _currentProfile = currentProfile;
        _reconnect = reconnect;
        _kind = subject.Kind;
        if (SftpServerQualification.Refusal(_channel.ServerVersion, _channel.SecurityExtensions()) is { } refusal)
            throw new RefusedOperationException(refusal);
        Authority = _channel.CaptureAuthority(profile, lease.Generation);
        if (subject.Authority.Generation != Authority.Generation)
            throw new SftpPermissionPathConflictException("The selected SFTP session changed. Inspect the item again.");
        VerifyAuthority();
        Evidence = Observe(_channel, path, cancellation);
        VerifyAuthority();
        if (subject.ListedSize is long size && (ulong)size != Evidence.Leaf.Size ||
            subject.ListedModifiedUtcTicks is long modified && modified != Evidence.Leaf.ModifiedUtc!.Value.Ticks)
            throw new SftpPermissionPathConflictException("The current SFTP attributes differ from the selected listing.");
    }

    internal SftpServerAuthority Authority { get; }
    internal SftpPermissionPathEvidence Evidence { get; }
    internal bool ModeWritesRefused => _lease.ModeWritesRefused;
    internal bool LostReply => _lostReply;
    internal bool EditorQualified => SftpServerQualification.SupportsUnchangedUid(_channel.ServerVersion);

    internal void Revalidate(CancellationToken cancellation)
    {
        if (ReadReviewedLeaf(cancellation) != Evidence.Leaf)
            throw new SftpPermissionPathConflictException("The SFTP object changed after review.");
    }

    internal RemoteSecurityAttributes ReadReviewedLeaf(CancellationToken cancellation)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        VerifyAuthority();
        SftpPermissionPathEvidence current;
        try { current = Observe(_channel, Evidence.Path, cancellation); }
        catch (RemoteDisconnectedException) { _lease.Broken = true; throw; }
        catch (Exception error) when (error is FileNotFoundException or RefusedOperationException or UnauthorizedAccessException)
        { throw new SftpPermissionPathConflictException("The reviewed SFTP path can no longer be corroborated.", error); }
        VerifyAuthority();
        return CorroboratePath(current);
    }

    private RemoteSecurityAttributes CorroboratePath(SftpPermissionPathEvidence current)
    {
        if (Evidence.StartDirectory != current.StartDirectory ||
            Evidence.StartDirectory != Evidence.Path && Evidence.StartAttributes != current.StartAttributes ||
            !Evidence.Ancestors.SequenceEqual(current.Ancestors))
            throw new SftpPermissionPathConflictException("The SFTP object or an ancestor changed after review.");
        return current.Leaf;
    }

    internal uint UnchangedUser(uint user) => SftpServerQualification.SupportsUnchangedUid(_channel.ServerVersion) ? uint.MaxValue : user;

    internal StatusCode? Write(SftpSecurityChange change, bool mode)
    {
        if (_lostReply) throw new SftpPermissionPathConflictException("Review the observed SFTP state before dispatching another change.");
        try { VerifyAuthority(); }
        catch (RemoteDisconnectedException error)
        { throw new SftpPermissionPathConflictException("The SFTP connection was lost before dispatch.", error); }
        if (mode && ModeWritesRefused) throw new SftpPermissionPathConflictException("The server has refused mode changes in this session.");
        try { return _channel.WriteReviewedSecurity(Evidence.Path, change); }
        catch (RemoteDisconnectedException) { RetireWriteLease(); throw; }
    }

    internal RemoteSecurityAttributes ReadAfterDispatch()
    {
        if (!_lostReply)
        {
            try { return ReadReviewedLeaf(CancellationToken.None); }
            catch (RemoteDisconnectedException) { RetireWriteLease(); }
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        if (!_recoveryAttempted)
        {
            _recoveryAttempted = true;
            if (!ProfileIsCurrent())
                throw new SftpPermissionPathConflictException("The SFTP profile changed after dispatch.");
            _recoveryLease = _reconnect(timeout.Token);
        }
        var recovery = _recoveryLease;
        if (recovery is null || recovery.Channel is not SshNetChannel channel)
            throw new SftpPermissionPathConflictException("The original SFTP authority could not be re-observed.");
        try
        {
            VerifyAuthority(recovery, channel);
            if (SftpServerQualification.Refusal(channel.ServerVersion, channel.SecurityExtensions()) is not null)
                throw new SftpPermissionPathConflictException("The SFTP subsystem changed after dispatch.");
            var observed = Observe(channel, Evidence.Path, timeout.Token);
            VerifyAuthority(recovery, channel);
            return CorroboratePath(observed);
        }
        catch (RemoteDisconnectedException) { recovery.Broken = true; throw; }
    }

    internal void RefuseModeWrites() => _lease.RefuseModeWrites();

    private void RetireWriteLease()
    {
        _lostReply = true;
        _lease.Broken = true;
        _lease.Dispose();
    }

    private SftpPermissionPathEvidence Observe(SshNetChannel channel, string path, CancellationToken cancellation) => CaptureEvidence(path,
        channel.HomeDirectory, Authority.User, _kind, channel.ReadExactSecurity, channel.LookupSecurityNames, cancellation);

    private void VerifyAuthority() => VerifyAuthority(_lease, _channel);

    private bool ProfileIsCurrent() => _currentProfile() == _profile && Authority.MatchesProfile(_profile);

    private void VerifyAuthority(SftpLease lease, SshNetChannel channel)
    {
        if (!lease.IsCurrent || !ProfileIsCurrent())
            throw new SftpPermissionPathConflictException("The authenticated SFTP connection changed after the review started.");
        if (!SshNetChannel.Wrap(() => channel.IsConnected))
            throw new RemoteDisconnectedException("The authenticated SFTP connection was lost.");
        SftpServerAuthority current;
        try { current = channel.CaptureAuthority(_profile, lease.Generation); }
        catch (IOException error)
        { throw new SftpPermissionPathConflictException("The authenticated SFTP profile changed after the review started.", error); }
        if (current != Authority)
            throw new SftpPermissionPathConflictException("The authenticated SFTP profile changed after the review started.");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _lease.Dispose(); }
        finally { _recoveryLease?.Dispose(); }
    }

    internal static SftpPermissionPathEvidence CaptureEvidence(string path, string startDirectory, string user, PermissionObjectKind kind,
        Func<string, CancellationToken, RemoteSecurityAttributes> lstat, Func<uint, uint, CancellationToken, SftpAccountNames?> lookup,
        CancellationToken cancellation)
    {
        if (user == "root")
            throw new RefusedOperationException("The root login is inspection-only for SFTP permissions.");
        try
        {
            SftpClient.ValidateSecurityPath(path);
            SftpClient.ValidateSecurityPath(startDirectory);
        }
        catch (ArgumentException)
        {
            throw new RefusedOperationException("The selected SFTP path or the start directory cannot be addressed exactly.");
        }
        if (path == "/" || path.Count(ch => ch == '/') > PermissionSnapshots.MaxPathDepth ||
            kind is not (PermissionObjectKind.File or PermissionObjectKind.Directory))
            throw new RefusedOperationException("SFTP permission review requires an ordinary leaf and bounded ancestry.");

        var ancestors = ImmutableArray.CreateBuilder<RemoteSecurityAttributes>();
        RemoteSecurityAttributes? start = null;
        ReadAncestor("/");
        for (int index = path.IndexOf('/', 1); index >= 0; index = path.IndexOf('/', index + 1))
            ReadAncestor(path[..index]);
        var leaf = Read(path);
        uint expectedType = kind == PermissionObjectKind.Directory ? 0x4000u : 0x8000u;
        if ((leaf.Flags & 0xfu) != 0xfu || (leaf.Permissions & ~0xfffu) != expectedType ||
            leaf.UserId is not uint owner || owner is 0 or uint.MaxValue ||
            leaf.GroupId is not uint group || group == uint.MaxValue || leaf.Size is null || leaf.ModifiedUtc is null)
            throw new RefusedOperationException("The SFTP leaf needs an ordinary type and complete non-root permission attributes.");
        if (ancestors.Any(ancestor => ancestor.UserId != 0 && ancestor.UserId != owner))
            throw new RefusedOperationException("An SFTP ancestor is owned by another server account.");
        var startAttributes = start ?? (startDirectory == path ? leaf : Read(startDirectory));
        if ((startAttributes.Flags & 0x6u) != 0x6u || (startAttributes.Permissions & ~0xfffu) != 0x4000 || startAttributes.UserId != owner)
            throw new RefusedOperationException("The SFTP leaf owner differs from the account's start-directory owner.");
        if (lookup(owner, group, cancellation)?.User != user)
            throw new RefusedOperationException("The server could not corroborate the leaf owner as the signed-in account.");
        return new(path, startDirectory, startAttributes, ancestors.ToImmutable(), leaf);

        RemoteSecurityAttributes Read(string exactPath)
        {
            cancellation.ThrowIfCancellationRequested();
            var observed = lstat(exactPath, cancellation);
            if (observed.Observation != RemoteSecurityObservation.ExactLStat)
                throw new RefusedOperationException("The server path lacks an exact LSTAT observation.");
            return observed;
        }

        void ReadAncestor(string ancestor)
        {
            var observed = Read(ancestor);
            if ((observed.Flags & 0x6u) != 0x6u || (observed.Permissions & ~0xfffu) != 0x4000 ||
                observed.UserId is null or uint.MaxValue)
                throw new RefusedOperationException("An SFTP ancestor is not an observed ordinary directory with an owner.");
            if ((observed.Permissions & 0x12u) != 0)
                throw new RefusedOperationException("An SFTP ancestor permits group or other users to write.");
            ancestors.Add(observed);
            if (ancestor == startDirectory) start = observed;
        }
    }
}
