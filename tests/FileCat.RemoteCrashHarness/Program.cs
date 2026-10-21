using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using FileCat.Core.State;
using FileCat.Remote.Ftp;
using FileCat.Remote.Sftp;

// Started and killed only by FtpJobTests against its loopback server. The parent owns the
// source, server, and journal roots; this process must never delete or reconcile them.
if (args.Length != 4 || !int.TryParse(args[0], out int port) || port is < 1 or > 65535 ||
    !Path.IsPathFullyQualified(args[1]) || !Path.IsPathFullyQualified(args[2]) ||
    args[3] is not ("copy" or "move") || !File.Exists(args[1]))
    return 2;

var profile = new RemoteProfile
{
    Name = "crash-test", Host = "127.0.0.1", Port = port, User = "tester",
    Protocol = RemoteProtocols.Ftp, PlainTextAccepted = true, Temporary = true,
};
using var connections = new SftpConnections(id => id == profile.Id ? profile : null,
    new ProtocolConnector(new SshNetConnector(), new FluentFtpConnector()),
    new HostKeyTrust(Path.Combine(args[2], "known-hosts")), new SessionSecretStore(),
    new LoopbackInteraction());
var providers = new ProviderRegistry();
providers.Register(new LocalFileSystemProvider());
providers.Register(new SftpProvider(connections, () => [profile], p => p));
SftpJobs.Register();
var manager = new JobManager(new PortableFileOperations(), providers, args[2]);
var job = manager.Submit(new JobRequest
{
    Kind = args[3] == "move" ? JobKind.Move : JobKind.Copy,
    Sources = [ItemRef.ForFileSystemPath(args[1], EntryKind.File)],
    Destination = SftpProvider.At(profile, "/"),
    Options = new TransferOptions { RateLimit = 64 * 1024 },
});
while (!job.State.IsFinished()) await Task.Delay(25);
Console.Error.WriteLine($"Upload ended before interruption: {job.State}; {string.Join("; ", job.Issues.Select(i => i.Message))}");
return 1;

file sealed class LoopbackInteraction : IRemoteInteraction
{
    public HostKeyDecision DecideHostKey(RemoteProfile profile, HostKeyCheck check) => HostKeyDecision.Reject;
    public SecretAnswer? AskSecret(RemoteProfile profile, SecretRequest request) => new("secret-pass", false);
    public IReadOnlyList<string>? AnswerPrompts(RemoteProfile profile, string instruction,
        IReadOnlyList<(string Prompt, bool Echo)> prompts) => null;
}
