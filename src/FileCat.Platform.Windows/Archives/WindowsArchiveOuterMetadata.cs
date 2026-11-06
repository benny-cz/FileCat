using System.Buffers.Binary;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Text;
using FileCat.Core.Archives;
using FileCat.Core.FileSystem;
using FileCat.Platform.Windows.Elevation;

namespace FileCat.Platform.Windows.Archives;

/// <summary>
/// Immutable observations of the named permission/label/resource-policy subset and outer fields on one held file.
/// This is neither a complete backup-security descriptor nor preservation/cleanup authority. Audit and other
/// unqueried security state have not been established absent. No release profile is enabled.
/// </summary>
internal sealed class WindowsArchiveOuterMetadata
{
    // Each requested component is readable with READ_CONTROL; never silently retry with fewer components.
    public const uint QueriedSecurityInformation = 1 | 2 | 4 | 0x10 | 0x20 | 0x40; // owner, group, DACL, label, resource attributes, CAP.
    public const int MaxSecurityBytes = 128 * 1024;
    private readonly byte[] security;
    public FileIdentity Identity { get; }
    public long CreationTime { get; }
    public long LastWriteTime { get; }
    public uint Attributes { get; }
    public uint LinkCount { get; }
    public string OwnerSid { get; }
    public string GroupSid { get; }
    public bool DaclProtected { get; }
    public ReadOnlySpan<byte> SecurityDescriptor => security;
    public ArchiveOriginSnapshot Origin { get; }
    public string Digest { get; }

    internal WindowsArchiveOuterMetadata(FileIdentity identity, NtFile.BasicInfo basic, uint linkCount,
        ReadOnlySpan<byte> descriptor, ArchiveOriginSnapshot origin)
    {
        ArgumentNullException.ThrowIfNull(origin);
        if (descriptor.Length is < 20 or > MaxSecurityBytes || linkCount == 0 ||
            origin.Format != ArchiveOriginFormat.WindowsZoneIdentifier || origin.State == ArchiveOriginState.Unavailable)
            throw new ArgumentException("Incomplete or unsupported Windows archive metadata evidence.");
        // Re-encode a bounded descriptor to a deterministic self-relative layout, preserving ACE order, flags, owner,
        // group and the returned label subset. No SID lookup, ACL sorting or semantic defaults are invented.
        var raw = new RawSecurityDescriptor(descriptor.ToArray(), 0);
        OwnerSid = raw.Owner?.Value ?? throw new IOException("The archive owner is unavailable.");
        GroupSid = raw.Group?.Value ?? throw new IOException("The archive primary group is unavailable.");
        if (raw.BinaryLength > MaxSecurityBytes) throw new IOException("The archive descriptor exceeds its bound.");
        security = new byte[raw.BinaryLength]; raw.GetBinaryForm(security, 0);
        DaclProtected = (raw.ControlFlags & ControlFlags.DiscretionaryAclProtected) != 0;
        Identity = identity; CreationTime = basic.CreationTime; LastWriteTime = basic.LastWriteTime;
        Attributes = basic.FileAttributes; LinkCount = linkCount; Origin = origin;
        byte[] identityBytes = Encoding.ASCII.GetBytes(identity.ToString());
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> header = stackalloc byte[12];
        BinaryPrimitives.WriteInt32LittleEndian(header, 1); // schema
        BinaryPrimitives.WriteUInt32LittleEndian(header[4..], QueriedSecurityInformation);
        BinaryPrimitives.WriteInt32LittleEndian(header[8..], identityBytes.Length);
        hash.AppendData(header); hash.AppendData(identityBytes);
        Span<byte> fields = stackalloc byte[28];
        BinaryPrimitives.WriteInt64LittleEndian(fields, CreationTime);
        BinaryPrimitives.WriteInt64LittleEndian(fields[8..], LastWriteTime);
        BinaryPrimitives.WriteUInt32LittleEndian(fields[16..], Attributes);
        BinaryPrimitives.WriteUInt32LittleEndian(fields[20..], LinkCount);
        BinaryPrimitives.WriteInt32LittleEndian(fields[24..], security.Length);
        hash.AppendData(fields); hash.AppendData(security); hash.AppendData(Convert.FromHexString(origin.Digest));
        Digest = Convert.ToHexString(hash.GetHashAndReset());
    }
}
