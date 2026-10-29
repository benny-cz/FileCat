using System.ComponentModel;
using System.Runtime.InteropServices;
using FileCat.Core.Inspect;
using FileCat.Core.Records;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Platform.Windows;

/// <summary>
/// A Cloud Files placeholder in its file-system record (D-56): what the Cloud Files API says of it — how much of it is on
/// this device, its pin and in-sync states, its IDs, the identity its provider gave it — and of its sync root's provider
/// and policies. Asked on the handle the record is read through, which only reads attributes: nothing is downloaded,
/// its pin and in-sync states stay as they are, and no provider is started.
/// </summary>
public sealed unsafe partial class WindowsFileRecords
{
    private const int CfPlaceholderInfoStandard = 1, CfSyncRootInfoStandard = 1;
    /// <summary>A sync root's identity is at most 64 KiB.</summary>
    private const int MaxSyncRootIdentity = 64 * 1024;

    /// <summary>IO_REPARSE_TAG_CLOUD and its 16 variants (0x9000?01A): a Cloud Files placeholder.</summary>
    internal static bool IsCloudTag(uint tag) => (tag & 0xFFFF_0FFF) == 0x9000_001A;

    /// <summary>
    /// What the Cloud Files API says of the placeholder a handle is open on (CfGetPlaceholderInfo, standard class), or
    /// null with why it could not say. It asks for no data, so it downloads nothing.
    /// </summary>
    internal static CloudPlaceholderInfo? PlaceholderInfo(SafeFileHandle handle, List<string> problems, out string? failed)
    {
        var buffer = new byte[CloudPlaceholderInfo.FixedSize + CloudPlaceholderInfo.MaxIdentity + 4];
        int result;
        uint got;
        try
        {
            fixed (byte* b = buffer) result = CfGetPlaceholderInfo(handle, CfPlaceholderInfoStandard, b, (uint)buffer.Length, out got);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            failed = "The Cloud Files API (cldapi.dll) is not available on this Windows.";
            return null;
        }
        failed = result < 0 ? "The Cloud Files API could not read it: " + HResultText(result) : null;
        return result < 0 ? null : CloudPlaceholderInfo.Parse(buffer.AsSpan(0, (int)Math.Min(got, (uint)buffer.Length)), problems);
    }

    /// <summary>What the Cloud Files API says of the sync root holding the item a handle is open on (CfGetSyncRootInfoByHandle), or null with why not.</summary>
    internal static CloudSyncRootInfo? SyncRootInfo(SafeFileHandle handle, List<string> problems, out string? failed)
    {
        var buffer = new byte[CloudSyncRootInfo.FixedSize + MaxSyncRootIdentity + 8];
        int result;
        uint got;
        try
        {
            fixed (byte* b = buffer) result = CfGetSyncRootInfoByHandle(handle, CfSyncRootInfoStandard, b, (uint)buffer.Length, out got);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            failed = "The Cloud Files API (cldapi.dll) is not available on this Windows.";
            return null;
        }
        failed = result < 0 ? "Its sync root could not be read: " + HResultText(result) : null;
        return result < 0 ? null : CloudSyncRootInfo.Parse(buffer.AsSpan(0, (int)Math.Min(got, (uint)buffer.Length)), problems);
    }

    private static string HResultText(int result) =>
        ((uint)result >> 16 == 0x8007 ? new Win32Exception(result & 0xFFFF).Message : Marshal.GetExceptionForHR(result)?.Message ?? "failed") + $" (0x{result:X8})";

    private sealed partial class Reader
    {
        private InspectionSection? CloudSection()
        {
            if ((_attributes & 0x400) == 0 || !IsCloudTag(_reparseTag) || _remote) return null;
            const string Title = "Cloud placeholder (Cloud Files)";
            var lines = Wrap("Its sync provider keeps its content, and Windows downloads it (hydrates it) when a program reads it. Read through the Cloud Files API on a handle that only reads attributes: nothing is downloaded, and its pin and in-sync states stay as they are.");
            var problems = new List<string>();
            if (PlaceholderInfo(_item, problems, out string? failed) is not { } info)
                return new InspectionSection(Title, []) { Lines = [.. lines, .. Wrap(failed ?? string.Join(" ", problems))] };
            var fields = new List<(string, string)>();
            if (!_isFolder)
                fields.Add(("On this device", RecordText.Bytes(info.OnDiskDataSize) + (_size < 0 ? ""
                    : info.OnDiskDataSize <= 0 ? $" of {RecordText.Bytes(_size)}: none of it (only in the cloud)"
                    : info.OnDiskDataSize >= _size ? $" of {RecordText.Bytes(_size)}: all of it"
                    : $" of {RecordText.Bytes(_size)}: part of it")));
            fields.Add(("Validated", RecordText.Bytes(info.ValidatedDataSize) + " (what its provider has confirmed is current)"));
            fields.Add(("Changed here", RecordText.Bytes(info.ModifiedDataSize) + (info.ModifiedDataSize > 0 ? " (not yet synced)" : "")));
            if (info.PropertiesSize != 0) fields.Add(("Properties", RecordText.Bytes(info.PropertiesSize) + " (its provider's own data about it)"));
            fields.Add(("Pin state", CloudPlaceholderInfo.PinStateText(info.PinState)));
            fields.Add(("In sync", CloudPlaceholderInfo.InSyncText(info.InSyncState)));
            bool own = _id is { } id && (ulong)id == (ulong)info.FileId;
            fields.Add(("File ID", $"0x{info.FileId:X16}" + (_id is null ? "" : own ? " (its own)" : $" (NOT its file ID, 0x{(ulong)_id.Value:X16})")));
            string? rootPath = info.SyncRootFileId != 0 ? PathOfId((ulong)info.SyncRootFileId) : null;
            fields.Add(("Sync root", (rootPath ?? "not found") + $" · file ID 0x{info.SyncRootFileId:X16}"));
            if (SyncRootInfo(_item, problems, out string? rootFailed) is { } root)
            {
                fields.Add(("Provider", $"{(root.ProviderName.Length > 0 ? root.ProviderName : "unnamed")}{(root.ProviderVersion.Length > 0 ? " " + root.ProviderVersion : "")} · {root.StatusText}"));
                fields.Add(("Downloads", root.HydrationText));
                fields.Add(("Folders", root.PopulationText));
            }
            else if (rootFailed is not null) problems.Add(rootFailed);
            fields.Add(("Its identity", info.Identity.Length == 0 ? "none" : $"{info.Identity.Length:N0} bytes: its provider's own name for it (below)"));
            if (!own && _id is not null) _warnings.Add("Cloud Files: the placeholder's file ID is not the item's (see Cloud placeholder).");
            foreach (var problem in problems) lines.AddRange(Wrap(problem, "• "));
            if (info.Identity.Length > 0)
            {
                lines.Add("Its identity:");
                lines.AddRange(RecordText.HexLines(info.Identity, 256));
            }
            return new InspectionSection(Title, fields) { Lines = lines };
        }
    }

    [LibraryImport("cldapi.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int CfGetPlaceholderInfo(SafeFileHandle handle, int infoClass, void* buffer, uint length, out uint returned);

    [LibraryImport("cldapi.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int CfGetSyncRootInfoByHandle(SafeFileHandle handle, int infoClass, void* buffer, uint length, out uint returned);
}
