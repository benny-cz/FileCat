using System.Text;
using FileCat.Core.Resources;
using FileCat.Core.State;
using FileCat.Remote.Sftp;

namespace FileCat.Remote.Tests;

public sealed class SftpProviderTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "filecat-remote-tests", Guid.NewGuid().ToString("N"));
    private readonly FakeSftpServer _server = new();
    private readonly ScriptedInteraction _ui = new();
    private readonly List<RemoteProfile> _profiles = [];
    private readonly PersistentSecrets _secrets = new();
    private readonly SftpConnections _connections;
    private readonly SftpProvider _provider;
    private readonly RemoteProfile _profile;

    public SftpProviderTests()
    {
        Directory.CreateDirectory(_dir);
        _connections = new SftpConnections(id => _profiles.FirstOrDefault(p => p.Id == id), new FakeConnector(_server),
            new HostKeyTrust(Path.Combine(_dir, "known_hosts"), []), _secrets, _ui);
        _provider = new SftpProvider(_connections, () => _profiles.Where(p => !p.Temporary).ToList(), p =>
        {
            _profiles.Add(p);
            return p;
        });
        _profile = new RemoteProfile { Name = "Test", Host = "files.example", User = "user" };
        _profiles.Add(_profile);
    }

    public void Dispose()
    {
        _connections.Dispose();
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { }
    }

    /// <summary>Stands in for an OS credential store.</summary>
    private sealed class PersistentSecrets : ISecretStore
    {
        private readonly Dictionary<string, string> _values = [];
        public bool IsPersistent => true;
        public string? Read(string key) => _values.GetValueOrDefault(key);
        public void Write(string key, string secret, string? label = null) => _values[key] = secret;
        public void Delete(string key) => _values.Remove(key);
    }

    private sealed class Sink : IEnumerationSink
    {
        public List<EntryData> Entries { get; } = [];
        public List<string> Issues { get; } = [];
        public void AddBatch(ReadOnlySpan<EntryData> entries) => Entries.AddRange(entries.ToArray());
        public void ReportIssue(string message) => Issues.Add(message);
    }

    private async Task<Sink> ListAsync(Location location)
    {
        var sink = new Sink();
        await _provider.EnumerateAsync(location, sink, TestContext.Current.CancellationToken);
        return sink;
    }

    [Fact]
    public async Task First_contact_asks_about_the_host_key_and_the_password_once()
    {
        _ui.Secrets.Enqueue("wrong");
        _ui.Secrets.Enqueue("secret");
        _server.File("/home/user/a.txt", "alpha");
        var home = SftpProvider.At(_profile);
        var sink = await ListAsync(home);
        Assert.Equal(["a.txt"], sink.Entries.Select(e => e.Name));
        var question = Assert.Single(_ui.HostKeyQuestions);
        Assert.Equal(HostKeyStatus.Unknown, question.Status);
        Assert.Equal(HostKeyTrust.Fingerprint(_server.HostKey), question.Fingerprint);
        Assert.Equal([false, true], _ui.SecretQuestions.Select(q => q.Retry));

        // The remembered key and the session's password make the next connection silent.
        _connections.Disconnect(_profile.Id);
        await ListAsync(home);
        Assert.Single(_ui.HostKeyQuestions);
        Assert.Equal(2, _ui.SecretQuestions.Count);
        Assert.Equal("/home/user", _connections.HomeOf(_profile.Id));
        Assert.Equal("sftp://user@files.example/home/user", _provider.GetDisplayPath(home));
    }

    [Fact]
    public async Task A_changed_or_rejected_host_key_stops_the_connection()
    {
        _ui.HostKeyAnswer = HostKeyDecision.Reject;
        await Assert.ThrowsAsync<HostKeyRejectedException>(() => ListAsync(SftpProvider.At(_profile)));
        Assert.Empty(_ui.SecretQuestions);

        _ui.HostKeyAnswer = HostKeyDecision.AcceptAndRemember;
        _ui.Secrets.Enqueue("secret");
        await ListAsync(SftpProvider.At(_profile));
        _connections.Disconnect(_profile.Id);
        _server.HostKey = FakeSftpServer.KeyBlob("ssh-ed25519", 9);
        _ui.HostKeyAnswer = HostKeyDecision.Reject;
        await Assert.ThrowsAsync<HostKeyRejectedException>(() => ListAsync(SftpProvider.At(_profile)));
        Assert.Equal(HostKeyStatus.Changed, _ui.HostKeyQuestions[^1].Status);
    }

    [Fact]
    public async Task Authentication_retries_are_bounded_and_a_cancel_stops_at_once()
    {
        foreach (var s in new[] { "a", "b", "c", "d" }) _ui.Secrets.Enqueue(s);
        await Assert.ThrowsAsync<RemoteAuthenticationException>(() => ListAsync(SftpProvider.At(_profile)));
        Assert.Equal(3, _ui.SecretQuestions.Count);
        _ui.Secrets.Clear();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ListAsync(SftpProvider.At(_profile)));
    }

    [Fact]
    public async Task A_saved_secret_is_used_without_asking()
    {
        _ui.Secrets.Enqueue("secret");
        _ui.SaveSecret = true;
        RemoteProfile? changed = null;
        _connections.ProfileChanged += p => changed = p;
        await ListAsync(SftpProvider.At(_profile));
        Assert.True(_profile.SaveSecret);
        Assert.Same(_profile, changed);
        Assert.Equal("secret", _secrets.Read(_profile.SecretKey));

        // A new session (fresh pool) reads the saved secret instead of asking.
        using var again = new SftpConnections(id => _profiles.FirstOrDefault(p => p.Id == id), new FakeConnector(_server),
            new HostKeyTrust(Path.Combine(_dir, "known_hosts"), []), _secrets, _ui);
        using (again.Lease(_profile.Id, TestContext.Current.CancellationToken)) { }
        Assert.Single(_ui.SecretQuestions);
    }

    /// <summary>
    /// A password the user asks to keep is kept once the server has accepted it (E-V11-S1): a mistyped one is never
    /// stored, so it is not tried first on every later connection — on a server that locks an account after a few
    /// failed logins, each of those would count.
    /// </summary>
    [Fact]
    public async Task A_secret_is_saved_only_once_the_server_has_accepted_it()
    {
        _ui.SaveSecret = true;
        foreach (var s in new[] { "mistyped", "wrong again", "still wrong" }) _ui.Secrets.Enqueue(s);
        await Assert.ThrowsAsync<RemoteAuthenticationException>(() => ListAsync(SftpProvider.At(_profile)));
        Assert.Null(_secrets.Read(_profile.SecretKey));
        Assert.False(_profile.SaveSecret);

        // Wrong once more, then right: only the one the server took is kept.
        _ui.Secrets.Enqueue("mistyped");
        _ui.Secrets.Enqueue("secret");
        await ListAsync(SftpProvider.At(_profile));
        Assert.Equal("secret", _secrets.Read(_profile.SecretKey));
        Assert.True(_profile.SaveSecret);
    }

    [Fact]
    public async Task Listings_show_links_as_what_they_point_to_and_skip_unsafe_names()
    {
        _ui.Secrets.Enqueue("secret");
        _server.Dir("/data/sub");
        _server.File("/data/file.txt", "12345");
        _server.File("/data/.hidden", "h");
        _server.Link("/data/to-sub", "sub");
        _server.Link("/data/to-file", "/data/file.txt");
        _server.Link("/data/dangling", "/nowhere");
        _server.Dir("/data").Children["bad/name"] = new FakeSftpServer.Node { Name = "bad/name" };
        var data = SftpProvider.At(_profile, "/data");
        var sink = await ListAsync(data);
        var byName = sink.Entries.ToDictionary(e => e.Name);
        Assert.Equal(EntryKind.Directory, byName["sub"].Kind);
        Assert.Equal(EntryKind.Directory, byName["to-sub"].Kind);
        Assert.True(byName["to-sub"].Has(EntryFlags.Link));
        Assert.Equal((EntryKind.File, 5L), (byName["to-file"].Kind, byName["to-file"].Size));
        Assert.True(byName["dangling"].Has(EntryFlags.Unavailable));
        Assert.True(byName[".hidden"].Has(EntryFlags.Hidden));
        Assert.False(byName.ContainsKey("bad/name"));
        Assert.Single(sink.Issues);

        var sub = _provider.GetChildLocation(data, byName["to-sub"])!;
        Assert.Equal("/data/to-sub", sub.Path);
        Assert.Equal("/data", _provider.GetParent(sub)!.Path);
        Assert.Null(_provider.GetParent(SftpProvider.At(_profile, "/")));
        Assert.Null(_provider.GetChildLocation(data, byName["file.txt"]));
    }

    [Fact]
    public async Task Content_reads_at_any_offset_and_downloads_carry_an_origin_mark()
    {
        _ui.Secrets.Enqueue("secret");
        _server.File("/data/big.bin", string.Concat(Enumerable.Range(0, 1000).Select(i => (char)('a' + i % 26))));
        var data = SftpProvider.At(_profile, "/data");
        await ListAsync(data);
        using (var content = _provider.OpenContent(new ItemRef(data, "big.bin", EntryKind.File))!)
        {
            Assert.Equal(1000, content.Length);
            var buffer = new byte[4];
            Assert.Equal(4, content.Read(27, buffer));
            Assert.Equal("bcde", Encoding.ASCII.GetString(buffer));
            Assert.Equal(4, content.Read(0, buffer));
            Assert.Equal("abcd", Encoding.ASCII.GetString(buffer));
        }
        Assert.Equal("[ZoneTransfer]\r\nZoneId=3\r\nHostUrl=sftp://files.example/\r\n", _provider.GetOriginMark(data));
        Assert.Equal("sftp://files.example:22", _provider.GetDeviceKey(data));
    }

    [Fact]
    public void Addresses_reuse_saved_connections_or_open_session_only_ones()
    {
        Assert.True(_provider.TryParse("sftp://user@files.example/srv/www", null, out var saved));
        Assert.Equal((_profile.Id, "/srv/www"), (saved!.Session, saved.Path));
        Assert.True(_provider.TryParse("sftp://FILES.example", null, out var home));
        Assert.Equal((_profile.Id, SftpProvider.Home), (home!.Session, home.Path));
        Assert.True(_provider.TryParse("sftp://bob:ignored@other.example:2222/~/project", null, out var typed));
        var temp = _profiles.Single(p => p.Temporary);
        Assert.Equal(("other.example", 2222, "bob", "~/project"), (temp.Host, temp.Port, temp.User, typed!.Path));
        Assert.Equal(temp.Id, typed.Session);
        Assert.False(_provider.TryParse("C:\\data", null, out _));
    }

    [Fact]
    public void Idle_connections_close_and_the_next_use_reconnects()
    {
        _ui.Secrets.Enqueue("secret");
        var ct = TestContext.Current.CancellationToken;
        using (_connections.Lease(_profile.Id, ct)) { }
        Assert.Equal(1, _server.ActiveChannels);
        _connections.CloseIdle(DateTime.UtcNow + TimeSpan.FromMinutes(5));
        Assert.Equal(1, _server.ActiveChannels);
        _connections.CloseIdle(DateTime.UtcNow + SftpConnections.IdleTimeout + TimeSpan.FromMinutes(1));
        Assert.Equal(0, _server.ActiveChannels);
        using (_connections.Lease(_profile.Id, ct)) { }
        Assert.Equal(2, _server.Connects);
        // The password typed earlier in the session is reused: no second question.
        Assert.Single(_ui.SecretQuestions);
    }

    private sealed class DeferringInteraction : IRemoteInteraction
    {
        public HostKeyDecision DecideHostKey(RemoteProfile profile, HostKeyCheck check) => throw new PromptDeferredException("Press Ctrl+R to connect.");
        public SecretAnswer? AskSecret(RemoteProfile profile, SecretRequest request) => throw new PromptDeferredException("Press Ctrl+R to connect.");
        public IReadOnlyList<string>? AnswerPrompts(RemoteProfile profile, string instruction, IReadOnlyList<(string Prompt, bool Echo)> prompts) =>
            throw new PromptDeferredException("Press Ctrl+R to connect.");
    }

    [Fact]
    public async Task A_deferred_question_becomes_an_error_the_panel_can_show()
    {
        _connections.Interaction = new DeferringInteraction();
        var error = await Assert.ThrowsAsync<PromptDeferredException>(() => ListAsync(SftpProvider.At(_profile)));
        Assert.Contains("Ctrl+R", error.Message);
        Assert.Equal(0, _server.Connects);
    }

    [Fact]
    public async Task Leases_reuse_connections_and_close_broken_ones()
    {
        _ui.Secrets.Enqueue("secret");
        var ct = TestContext.Current.CancellationToken;
        using (var first = _connections.Lease(_profile.Id, ct))
        using (var second = _connections.Lease(_profile.Id, ct))
        {
            Assert.NotSame(first.Channel, second.Channel);
        }
        Assert.Equal(2, _server.Connects);
        using (_connections.Lease(_profile.Id, ct)) { }
        Assert.Equal(2, _server.Connects);
        using (var broken = _connections.Lease(_profile.Id, ct)) broken.Broken = true;
        Assert.Equal(1, _server.ActiveChannels);
        _connections.Disconnect(_profile.Id);
        Assert.Equal(0, _server.ActiveChannels);
        await Task.CompletedTask;
    }
}
