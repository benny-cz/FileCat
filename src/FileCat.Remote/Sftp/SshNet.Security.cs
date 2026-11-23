using FileCat.Core.State;
using Renci.SshNet.Common;
using Renci.SshNet.Sftp;

namespace FileCat.Remote.Sftp;

internal sealed partial class SshNetChannel
{
    internal SftpServerAuthority CaptureAuthority(RemoteProfile profile, long generation)
    {
        var connection = client.ConnectionInfo;
        if (!string.Equals(profile.Host, connection.Host, StringComparison.OrdinalIgnoreCase) || profile.Port != connection.Port ||
            profile.User != connection.Username || profile.Protocol != RemoteProtocols.Sftp)
            throw new IOException("The authenticated SFTP connection differs from the configured profile.");
        return new(profile.Id, connection.Host.ToLowerInvariant(), connection.Port, connection.Username,
            profile.Auth, profile.KeyFile, profile.InitialPath, hostKeyDigest, generation);
    }

    internal RemoteSecurityAttributes ReadExactSecurity(string absolutePath, CancellationToken cancellation) => Wrap(() =>
        ProjectSecurity(client.ReadSecurityLStatAsync(absolutePath, cancellation).GetAwaiter().GetResult(), RemoteSecurityObservation.ExactLStat));

    internal IReadOnlyDictionary<string, string> SecurityExtensions() => Wrap(client.SecurityExtensions);

    internal SftpAccountNames? LookupSecurityNames(uint user, uint group, CancellationToken ct) => Wrap(() =>
        client.LookupSecurityNamesAsync(user, group, ct).GetAwaiter().GetResult());

    internal string? ServerVersion => client.ConnectionInfo.ServerVersion;

    internal StatusCode? WriteReviewedSecurity(string exactPath, SftpSecurityChange change) => Wrap<StatusCode?>(() =>
    {
        try
        {
            client.SetSecurityNoFollowAsync(exactPath, change, CancellationToken.None).GetAwaiter().GetResult();
            return null;
        }
        catch (SftpException error) { return error.StatusCode; }
    });

    private static RemoteSecurityAttributes ProjectSecurity(SftpWireAttributes attributes, RemoteSecurityObservation observation) =>
        new(observation, attributes.Flags, attributes.Size, attributes.UserId, attributes.GroupId, attributes.Permissions, attributes.ModifiedUtc);
}

internal sealed record SftpServerAuthority(string ProfileId, string Host, int Port, string User, string Auth,
    string? KeyFile, string? InitialPath, string HostKeySha256, long Generation)
{
    internal string Display => $"{User}@{Host} · port {Port}";

    internal bool MatchesProfile(RemoteProfile profile) =>
        profile.Id == ProfileId && profile.Protocol == RemoteProtocols.Sftp &&
        string.Equals(profile.Host, Host, StringComparison.OrdinalIgnoreCase) && profile.Port == Port &&
        profile.User == User && profile.Auth == Auth && profile.KeyFile == KeyFile && profile.InitialPath == InitialPath;
}
