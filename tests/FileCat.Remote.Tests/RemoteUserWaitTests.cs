using System.Reflection;
using FileCat.Core.State;
using FileCat.Core.Threading;
using FileCat.Remote.Sftp;

namespace FileCat.Remote.Tests;

/// <summary>
/// I684: a listing that connects waits for the person (the server's key, a password, the system's keychain) and not for
/// the server, so the server is not reported as not responding meanwhile.
/// </summary>
public sealed class RemoteUserWaitTests : IDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "filecat-remote-tests", Guid.NewGuid().ToString("N"));

    public RemoteUserWaitTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { }
    }

    // A scheduler whose watchdog checkpoints are the test's own.
    private static DeviceIoScheduler ManualScheduler(TimeSpan threshold, int threads)
    {
        var io = new DeviceIoScheduler(threshold, threadsPerDevice: threads, maxThreadsPerDevice: threads + 1);
        ((Timer)typeof(DeviceIoScheduler).GetField("_watchdog", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(io)!)
            .Change(Timeout.Infinite, Timeout.Infinite);
        return io;
    }

    private static void Watch(DeviceIoScheduler io) =>
        typeof(DeviceIoScheduler).GetMethod("Watch", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(io, null);

    [Fact]
    public async Task Each_connection_question_and_the_systems_secret_store_wait_for_the_person()
    {
        var server = new FakeSftpServer();
        var profile = new RemoteProfile { Name = "slow answers", Host = "answers.example", User = "user", SaveSecret = true };
        var ui = new ObservingInteraction(server.Password);
        var secrets = new ObservingSecrets();
        using var connections = new SftpConnections(id => id == profile.Id ? profile : null, new FakeConnector(server),
            new HostKeyTrust(Path.Combine(_dir, "known_hosts"), []), secrets, ui);
        using var io = ManualScheduler(TimeSpan.FromMilliseconds(1), threads: 1);
        var during = await io.Run("sftp://answers.example:22", IoPriority.Interactive, ct =>
        {
            bool before = DeviceIoScheduler.IsAwaitingUser;
            // As the provider's listing does: the device call waits while a pool thread connects and asks.
            using var lease = Task.Run(() => connections.Lease(profile.Id, ct), ct).GetAwaiter().GetResult();
            return (before, after: DeviceIoScheduler.IsAwaitingUser);
        }, TestContext.Current.CancellationToken).WaitAsync(Patience, TestContext.Current.CancellationToken);

        Assert.False(during.before);
        Assert.False(during.after);
        Assert.Equal(["host key: True", "secret: True"], ui.Asked);
        Assert.Equal(["read: True", "write: True"], secrets.Calls);
    }

    [Fact]
    public async Task A_call_waiting_for_another_connection_attempt_that_asks_the_person_is_not_reported()
    {
        var server = new FakeSftpServer();
        var profile = new RemoteProfile { Name = "one prompt", Host = "prompt.example", User = "user" };
        using var answer = new ManualResetEventSlim();
        var asking = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ui = new ObservingInteraction(server.Password, beforeSecret: () => { asking.TrySetResult(); answer.Wait(Patience); });
        using var connections = new SftpConnections(id => id == profile.Id ? profile : null, new FakeConnector(server),
            new HostKeyTrust(Path.Combine(_dir, "known_hosts"), []), new SessionSecretStore(), ui);
        const string device = "sftp://prompt.example:22";
        using var io = ManualScheduler(TimeSpan.FromMilliseconds(500), threads: 2);
        var leases = new List<Task>();
        try
        {
            Task Lease() => io.Run(device, IoPriority.Interactive, ct =>
            {
                using var lease = Task.Run(() => connections.Lease(profile.Id, ct), ct).GetAwaiter().GetResult();
            }, TestContext.Current.CancellationToken);
            leases.Add(Lease());
            await asking.Task.WaitAsync(Patience, TestContext.Current.CancellationToken);
            leases.Add(Lease()); // finds no idle connection and waits for the first attempt, which waits for the person
            await Task.Delay(1000, TestContext.Current.CancellationToken);
            Watch(io);
            Assert.Equal(DeviceHealth.Responsive, io.GetHealth(device));
            Assert.All(leases, lease => Assert.False(lease.IsCompleted));
        }
        finally
        {
            answer.Set();
            await Task.WhenAll(leases).WaitAsync(Patience, TestContext.Current.CancellationToken);
        }
        Assert.Equal(2, server.Connects);
    }

    private sealed class ObservingInteraction(string password, Action? beforeSecret = null) : IRemoteInteraction
    {
        public List<string> Asked { get; } = [];

        public HostKeyDecision DecideHostKey(RemoteProfile profile, HostKeyCheck check)
        {
            lock (Asked) Asked.Add("host key: " + DeviceIoScheduler.IsAwaitingUser);
            return HostKeyDecision.AcceptAndRemember;
        }

        public SecretAnswer? AskSecret(RemoteProfile profile, SecretRequest request)
        {
            lock (Asked) Asked.Add("secret: " + DeviceIoScheduler.IsAwaitingUser);
            beforeSecret?.Invoke();
            return new SecretAnswer(password, Save: true);
        }

        public IReadOnlyList<string>? AnswerPrompts(RemoteProfile profile, string instruction, IReadOnlyList<(string Prompt, bool Echo)> prompts)
        {
            lock (Asked) Asked.Add("prompts: " + DeviceIoScheduler.IsAwaitingUser);
            return [password];
        }
    }

    private sealed class ObservingSecrets : ISecretStore
    {
        public List<string> Calls { get; } = [];
        public bool IsPersistent => true;

        public string? Read(string key)
        {
            lock (Calls) Calls.Add("read: " + DeviceIoScheduler.IsAwaitingUser);
            return null;
        }

        public void Write(string key, string secret, string? label = null)
        {
            lock (Calls) Calls.Add("write: " + DeviceIoScheduler.IsAwaitingUser);
        }

        public void Delete(string key) { }
    }
}
