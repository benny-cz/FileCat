using System.Text;
using Avalonia.Headless.XUnit;
using FileCat.App.Services;
using FileCat.Core.Diagnostics;
using FileCat.Core.Platform;
using FileCat.Core.State;
using FileCat.Remote.Ftp;
using FileCat.Remote.Sftp;

namespace FileCat.App.Tests;

/// <summary>
/// Release plan V11: "place unique sentinels in secret values … cause representative errors, and search each default
/// log/export for leakage", and "no plaintext-secret fallback". Passwords go through FileCat's remote stack as the app
/// builds it — the operating system's credential store, FileCat's log (with diagnostic logging on, the most it ever
/// writes), its settings — against real servers, wrongly and rightly; afterwards every file FileCat wrote is searched
/// for them, as UTF-8, UTF-16 and Base64. Throwaway credentials only: FILECAT_REMOTE_LAB, _USER, _PASSWORD.
/// </summary>
public sealed class SecretLeakTests
{
    /// <summary>Answers as a user would: the given passwords in turn, asked to be saved; every key and certificate trusted.</summary>
    private sealed class Answers : IRemoteInteraction
    {
        public readonly Queue<string> Passwords = new();
        public int Asked;

        public SecretAnswer? AskSecret(RemoteProfile profile, SecretRequest request)
        {
            Asked++;
            return Passwords.Count > 0 ? new SecretAnswer(Passwords.Dequeue(), true) : null;
        }

        public IReadOnlyList<string>? AnswerPrompts(RemoteProfile profile, string instruction, IReadOnlyList<(string Prompt, bool Echo)> prompts)
        {
            Asked++;
            if (Passwords.Count == 0) return null;
            string given = Passwords.Dequeue();
            return prompts.Select(_ => given).ToList();
        }

        public HostKeyDecision DecideHostKey(RemoteProfile profile, HostKeyCheck check) => HostKeyDecision.AcceptAndRemember;

        public HostKeyDecision DecideCertificate(RemoteProfile profile, CertificateCheck check) => HostKeyDecision.AcceptAndRemember;

        public bool AllowUnencrypted(RemoteProfile profile) => false;
    }

