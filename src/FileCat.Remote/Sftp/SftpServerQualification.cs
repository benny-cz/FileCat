namespace FileCat.Remote.Sftp;

internal static class SftpServerQualification
{
    private const string OpenSsh = "SSH-2.0-OpenSSH_";
    private const string LSetStat = "lsetstat@openssh.com";
    internal const string EditorRefusal = "This SFTP server build is awaiting qualification for permission changes.";

    internal static bool SupportsUnchangedUid(string? banner) => banner == "SSH-2.0-OpenSSH_9.6p1 Ubuntu-3ubuntu13.20";

    // OpenSSH portable V_9_6_P1, sftp-server.c: process_init. The SSH banner describes sshd;
    // the complete versioned extension set also corroborates the SFTP subsystem.
    internal static IReadOnlyDictionary<string, string> PortableOpenSsh96 { get; } =
        new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["posix-rename@openssh.com"] = "1",
            ["statvfs@openssh.com"] = "2",
            ["fstatvfs@openssh.com"] = "2",
            ["hardlink@openssh.com"] = "1",
            ["fsync@openssh.com"] = "1",
            [LSetStat] = "1",
            ["limits@openssh.com"] = "1",
            ["expand-path@openssh.com"] = "1",
            ["copy-data"] = "1",
            ["home-directory"] = "1",
            ["users-groups-by-id@openssh.com"] = "1"
        });

    internal static string? Refusal(string? banner, IReadOnlyDictionary<string, string> extensions)
    {
        if (banner?.Contains("_for_Windows", StringComparison.OrdinalIgnoreCase) == true)
            return "Windows OpenSSH exposes an inspection-only permission view.";
        if (extensions.GetValueOrDefault(LSetStat) != "1")
            return "This server lacks the version 1 no-follow security extension.";
        if (banner is null || !banner.StartsWith(OpenSsh, StringComparison.Ordinal))
            return "This SFTP server has no identified OpenSSH release for permission changes.";
        int comment = banner.IndexOf(' ');
        string release = comment < 0 ? banner[OpenSsh.Length..] : banner[OpenSsh.Length..comment];
        var expected = release switch
        {
            "9.6p1" => PortableOpenSsh96,
            _ => null
        };
        if (expected is null)
            return "This OpenSSH release is awaiting permission-write qualification.";
        return extensions.Count == expected.Count && expected.All(pair => extensions.GetValueOrDefault(pair.Key) == pair.Value)
            ? null : "The advertised SFTP subsystem differs from the qualified OpenSSH release.";
    }
}
