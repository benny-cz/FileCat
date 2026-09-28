using System.Collections.Concurrent;
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
}

/// <summary>The callbacks a connector uses while it connects one channel.</summary>
public sealed class ConnectContext
{
    /// <summary>The server's host key blob; true lets the connection continue.</summary>
    public required Func<byte[], bool> ApproveHostKey { get; init; }

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
    /// <summary>Unused connections close after this long (plan §14.1: close idle sessions).</summary>
    public static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(10);
    private const int MaxAuthenticationAttempts = 3;
    private readonly Timer _sweeper;

    private readonly Func<string, RemoteProfile?> _profiles;
    private readonly ISftpConnector _connector;
    private readonly HostKeyTrust _trust;
    private readonly ISecretStore _secrets;
    private readonly ConcurrentDictionary<string, Pool> _pools = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _sessionSecrets = new(StringComparer.Ordinal);

    public SftpConnections(Func<string, RemoteProfile?> profiles, ISftpConnector connector, HostKeyTrust trust, ISecretStore secrets,
        IRemoteInteraction interaction)
    {
        _profiles = profiles;
        _connector = connector;
        _trust = trust;
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
                var keep = pool.Idle.Where(i => now - i.Since < IdleTimeout).Reverse().ToList();
                foreach (var stale in pool.Idle.Where(i => now - i.Since >= IdleTimeout)) stale.Channel.Dispose();
                pool.Idle.Clear();
                foreach (var i in keep) pool.Idle.Push(i);
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
        var profile = _profiles(profileId) ?? throw new IOException("This connection is not configured any more.");
        var pool = _pools.GetOrAdd(profileId, _ => new Pool());
        if (!pool.Slots.Wait(TimeSpan.FromMinutes(2), ct))
            throw new IOException($"All {MaxPerServer} connections to {profile.Display} are busy; try again when a transfer finishes.");
        try
        {
            ISftpChannel? channel = null;
            lock (pool)
            {
                while (pool.Idle.TryPop(out var idle))
                {
                    if (idle.Channel.IsConnected) { channel = idle.Channel; break; }
                    idle.Channel.Dispose();
                }
            }
            if (channel is null)
            {
                // One connection attempt at a time per server, so prompts never stack up.
                pool.Connecting.Wait(ct);
                try { channel = Connect(profile, ct); }
                finally { pool.Connecting.Release(); }
            }
            pool.Home ??= channel.HomeDirectory;
            pool.Closed = false;
            return new SftpLease(this, pool, channel);
        }
        catch
        {
            pool.Slots.Release();
            throw;
        }
    }

    internal void Return(Pool pool, ISftpChannel channel, bool broken)
    {
        if (!broken && channel.IsConnected && !pool.Closed)
        {
            lock (pool) pool.Idle.Push((channel, DateTime.UtcNow));
        }
        else channel.Dispose();
        pool.Slots.Release();
    }

    /// <summary>Closes the idle connections of a server; leased ones close when their work returns them.</summary>
    public void Disconnect(string profileId)
    {
        if (!_pools.TryGetValue(profileId, out var pool)) return;
        pool.Closed = true;
        lock (pool)
        {
            while (pool.Idle.TryPop(out var c)) c.Channel.Dispose();
        }
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
            var context = new ConnectContext
            {
                ApproveHostKey = key =>
                {
                    bool ok = ApproveHostKey(profile, key);
                    rejectedHostKey |= !ok;
                    return ok;
                },
                GetSecret = passphrase =>
                {
                    if (!retry && known is not null) return known;
                    var answer = Interaction.AskSecret(profile, new SecretRequest(passphrase, retry, _secrets.IsPersistent));
                    if (answer is null) throw new ConnectCanceledException();
                    known = answer.Secret;
                    _sessionSecrets[profile.Id] = answer.Secret;
                    if (answer.Save && _secrets.IsPersistent)
                    {
                        try
                        {
                            _secrets.Write(profile.SecretKey, answer.Secret);
                            if (!profile.SaveSecret)
                            {
                                profile.SaveSecret = true;
                                ProfileChanged?.Invoke(profile);
                            }
                        }
                        catch (IOException ex) { AppLog.Warn("Could not save a secret", ex); }
                    }
                    return answer.Secret;
                },
                AnswerPrompts = (instruction, prompts) =>
                {
                    // A lone hidden prompt (PAM's "Password:") takes the known password without asking again.
                    if (!retry && known is not null && prompts.Count == 1 && !prompts[0].Echo) return [known];
                    return Interaction.AnswerPrompts(profile, instruction, prompts) ?? throw new ConnectCanceledException();
                },
            };
            try
            {
                return _connector.Connect(profile, context, ct);
            }
            catch (RemoteAuthenticationException) when (attempt < MaxAuthenticationAttempts)
            {
                // Ask again, a bounded number of times: authentication failures never become a retry storm.
                _sessionSecrets.TryRemove(profile.Id, out _);
                known = null;
            }
            catch (IOException) when (rejectedHostKey)
            {
                throw new HostKeyRejectedException($"The host key of {profile.Display} was not accepted, so FileCat did not connect.");
            }
        }
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
        _sweeper.Dispose();
        foreach (var pool in _pools.Values)
        {
            pool.Closed = true;
            lock (pool)
            {
                while (pool.Idle.TryPop(out var c)) c.Channel.Dispose();
            }
        }
    }

    internal sealed class Pool
    {
        public readonly SemaphoreSlim Slots = new(MaxPerServer, MaxPerServer);
        public readonly SemaphoreSlim Connecting = new(1, 1);
        public readonly Stack<(ISftpChannel Channel, DateTime Since)> Idle = new();
        public volatile string? Home;
        public volatile bool Closed;
    }
}

/// <summary>Exclusive use of one connection; dispose to return it (a broken one is closed instead).</summary>
public sealed class SftpLease : IDisposable
{
    private readonly SftpConnections _owner;
    private readonly SftpConnections.Pool _pool;
    private int _returned;

    internal SftpLease(SftpConnections owner, SftpConnections.Pool pool, ISftpChannel channel)
    {
        _owner = owner;
        _pool = pool;
        Channel = channel;
    }

    public ISftpChannel Channel { get; }

    /// <summary>Marks the connection as unusable (a protocol or connection error), so it is closed rather than reused.</summary>
    public bool Broken { get; set; }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _returned, 1) == 0) _owner.Return(_pool, Channel, Broken);
    }
}
