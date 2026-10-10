using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using FileCat.Core.Diagnostics;
using FileCat.Core.State;

namespace FileCat.Remote.Sftp;

public enum HostKeyDecision
{
    Reject,
    /// <summary>Trust for this connection only.</summary>
    AcceptOnce,
    /// <summary>Trust and remember in FileCat's known_hosts (for a changed key: replace the remembered one).</summary>
    AcceptAndRemember,
}

/// <param name="Passphrase">A key passphrase rather than a password.</param>
/// <param name="Retry">The previous secret was rejected.</param>
/// <param name="CanSave">An OS credential store exists, so "save" is meaningful.</param>
public sealed record SecretRequest(bool Passphrase, bool Retry, bool CanSave);

public sealed record SecretAnswer(string Secret, bool Save);

/// <summary>
/// What connecting may need from the user. Called on a background thread; implementations may block it while the UI
/// asks (never on the UI thread).
/// </summary>
public interface IRemoteInteraction
{
    HostKeyDecision DecideHostKey(RemoteProfile profile, HostKeyCheck check);

    /// <summary>A password or key passphrase; null when the user cancels.</summary>
    SecretAnswer? AskSecret(RemoteProfile profile, SecretRequest request);

    /// <summary>Keyboard-interactive answers, one per prompt; null when the user cancels.</summary>
    IReadOnlyList<string>? AnswerPrompts(RemoteProfile profile, string instruction, IReadOnlyList<(string Prompt, bool Echo)> prompts);

    /// <summary>An FTPS certificate the OS does not accept (self-signed, wrong name, changed since pinned).</summary>
    HostKeyDecision DecideCertificate(RemoteProfile profile, Ftp.CertificateCheck check) => HostKeyDecision.Reject;

    /// <summary>Unencrypted FTP to a server the user did not choose it for knowingly; false cancels.</summary>
    bool AllowUnencrypted(RemoteProfile profile) => false;

    /// <summary>Something the user should know that needs no answer (a password the OS store did not keep).</summary>
    void Inform(RemoteProfile profile, string message) { }
}

/// <summary>SFTP profiles connect over SSH, FTP ones over FTP or FTPS.</summary>
public sealed class ProtocolConnector(ISftpConnector ssh, ISftpConnector ftp) : ISftpConnector
{
    public ISftpChannel Connect(RemoteProfile profile, ConnectContext context, CancellationToken ct) =>
        (profile.IsFtp ? ftp : ssh).Connect(profile, context, ct);
}

/// <summary>The callbacks a connector uses while it connects one channel.</summary>
public sealed class ConnectContext
{
    /// <summary>The server's host key blob; true lets the connection continue.</summary>
    public required Func<byte[], bool> ApproveHostKey { get; init; }

    /// <summary>An FTPS server's certificate (with the OS's objections, if any); true lets the connection continue.</summary>
    public Func<Ftp.CertificateInfo, bool> ApproveCertificate { get; init; } = info => info.Problems.Count == 0;

    /// <summary>A password or passphrase (argument: passphrase?); null cancels.</summary>
    public required Func<bool, string?> GetSecret { get; init; }

    public required Func<string, IReadOnlyList<(string Prompt, bool Echo)>, IReadOnlyList<string>?> AnswerPrompts { get; init; }
}

/// <summary>Opens one SFTP connection (SSH.NET in the product).</summary>
public interface ISftpConnector
{
    ISftpChannel Connect(RemoteProfile profile, ConnectContext context, CancellationToken ct);
}

/// <summary>The server rejected the credentials (a retry may ask again, a bounded number of times).</summary>
public sealed class RemoteAuthenticationException(string message, Exception? inner = null) : IOException(message, inner);

/// <summary>The user or FileCat's trust store did not accept the server's host key.</summary>
public sealed class HostKeyRejectedException(string message) : IOException(message);

/// <summary>The user canceled a prompt that connecting needed.</summary>
public sealed class ConnectCanceledException() : OperationCanceledException("Connecting was canceled.");

/// <summary>
/// Connecting needed a question the UI does not ask right now (tabs restored at startup): an error the panel shows,
/// with the way to connect.
/// </summary>
public sealed class PromptDeferredException(string message) : IOException(message);

/// <summary>
/// Connections per server (plan §14.1): leased by tabs, viewers, and jobs, reused within a bound, re-established when
/// broken, and closed on request. Host identity is verified on every new connection; secrets typed during the session
/// stay in memory, and are saved only where the user asked and an OS store exists.
/// </summary>
public sealed class SftpConnections : IDisposable
{
    public const int MaxPerServer = 4;
    /// <summary>FTP servers often allow only a few connections per address.</summary>
    public const int MaxPerFtpServer = 2;
    /// <summary>Unused connections close after this long (plan §14.1: close idle sessions).</summary>
    public static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(10);
    private const int MaxAuthenticationAttempts = 3;
    private readonly Timer _sweeper;
    private volatile bool _disposed;

