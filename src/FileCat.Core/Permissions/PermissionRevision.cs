using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using FileCat.Core.FileSystem;

namespace FileCat.Core.Permissions;

/// <summary>Section evidence compared on the bound subject. Presentation, timestamps outside the requested sections and lookup labels are irrelevant.</summary>
internal readonly record struct PermissionRevision(PermissionSections Sections, string Digest)
{
    internal static PermissionRevision? Read(PermissionSnapshot snapshot, PermissionSections sections)
    {
        const PermissionSections known = PermissionSections.Ordinary | PermissionSections.Audit | PermissionSections.Flags |
            PermissionSections.VolumePolicy | PermissionSections.Times | PermissionSections.FileCapabilities;
        if ((snapshot.Binding is null && snapshot.ServerState is null) || sections == PermissionSections.None || (sections & ~known) != 0) return null;
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write((int)snapshot.Model);
        writer.Write((int)sections);
        if (snapshot.ServerState is { } server)
        {
            writer.Write(server.Target.Digest);
            writer.Write(server.Flags);
            writer.Write(server.NativeMode);
            writer.Write(server.Size);
            writer.Write(server.ModifiedUtcTicks);
        }
        if (!Append(PermissionSections.Owner, snapshot.Owner, Principal) || !Append(PermissionSections.Group, snapshot.Group, Principal) ||
            !Append(PermissionSections.Mode, snapshot.Mode, writer.Write) || !Append(PermissionSections.Access, snapshot.Access, Acl) ||
            !Append(PermissionSections.Defaults, snapshot.Defaults, Acl) || !Append(PermissionSections.Inheritance, snapshot.Inheritance, Control) ||
            !Append(PermissionSections.Audit, snapshot.Audit, Acl) || !Append(PermissionSections.Flags, snapshot.Flags, Flags) ||
            !Append(PermissionSections.VolumePolicy, snapshot.VolumePolicy, value => writer.Write(value.HonorsOwnership)) ||
            !Append(PermissionSections.Times, snapshot.Times, Times) ||
            !Append(PermissionSections.FileCapabilities, snapshot.FileCapabilities, FileCapabilities)) return null;
        writer.Flush();
        return new(sections, Convert.ToHexString(SHA256.HashData(stream.GetBuffer().AsSpan(0, (int)stream.Length))));

        bool Append<T>(PermissionSections section, PermissionObservation<T>? observation, Action<T> value) where T : struct
        {
            if ((sections & section) == 0) return true;
            if (observation is null || observation.Status is not (PermissionReadStatus.Present or PermissionReadStatus.Absent) ||
                observation.Status == PermissionReadStatus.Present && observation.Value is null) return false;
            writer.Write((int)observation.Status);
            writer.Write(observation.Value.HasValue);
            if (observation.Value is { } observed) value(observed);
            return true;
        }

        void Principal(Principal principal)
        {
            writer.Write((int)principal.Kind);
            writer.Write(principal.Id);
            writer.Write(principal.Authority);
        }

        void Acl(PermissionAclValue value)
        {
            ReadOnlySpan<byte> bytes;
            switch (value.Acl)
            {
                case WindowsDacl windows:
                    writer.Write((byte)0);
                    writer.Write((int)windows.Form);
                    if (windows.Form != WindowsDaclForm.Entries) return;
                    // The self-relative descriptor also contains independently editable principals. Compare only the ACL here.
                    var descriptor = windows.SecurityDescriptor.AsSpan();
                    if (descriptor.Length < 20) throw new InvalidDataException("The bound descriptor is incomplete.");
                    uint offset = BinaryPrimitives.ReadUInt32LittleEndian(descriptor[16..]);
                    if (offset < 20 || offset > descriptor.Length - 8) throw new InvalidDataException("The bound ACL offset is invalid.");
                    ushort length = BinaryPrimitives.ReadUInt16LittleEndian(descriptor[((int)offset + 2)..]);
                    if (length < 8 || length > descriptor.Length - offset) throw new InvalidDataException("The bound ACL size is invalid.");
                    bytes = descriptor.Slice((int)offset, length);
                    break;
                case PosixAcl posix:
                    writer.Write((byte)1);
                    bytes = posix.Bytes.AsSpan();
                    break;
                case WindowsSacl sacl:
                    writer.Write((byte)4);
                    writer.Write(sacl.Present);
                    writer.Write(sacl.Control.Flags);
                    writer.Write(sacl.Control.DescriptorRevision);
                    writer.Write(sacl.Control.ResourceManagerControl.HasValue);
                    if (sacl.Control.ResourceManagerControl is { } resourceControl) writer.Write(resourceControl);
                    bytes = sacl.Bytes.AsSpan();
                    break;
                case OpaquePermissionAcl opaque:
                    writer.Write((byte)2);
                    bytes = opaque.Bytes.AsSpan();
                    break;
                case DarwinAcl darwin:
                    writer.Write((byte)3);
                    bytes = darwin.FileSecurity.AsSpan();
                    break;
                default: throw new InvalidDataException("This ACL does not have revision evidence.");
            }
            if (bytes.Length > PermissionSnapshots.MaxSectionBytes) throw new InvalidDataException("The ACL exceeds the revision budget.");
            writer.Write(bytes.Length);
            writer.Write(bytes);
        }

        void Control(WindowsAclControl value)
        {
            // Ignore owner/group defaulting and independently read SACL control bits on a DACL review.
            writer.Write((ushort)(value.Flags & 0x950c));
            writer.Write(value.DescriptorRevision);
        }

        void Flags(PermissionFlagsValue value) { writer.Write(value.KnownBits); writer.Write(value.Bits & value.KnownBits); }
        void Times(PermissionTimesValue value) { writer.Write(value.Modified?.UtcTicks ?? -1); writer.Write(value.Created?.UtcTicks ?? -1); }
        void FileCapabilities(PermissionFileCapabilities value)
        {
            writer.Write(value.Bytes.Length);
            writer.Write(value.Bytes.AsSpan());
        }
    }

}