    [AvaloniaFact]
    public void Passwords_reach_neither_the_log_nor_any_file_FileCat_writes()
    {
        string? host = Environment.GetEnvironmentVariable("FILECAT_REMOTE_LAB");
        string? user = Environment.GetEnvironmentVariable("FILECAT_REMOTE_LAB_USER");
        string? password = Environment.GetEnvironmentVariable("FILECAT_REMOTE_LAB_PASSWORD");
        if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(user) || string.IsNullOrEmpty(password))
        {
            Assert.Skip("Set FILECAT_REMOTE_LAB, FILECAT_REMOTE_LAB_USER and FILECAT_REMOTE_LAB_PASSWORD to a disposable test server.");
            return;
        }
        var log = TestContext.Current.TestOutputHelper;
        var ct = TestContext.Current.CancellationToken;
        string root = Path.Combine(Path.GetTempPath(), "filecat-v11", Guid.NewGuid().ToString("N"));
        // Sentinels: unique strings no file would hold by chance.
        string wrong = "FCV11wrong" + Guid.NewGuid().ToString("N");
        string unreachable = "FCV11closed" + Guid.NewGuid().ToString("N");
        var profiles = new List<RemoteProfile>();
        AppServices? services = null;
        // The platform as the app registers it at start: on Windows that is what puts secrets in Credential Manager.
        var platformBefore = PlatformFactory.WindowsFactory;
        if (OperatingSystem.IsWindows()) PlatformFactory.WindowsFactory = () => new FileCat.Platform.Windows.WindowsPlatform();
        try
        {
            services = AppServices.CreateForPaths(AppPaths.Resolve(overrideRoot: Path.Combine(root, "data")));
            Assert.True(services.Platform.Secrets.IsPersistent || !OperatingSystem.IsWindows(), "On Windows the secrets must go to Credential Manager, as in the app.");
            services.Settings.DiagnosticMode = true;
            AppLog.DiagnosticMode = true;
            var answers = new Answers();
            services.Sftp.Interaction = answers;
            RemoteProfile Add(string name, string protocol, int port)
            {
                var p = new RemoteProfile { Name = name, Host = host, Port = port, User = user, Protocol = protocol };
                services.Settings.RemoteProfiles.Add(p);
                profiles.Add(p);
                return p;
            }
            var sftp = Add("v11 sftp", RemoteProtocols.Sftp, 22);
            var ftpes = Add("v11 ftpes", RemoteProtocols.FtpExplicitTls, 21);
            var closed = Add("v11 closed", RemoteProtocols.Sftp, 2999);
            services.SaveSettings();

            string Try(RemoteProfile profile, params string[] given)
            {
                answers.Passwords.Clear();
                foreach (string g in given) answers.Passwords.Enqueue(g);
                try
                {
                    using var lease = services.Sftp.Lease(profile.Id, ct);
                    return $"listed {lease.Channel.List(".", ct).Count} entries";
                }
                catch (Exception ex) when (ex is not Xunit.Sdk.XunitException)
                {
                    AppLog.Warn($"V11: {profile.Name} failed as intended", ex);
                    return $"{ex.GetType().Name}: {ex.Message}";
                }
            }

            foreach (var profile in new[] { sftp, ftpes })
            {
                // Wrong three times (asked to be saved each time), then given up; then right, and saved.
                log?.WriteLine($"{profile.Name}, wrong password: {Try(profile, wrong, wrong, wrong)}");
                services.Sftp.Disconnect(profile.Id);
                services.Sftp.ForgetSecret(profile);
                log?.WriteLine($"{profile.Name}, right password: {Try(profile, password)}");
                string? saved = services.Platform.Secrets.IsPersistent ? services.Platform.Secrets.Read(profile.SecretKey) : null;
                log?.WriteLine($"{profile.Name}: the operating system's store holds {(saved is null ? "nothing" : saved == password ? "the password" : "something else")}");
            }
            log?.WriteLine($"closed port: {Try(closed, unreachable)}");
            log?.WriteLine($"asked for a secret {answers.Asked} times; the secret store is {(services.Platform.Secrets.IsPersistent ? "the operating system's" : "this session's only")}");
            services.SaveSettings();
            services.Dispose();
            services = null;

            // Every file FileCat wrote, searched for every secret in every form a file could hold it in.
            var secrets = new[] { ("the wrong password", wrong), ("the closed port's password", unreachable), ("the real password", password) };
            var leaks = new List<string>();
            int files = 0;
            foreach (string file in Directory.EnumerateFiles(Path.Combine(root, "data"), "*", SearchOption.AllDirectories))
            {
                files++;
                byte[] bytes;
                try { bytes = File.ReadAllBytes(file); }
                catch (IOException) { continue; }
                foreach (var (what, secret) in secrets)
                {
                    foreach (var (form, needle) in new[]
                    {
                        ("UTF-8", Encoding.UTF8.GetBytes(secret)),
                        ("UTF-16", Encoding.Unicode.GetBytes(secret)),
                        ("Base64", Encoding.ASCII.GetBytes(Convert.ToBase64String(Encoding.UTF8.GetBytes(secret)))),
                    })
                    {
                        if (bytes.AsSpan().IndexOf(needle) >= 0) leaks.Add($"{what} as {form} in {Path.GetRelativePath(root, file)}");
                    }
                }
            }
            log?.WriteLine($"searched {files} files under the data folder");
            foreach (string file in Directory.EnumerateFiles(Path.Combine(root, "data"), "*", SearchOption.AllDirectories))
                log?.WriteLine($"  {Path.GetRelativePath(root, file)} ({new FileInfo(file).Length} bytes)");
            Assert.True(files > 0, "FileCat wrote nothing here: the search would prove nothing.");
            Assert.True(leaks.Count == 0, "A secret was written to disk: " + string.Join("; ", leaks));
        }
        finally
        {
            services?.Dispose();
            // The operating system's store is the user's own: what this test saved there goes.
            var store = PlatformFactory.Create().Secrets;
            foreach (var p in profiles) try { store.Delete(p.SecretKey); } catch (IOException) { }
            PlatformFactory.WindowsFactory = platformBefore;
            AppLog.DiagnosticMode = false;
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }
}