    private readonly Func<string, RemoteProfile?> _profiles;
    private readonly ISftpConnector _connector;
    private readonly HostKeyTrust _trust;
    private readonly Ftp.CertificateTrust? _certificates;
    private readonly ConcurrentDictionary<string, bool> _plainAllowed = new(StringComparer.Ordinal);
    private readonly ISecretStore _secrets;
    private readonly ConcurrentDictionary<string, Pool> _pools = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _sessionSecrets = new(StringComparer.Ordinal);

    public SftpConnections(Func<string, RemoteProfile?> profiles, ISftpConnector connector, HostKeyTrust trust, ISecretStore secrets,
        IRemoteInteraction interaction, Ftp.CertificateTrust? certificates = null)
    {
        _profiles = profiles;
        _connector = connector;
        _trust = trust;
        _certificates = certificates;
        _secrets = secrets;
        Interaction = interaction;
        _sweeper = new Timer(_ => CloseIdle(DateTime.UtcNow), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    /// <summary>Closes connections nobody has used for <see cref="IdleTimeout"/>; tabs reconnect when they refresh.</summary>
    internal void CloseIdle(DateTime now)
    {
        foreach (var pool in _pools.Values)
        {
            lock (pool)
            {
                var keep = new List<(ISftpChannel Channel, DateTime Since)>();
                while (pool.Idle.TryPop(out var idle))
                {
                    if (now - idle.Since < IdleTimeout) keep.Add(idle);
                    else
                    {
                        // Remove before closing: a failed close must neither retain this stale entry nor escape the timer.
                        try { idle.Channel.Dispose(); }
                        catch (Exception ex) { AppLog.Warn("Could not close an idle remote connection", ex); }
                    }
                }
                for (int i = keep.Count - 1; i >= 0; i--) pool.Idle.Push(keep[i]);
            }
        }
    }

    public IRemoteInteraction Interaction { get; set; }

    /// <summary>Raised when connecting changed a profile (the user chose to save its secret).</summary>
    public event Action<RemoteProfile>? ProfileChanged;

    public RemoteProfile? Profile(string profileId) => _profiles(profileId);

    /// <summary>The home folder of a server once a connection told it; null before the first connection.</summary>
    public string? HomeOf(string profileId) => _pools.TryGetValue(profileId, out var p) ? p.Home : null;

    public bool IsConnected(string profileId) => _pools.TryGetValue(profileId, out var p) && p.Home is not null && !p.Closed;

    /// <summary>A connection for exclusive use until the lease is disposed; connects (and asks the user) when needed.</summary>
    public SftpLease Lease(string profileId, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var profile = _profiles(profileId) ?? throw new IOException("This connection is not configured any more.");
        var pool = _pools.GetOrAdd(profileId, _ => new Pool(profile.IsFtp ? MaxPerFtpServer : MaxPerServer));
        if (!pool.Slots.Wait(TimeSpan.FromMinutes(2), ct))
            throw new IOException($"All {pool.Size} connections to {profile.Display} are busy; try again when a transfer finishes.");
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ISftpChannel? channel = null;
            long generation;
            lock (pool)
            {
                generation = pool.Generation;
                while (pool.Idle.TryPop(out var idle))
                {
                    if (idle.Channel.IsConnected) { channel = idle.Channel; break; }
                    // A stale channel is already out of the pool; its cleanup cannot prevent a fresh connection.
                    try { idle.Channel.Dispose(); }
                    catch (Exception ex) { AppLog.Warn("Could not close a disconnected remote connection", ex); }
                }
            }
            if (channel is null)
            {
                // One connection attempt at a time per server, so prompts never stack up.
                pool.Connecting.Wait(ct);
                try
                {
                    ObjectDisposedException.ThrowIf(_disposed, this);
                    channel = Connect(profile, ct);
                }
                finally { pool.Connecting.Release(); }
            }
            lock (pool)
            {
                // The owner may have closed while a connection was waiting for a prompt.
                if (_disposed)
                {
                    try { channel.Dispose(); }
                    catch (Exception ex) { AppLog.Warn("Could not close a connection after shutdown", ex); }
                    throw new ObjectDisposedException(nameof(SftpConnections));
                }
                // Disconnect may have happened while this connection was being established. Its lease may finish
                // its current work, but must not reopen the pool or return into a later connection session.
                if (generation == pool.Generation)
                {
                    pool.Home ??= channel.HomeDirectory;
                    pool.Closed = false;
                }
                return new SftpLease(this, pool, channel, generation);
            }
        }
        catch
        {
            pool.Slots.Release();
            throw;
        }
    }

    internal void Return(Pool pool, ISftpChannel channel, bool broken, long generation)
    {
        try
        {
            lock (pool)
            {
                if (!broken && !_disposed && generation == pool.Generation && channel.IsConnected && !pool.Closed)
                    pool.Idle.Push((channel, DateTime.UtcNow));
                else channel.Dispose();
            }
        }
        finally { pool.Slots.Release(); }
    }

    /// <summary>Closes the idle connections of a server; leased ones close when their work returns them.</summary>
    public void Disconnect(string profileId)
    {
        if (!_pools.TryGetValue(profileId, out var pool)) return;
        ExceptionDispatchInfo? failure = null;
        lock (pool)
        {
            pool.Closed = true;
            pool.Generation++;
            while (pool.Idle.TryPop(out var c))
            {
                try { c.Channel.Dispose(); }
                catch (Exception ex)
                {
                    if (failure is null) failure = ExceptionDispatchInfo.Capture(ex);
                    else AppLog.Warn("Could not close another remote connection during disconnect", ex);
                }
            }
        }
        failure?.Throw();
    }

    /// <summary>Forgets a secret typed or saved for the profile (after the user edits or deletes it).</summary>
    public void ForgetSecret(RemoteProfile profile)
    {
        _sessionSecrets.TryRemove(profile.Id, out _);
        try { _secrets.Delete(profile.SecretKey); }
        catch (IOException ex) { AppLog.Warn("Could not remove a saved secret", ex); }
    }

    private ISftpChannel Connect(RemoteProfile profile, CancellationToken ct)
    {
        // Unencrypted FTP is only ever a knowing choice: in the connection dialog, or asked once per session.
        if (profile.Protocol == RemoteProtocols.Ftp && !profile.PlainTextAccepted && !_plainAllowed.ContainsKey(profile.Id))
        {
            if (!Interaction.AllowUnencrypted(profile)) throw new ConnectCanceledException();
            _plainAllowed[profile.Id] = true;
        }
        string? known = _sessionSecrets.GetValueOrDefault(profile.Id);
        if (known is null && profile.SaveSecret)
        {
            try { known = _secrets.Read(profile.SecretKey); }
            catch (IOException ex) { AppLog.Warn("Could not read a saved secret", ex); }
        }
        bool rejectedHostKey = false;
        for (int attempt = 1; ; attempt++)
        {
            bool retry = attempt > 1;
            // A secret the user asked to keep is kept once the server has accepted it: a mistyped one is never stored,
            // and so never tried first on every later connection (where a server that locks accounts would count it).
            string? toSave = null;
            var context = new ConnectContext
            {
                ApproveHostKey = key =>
                {
                    bool ok = ApproveHostKey(profile, key);
                    rejectedHostKey |= !ok;
                    return ok;
                },
                ApproveCertificate = certificate => ApproveCertificate(profile, certificate),
                GetSecret = passphrase =>
                {
                    if (!retry && known is not null) return known;
                    var answer = Interaction.AskSecret(profile, new SecretRequest(passphrase, retry, _secrets.IsPersistent));
                    if (answer is null) throw new ConnectCanceledException();
                    known = answer.Secret;
                    _sessionSecrets[profile.Id] = answer.Secret;
                    toSave = answer.Save ? answer.Secret : null;
                    return answer.Secret;
                },
                AnswerPrompts = (instruction, prompts) =>
                {
                    // A lone hidden prompt (PAM's "Password:") takes the known password without asking again.
                    if (!retry && known is not null && prompts.Count == 1 && !prompts[0].Echo) return [known];
                    return Interaction.AnswerPrompts(profile, instruction, prompts) ?? throw new ConnectCanceledException();
                },
            };
            ISftpChannel channel;
            try
            {
                channel = _connector.Connect(profile, context, ct);
            }
            catch (RemoteAuthenticationException) when (attempt < MaxAuthenticationAttempts)
            {
                // Ask again, a bounded number of times: authentication failures never become a retry storm.
                _sessionSecrets.TryRemove(profile.Id, out _);
                known = null;
                continue;
            }
            catch (IOException) when (rejectedHostKey)
            {
                throw new HostKeyRejectedException($"The host key of {profile.Display} was not accepted, so FileCat did not connect.");
            }
            // Authentication has succeeded. A save/notification failure must neither abandon its channel nor retry
            // authentication: this caller owns the channel until the remaining setup has returned it to the pool.
            try
            {
                if (toSave is not null) SaveSecret(profile, toSave);
                return channel;
            }
            catch
            {
                try { channel.Dispose(); }
                catch (Exception ex) { AppLog.Warn("Could not close a connection after post-connect setup failed", ex); }
                throw;
            }
        }
    }

    /// <summary>Keeps a secret the server has just accepted in the operating system's store, where there is one.</summary>
    private void SaveSecret(RemoteProfile profile, string secret)
    {
        if (!_secrets.IsPersistent) return;
        try
        {
            _secrets.Write(profile.SecretKey, secret, $"FileCat: {profile.Name} ({RemoteProtocols.Describe(profile.Protocol)}, {profile.Display})");
            if (!profile.SaveSecret)
            {
                profile.SaveSecret = true;
                ProfileChanged?.Invoke(profile);
            }
        }
        catch (IOException ex)
        {
            AppLog.Warn("Could not save a secret", ex);
            Interaction.Inform(profile, "The password was not saved, so FileCat keeps it only until it closes. " + ex.Message);
        }
    }

    private bool ApproveCertificate(RemoteProfile profile, Ftp.CertificateInfo certificate)
    {
        var trust = _certificates;
        if (trust is null) return certificate.Problems.Count == 0;
        var check = trust.Check(profile.Host, profile.Port, certificate);
        if (check.Status is Ftp.CertificateStatus.Valid or Ftp.CertificateStatus.Pinned) return true;
        var decision = Interaction.DecideCertificate(profile, check);
        if (decision == HostKeyDecision.AcceptAndRemember) trust.Remember(profile.Host, profile.Port, certificate.Sha256);
        return decision != HostKeyDecision.Reject;
    }

    private bool ApproveHostKey(RemoteProfile profile, byte[] key)
    {
        var check = _trust.Check(profile.Host, profile.Port, key);
        switch (check.Status)
        {
            case HostKeyStatus.Trusted:
                return true;
            case HostKeyStatus.Revoked:
                return false;
        }
        var decision = Interaction.DecideHostKey(profile, check);
        if (decision == HostKeyDecision.AcceptAndRemember) _trust.Remember(profile.Host, profile.Port, key);
        return decision != HostKeyDecision.Reject;
    }

    public void Dispose()
    {
        _disposed = true;
        _sweeper.Dispose();
        ExceptionDispatchInfo? failure = null;
        foreach (var pool in _pools.Values)
        {
            pool.Closed = true;
            lock (pool)
            {
                while (pool.Idle.TryPop(out var c))
                {
                    try { c.Channel.Dispose(); }
                    catch (Exception ex)
                    {
                        if (failure is null) failure = ExceptionDispatchInfo.Capture(ex);
                        else AppLog.Warn("Could not close another connection during shutdown", ex);
                    }
                }
            }
        }
        failure?.Throw();
    }

    internal sealed class Pool(int size)
    {
        public readonly int Size = size;
        public readonly SemaphoreSlim Slots = new(size, size);
        public readonly SemaphoreSlim Connecting = new(1, 1);
        public readonly Stack<(ISftpChannel Channel, DateTime Since)> Idle = new();
        public volatile string? Home;
        public volatile bool Closed;
        // Accessed only under the pool lock; old leases retire even after a newer session has reopened the pool.
        public long Generation;
    }
}

/// <summary>Exclusive use of one connection; dispose to return it (a broken one is closed instead).</summary>
public sealed class SftpLease : IDisposable
{
    private sealed record State(SftpConnections Owner, SftpConnections.Pool Pool, ISftpChannel Channel, long Generation);
    private State? _state;

    internal SftpLease(SftpConnections owner, SftpConnections.Pool pool, ISftpChannel channel, long generation)
    {
        _state = new State(owner, pool, channel, generation);
    }

    public ISftpChannel Channel => Volatile.Read(ref _state)?.Channel ?? throw new ObjectDisposedException(nameof(SftpLease));

    /// <summary>Marks the connection as unusable (a protocol or connection error), so it is closed rather than reused.</summary>
    public bool Broken { get; set; }

    public void Dispose()
    {
        // A held returned lease must not keep the connection owner, its pools or the retired channel alive.
        // Remove the state before returning it, including when channel cleanup fails.
        if (Interlocked.Exchange(ref _state, null) is { } state)
            state.Owner.Return(state.Pool, state.Channel, Broken, state.Generation);
    }
}
