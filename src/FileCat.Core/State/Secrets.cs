using System.Collections.Concurrent;

namespace FileCat.Core.State;

/// <summary>
/// Secrets (passwords, key passphrases) by reference (plan §14.1): an OS credential store where one exists, never
/// settings files, logs, or workspaces.
/// </summary>
public interface ISecretStore
{
    /// <summary>True when secrets survive restarts (an OS credential store); false for the session-only fallback.</summary>
    bool IsPersistent { get; }

    string? Read(string key);

    void Write(string key, string secret);

    void Delete(string key);
}

/// <summary>The fallback without an OS store: secrets live in memory until FileCat exits (no plaintext persistence).</summary>
public sealed class SessionSecretStore : ISecretStore
{
    private readonly ConcurrentDictionary<string, string> _secrets = new(StringComparer.Ordinal);

    public bool IsPersistent => false;

    public string? Read(string key) => _secrets.TryGetValue(key, out var s) ? s : null;

    public void Write(string key, string secret) => _secrets[key] = secret;

    public void Delete(string key) => _secrets.TryRemove(key, out _);
}

/// <summary>A saved server connection. It holds no secret: a stored password or passphrase is referenced by <see cref="Id"/>.</summary>
public sealed class RemoteProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 22;
    /// <summary>"sftp" (default), "ftpes" (FTP with explicit TLS), "ftps" (implicit TLS), or "ftp" (unencrypted).</summary>
    public string Protocol { get; set; } = RemoteProtocols.Sftp;
    /// <summary>The user chose unencrypted FTP knowingly (in the connection dialog), so connecting does not ask again.</summary>
    public bool PlainTextAccepted { get; set; }
    public string User { get; set; } = string.Empty;
    /// <summary>"password", "key", or "keyboard-interactive".</summary>
    public string Auth { get; set; } = RemoteAuth.Password;
    /// <summary>Private key file for <see cref="RemoteAuth.Key"/> (OpenSSH, PuTTY, or PKCS#8 format).</summary>
    public string? KeyFile { get; set; }
    /// <summary>Folder opened after connecting; empty for the server's home folder.</summary>
    public string? InitialPath { get; set; }
    /// <summary>Whether the user chose to keep the password or passphrase in the OS secret store.</summary>
    public bool SaveSecret { get; set; }

    /// <summary>Not saved: connections typed as sftp:// addresses live for the session only.</summary>
    public bool Temporary { get; set; }

    public string SecretKey => "FileCat/sftp/" + Id;

    public string Display => (User.Length > 0 ? User + "@" : "") + Host + (Port == RemoteProtocols.DefaultPort(Protocol) ? "" : ":" + Port);

    public bool IsFtp => RemoteProtocols.IsFtp(Protocol);
}

public static class RemoteProtocols
{
    public const string Sftp = "sftp";
    /// <summary>FTP upgraded to TLS with AUTH TLS (FileZilla and WinSCP call this ftpes://).</summary>
    public const string FtpExplicitTls = "ftpes";
    /// <summary>FTP inside TLS from the first byte, usually on port 990 (ftps://).</summary>
    public const string FtpImplicitTls = "ftps";
    /// <summary>Unencrypted FTP: passwords and files travel in the clear, so it is only ever an explicit choice.</summary>
    public const string Ftp = "ftp";

    public static bool IsFtp(string protocol) => protocol is FtpExplicitTls or FtpImplicitTls or Ftp;

    public static int DefaultPort(string protocol) => protocol switch { FtpImplicitTls => 990, FtpExplicitTls or Ftp => 21, _ => 22 };

    /// <summary>What users read: "SFTP", "FTPS (explicit TLS)", "FTPS (implicit TLS)", "FTP (unencrypted)".</summary>
    public static string Describe(string protocol) => protocol switch
    {
        FtpExplicitTls => "FTPS (explicit TLS)",
        FtpImplicitTls => "FTPS (implicit TLS)",
        Ftp => "FTP (unencrypted)",
        _ => "SFTP",
    };
}

public static class RemoteAuth
{
    public const string Password = "password";
    public const string Key = "key";
    public const string KeyboardInteractive = "keyboard-interactive";
}
