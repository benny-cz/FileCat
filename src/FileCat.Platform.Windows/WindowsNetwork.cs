using System.ComponentModel;
using System.Runtime.InteropServices;
using FileCat.Core.FileSystem;
using FileCat.Core.Resources;
using static FileCat.Platform.Windows.Native.NativeMethods;

namespace FileCat.Platform.Windows;

public sealed record ShareTag(string Server, string Share, string? Remark, bool IsSpecial);

/// <summary>
/// Share listing for <c>\\server</c> roots (which cannot be enumerated as directories), credential prompts
/// through the Windows networking UI, and connect/disconnect network drive dialogs (plan §8.2, NET-004).
/// </summary>
public sealed class NetworkShareProvider : ResourceProvider
{
    public override string Scheme => Schemes.Network;

    public override string GetDisplayPath(Location location) => location.Path;

    public override string GetDisplayName(Location location) => location.Path.TrimStart('\\');

    public override Location? GetParent(Location location) => new Location(Schemes.Computer, string.Empty);

    public override string? GetNameInParent(Location location) => null;

    public override string GetDeviceKey(Location location) => PathUtil.GetDeviceKey(location.Path);

    public override LocationCapabilities GetCapabilities(Location location) => LocationCapabilities.Enumerate;

    public override bool TryParse(string text, Location? current, out Location? location)
    {
        location = null;
        var t = text.Trim();
        if (!PathUtil.IsUncServerRoot(t)) return false;
        location = new Location(Schemes.Network, t.TrimEnd('\\', '/'));
        return true;
    }

    public override Task EnumerateAsync(Location location, IEnumerationSink sink, CancellationToken ct)
    {
        var server = location.Path.TrimEnd('\\');
        foreach (var share in WindowsNetwork.EnumerateShares(server))
        {
            ct.ThrowIfCancellationRequested();
            var e = new EntryData(share.Share, EntryKind.Share) { Tag = share };
            if (share.IsSpecial) e.Flags |= EntryFlags.Hidden;
            sink.AddBatch([e]);
        }
        return Task.CompletedTask;
    }

    public override Location? GetChildLocation(Location parent, in EntryData entry) =>
        entry.Kind == EntryKind.Share ? Location.FileSystem(parent.Path.TrimEnd('\\') + "\\" + entry.Name) : null;
}

/// <summary>Thrown when a server requires credentials; the UI offers the Windows credential prompt.</summary>
public sealed class NetworkAuthenticationRequiredException(string server, int error)
    : UnauthorizedAccessException($"{server} requires credentials ({new Win32Exception(error).Message}). Press Enter to sign in.")
{
    public string Server { get; } = server;
}

public static unsafe class WindowsNetwork
{
    public static IReadOnlyList<ShareTag> EnumerateShares(string server)
    {
        var result = new List<ShareTag>();
        int resume = 0;
        int rc;
        do
        {
            rc = NetShareEnum(server, 1, out var buffer, MAX_PREFERRED_LENGTH, out int read, out _, ref resume);
            try
            {
                if (rc is not (NERR_Success or ERROR_MORE_DATA))
                {
                    if (rc is ERROR_ACCESS_DENIED or ERROR_LOGON_FAILURE) throw new NetworkAuthenticationRequiredException(server, rc);
                    if (rc == ERROR_BAD_NETPATH) throw new DirectoryNotFoundException($"The server {server} was not found or is not reachable.");
                    throw new IOException(new Win32Exception(rc).Message);
                }
                var size = Marshal.SizeOf<SHARE_INFO_1>();
                for (int i = 0; i < read; i++)
                {
                    var si = Marshal.PtrToStructure<SHARE_INFO_1>(buffer + i * size);
                    uint baseType = si.shi1_type & 0xFF;
                    if (baseType != STYPE_DISKTREE) continue;
                    var name = Marshal.PtrToStringUni(si.shi1_netname) ?? string.Empty;
                    var remark = Marshal.PtrToStringUni(si.shi1_remark);
                    result.Add(new ShareTag(server, name, string.IsNullOrEmpty(remark) ? null : remark, (si.shi1_type & STYPE_SPECIAL) != 0 || name.EndsWith('$')));
                }
            }
            finally
            {
                if (buffer != 0) NetApiBufferFree(buffer);
            }
        } while (rc == ERROR_MORE_DATA);
        return result;
    }

    /// <summary>Shows the Windows credential prompt for a server or share. Returns an error or null on success.</summary>
    public static string? ConnectInteractive(string remoteName, nint ownerWindow)
    {
        var remote = Marshal.StringToHGlobalUni(PathUtil.IsUncServerRoot(remoteName) ? remoteName.TrimEnd('\\') + "\\IPC$" : remoteName);
        try
        {
            var res = new NETRESOURCEW { dwType = RESOURCETYPE_DISK, lpRemoteName = remote };
            int rc = WNetAddConnection3(ownerWindow, ref res, 0, 0, CONNECT_INTERACTIVE | CONNECT_PROMPT | CONNECT_TEMPORARY);
            if (rc == 0) return null;
            if (rc == ERROR_CANCELLED) return "Sign-in was canceled.";
            if (res.dwType == RESOURCETYPE_DISK && PathUtil.IsUncServerRoot(remoteName))
            {
                // IPC$ may be disabled: retry against the server name itself.
                var plain = Marshal.StringToHGlobalUni(remoteName.TrimEnd('\\'));
                try
                {
                    var res2 = new NETRESOURCEW { dwType = RESOURCETYPE_DISK, lpRemoteName = plain };
                    int rc2 = WNetAddConnection3(ownerWindow, ref res2, 0, 0, CONNECT_INTERACTIVE | CONNECT_PROMPT | CONNECT_TEMPORARY);
                    if (rc2 == 0) return null;
                }
                finally
                {
                    Marshal.FreeHGlobal(plain);
                }
            }
            return new Win32Exception(rc).Message;
        }
        finally
        {
            Marshal.FreeHGlobal(remote);
        }
    }

    public static string? ShowConnectDriveDialog(nint owner)
    {
        int rc = WNetConnectionDialog(owner, RESOURCETYPE_DISK);
        return rc is 0 or -1 ? null : new Win32Exception(rc).Message;
    }

    public static string? ShowDisconnectDriveDialog(nint owner)
    {
        int rc = WNetDisconnectDialog(owner, RESOURCETYPE_DISK);
        return rc is 0 or -1 ? null : new Win32Exception(rc).Message;
    }

    /// <summary>UNC name behind a mapped drive letter ("Z:"), or null.</summary>
    public static string? GetRemoteName(string drive)
    {
        var buf = stackalloc char[520];
        int len = 520;
        return WNetGetConnection(drive, buf, ref len) == 0 ? new string(buf) : null;
    }
}
