using FileCat.Core.Diagnostics;

namespace FileCat.Remote.Ftp;

public enum CertificateStatus
{
    /// <summary>The OS validates it for this host: accepted like a browser would.</summary>
    Valid,
    /// <summary>Not valid for the OS, but the user pinned exactly this certificate for this server.</summary>
    Pinned,
    /// <summary>Not valid for the OS and never seen: the user decides, Cancel by default.</summary>
    Unknown,
    /// <summary>Not valid for the OS and different from the one the user pinned: possibly an impostor.</summary>
    Changed,
}

public sealed record CertificateCheck(string Host, int Port, CertificateInfo Certificate, CertificateStatus Status, string? PinnedSha256);

/// <summary>
/// FTPS certificates the user chose to trust although the OS does not (self-signed NAS and home servers), pinned by
/// SHA-256 per host and port in a small text file beside FileCat's state. A certificate the OS validates needs no pin.
/// </summary>
public sealed class CertificateTrust(string path)
{
    private readonly object _lock = new();

    public CertificateCheck Check(string host, int port, CertificateInfo certificate)
    {
        if (certificate.Problems.Count == 0) return new CertificateCheck(host, port, certificate, CertificateStatus.Valid, null);
        var pinned = Pins().Where(p => p.Key == Key(host, port)).Select(p => p.Sha256).ToList();
        if (pinned.Contains(certificate.Sha256, StringComparer.OrdinalIgnoreCase)) return new CertificateCheck(host, port, certificate, CertificateStatus.Pinned, certificate.Sha256);
        return new CertificateCheck(host, port, certificate, pinned.Count > 0 ? CertificateStatus.Changed : CertificateStatus.Unknown, pinned.FirstOrDefault());
    }

    /// <summary>Pins the certificate for the server, replacing any earlier pin.</summary>
    public void Remember(string host, int port, string sha256)
    {
        lock (_lock)
        {
            var lines = Pins().Where(p => p.Key != Key(host, port)).Select(p => $"{p.Key} {p.Sha256}").Append($"{Key(host, port)} {sha256}").ToList();
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var temp = path + ".tmp";
                File.WriteAllLines(temp, ["# FTPS certificates trusted in FileCat: host:port SHA-256", .. lines]);
                File.Move(temp, path, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppLog.Warn("Could not save a trusted certificate", ex);
            }
        }
    }

    private static string Key(string host, int port) => $"{host.ToLowerInvariant()}:{port}";

    private List<(string Key, string Sha256)> Pins()
    {
        lock (_lock)
        {
            try
            {
                if (!File.Exists(path)) return [];
                return File.ReadAllLines(path)
                    .Where(l => l.Length > 0 && !l.StartsWith('#'))
                    .Select(l => l.Split(' ', 2, StringSplitOptions.TrimEntries))
                    .Where(p => p.Length == 2 && p[1].Length == 64)
                    .Select(p => (p[0].ToLowerInvariant(), p[1]))
                    .ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppLog.Warn("Could not read trusted certificates", ex);
                return [];
            }
        }
    }
}
