using System.Collections.Immutable;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using FileCat.Core.Permissions;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Recovery.Unix;

[Flags]
internal enum LinuxBrokerSections : uint { Access = 1, Defaults = 2, Capabilities = 4, Flags = 8, All = 15 }
internal enum LinuxBrokerReadStatus { Unrequested, Present, Absent, NotApplicable, Denied, Unsupported, Incomplete, Opaque, Failed }
internal enum LinuxBrokerWriteState { NotSent, Rejected, Verified, Uncertain }

internal abstract record LinuxBrokerChange
{
    internal sealed record Mode(ushort Bits) : LinuxBrokerChange;
    internal sealed record Ownership(uint? Uid, uint? Gid) : LinuxBrokerChange;
    internal sealed record Acl(bool Default, ImmutableArray<byte> Bytes) : LinuxBrokerChange;
    internal sealed record Protection(uint Set, uint Clear) : LinuxBrokerChange;
}

internal sealed record LinuxBrokerAttribute(LinuxBrokerReadStatus Status, int Error, ImmutableArray<byte> Bytes);
internal sealed record LinuxBrokerFlags(LinuxBrokerReadStatus Status, int Error, uint Known, uint Bits);

internal sealed record LinuxBrokerSnapshot(string Path, LinuxBrokerIdentity Identity, LinuxBrokerSections Sections,
    uint Mode, uint Uid, uint Gid, uint LinkCount, ulong Size, ulong Attributes, ulong AttributeMask, ulong MountAttributes,
    long ChangeSeconds, uint ChangeNanoseconds, long ModificationSeconds, uint ModificationNanoseconds,
    ImmutableArray<byte> Revision, LinuxBrokerAttribute Access, LinuxBrokerAttribute Defaults, LinuxBrokerAttribute Capabilities,
    LinuxBrokerFlags Flags);

internal sealed record LinuxBrokerOutcome(LinuxBrokerWriteState State, int SetterError, int VerificationError,
    LinuxBrokerSnapshot? Observed, string? Detail = null);

/// <summary>One operation on one authenticated system-bus connection. Run away from the UI thread.</summary>
internal sealed class LinuxPermissionBroker(string busSocket = "/run/dbus/system_bus_socket")
{
    private const string Name = "org.filecat.Permissions", Object = "/org/filecat/Permissions",
        Interface = "org.filecat.Permissions1", BusDaemon = "org.freedesktop.DBus";
    private const string InspectSignature = "uayayh(ttuuq)u", SnapshotSignature = "(u(ttuuq)(uuuuttttxuxu)ay(uiay)(uiay)(uiay)(uiuu))";
    private const string UnknownOutcome = "org.filecat.Permissions1.OutcomeUnavailable";
    private const int MaxAclBytes = 4 + 8 * 4096, MaxReplyBytes = 3 * MaxAclBytes;
    private static readonly UTF8Encoding ExactUtf8 = new(false, true);

    internal static LinuxPermissionBroker? ForInstalledFileCat() =>
        OperatingSystem.IsLinux() && RuntimeInformation.ProcessArchitecture == Architecture.X64 &&
        !Environment.IsPrivilegedProcess && Environment.ProcessPath == "/opt/filecat/FileCat" &&
        File.Exists("/var/lib/filecat-permissions/installed") &&
        File.Exists("/opt/filecat/FileCat.Permissions.Linux") &&
        File.Exists("/usr/share/polkit-1/actions/org.filecat.permissions.policy") ? new() : null;

    internal LinuxBrokerSnapshot Inspect(LinuxBrokerTarget target, LinuxBrokerSections sections, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        RequireSections(sections);
        try
        {
            using var bus = DBusConnection.Connect(busSocket, MaxReplyBytes, cancellation);
            using var abort = cancellation.Register(bus.Dispose);
            var (sender, token, challenge) = Begin(bus);
            using (challenge)
            {
                var body = WriteTarget(target, sections, token);
                cancellation.ThrowIfCancellationRequested();
                var reply = bus.CallRaw(sender, Object, Interface, "Inspect", InspectSignature, body, target.Pin, sender);
                var reader = RequireReply(reply, SnapshotSignature);
                var snapshot = ReadSnapshot(reader, target, sections);
                RequireEnd(reader);
                return snapshot;
            }
        }
        catch (Exception error) when (cancellation.IsCancellationRequested && IsTransportFailure(error))
        {
            throw new OperationCanceledException(cancellation);
        }
    }

    internal LinuxBrokerOutcome Apply(LinuxBrokerTarget target, LinuxBrokerSnapshot reviewed, LinuxBrokerChange change,
        CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if (reviewed.Path != target.Path || reviewed.Identity != target.Identity || reviewed.Revision.Length != 32)
            throw new PermissionBindingConflictException("The broker review belongs to a different item.");
        RequireSections(reviewed.Sections);
        try
        {
            using var bus = DBusConnection.Connect(busSocket, MaxReplyBytes, cancellation);
            using var abort = cancellation.Register(bus.Dispose);
            var (sender, token, challenge) = Begin(bus);
            using (challenge)
            {
                var body = WriteTarget(target, reviewed.Sections, token);
                body.ByteArray(reviewed.Revision.AsSpan());
                WriteChange(body, change);
                cancellation.ThrowIfCancellationRequested();
                try
                {
                    var reply = bus.CallRaw(sender, Object, Interface, "Apply", InspectSignature + "ayuv", body, target.Pin, sender);
                    return ReadOutcome(RequireReply(reply, "uiia" + SnapshotSignature), target, reviewed.Sections);
                }
                catch (DBusException error) when (error.Sender == sender && error.Name != UnknownOutcome)
                {
                    // A method error from the pinned broker precedes dispatch. Post-dispatch failures use a distinct reply.
                    throw;
                }
                catch (Exception error) when (IsTransportFailure(error))
                {
                    return new(LinuxBrokerWriteState.Uncertain, 0, 0, null,
                        "The broker connection ended before FileCat could verify the outcome. Inspect the item again.");
                }
            }
        }
        catch (Exception error) when (cancellation.IsCancellationRequested && error is not DBusException && IsTransportFailure(error))
        {
            throw new OperationCanceledException(cancellation);
        }
    }

    private static bool IsTransportFailure(Exception error) => error is IOException or SocketException or ObjectDisposedException;

    private static (string Sender, byte[] Token, SafeFileHandle Challenge) Begin(DBusConnection bus)
    {
        var version = new DBusWriter();
        version.UInt32(1);
        var reply = bus.CallRaw(Name, Object, Interface, "Begin", "u", version);
        try
        {
            var reader = RequireReply(reply, "ayh", 1);
            var token = reader.ByteArray(32).ToArray();
            if (token.Length != 32 || reader.UInt32() != 0 || !reply.Sender.StartsWith(':'))
                throw new IOException("The permissions broker sent an invalid challenge.");
            RequireEnd(reader);
            string owner = ReadBusOwner(bus, Name);
            if (reply.Sender != owner || ReadBusUid(bus, owner) != 0)
                throw new IOException("The permissions broker's system-bus owner could not be authenticated.");
            var challenge = new SafeFileHandle((nint)reply.Fds[0], ownsHandle: true);
            reply.Fds.Clear();
            try
            {
                UnixNative.EchoLinuxChallenge(challenge, token);
                return (owner, token, challenge);
            }
            catch
            {
                challenge.Dispose();
                throw;
            }
        }
        finally { CloseDescriptors(reply); }
    }

    private static DBusReader ReadBusIdentity(DBusConnection bus, string method, string name, string signature)
    {
        var body = new DBusWriter();
        body.String(name);
        var reply = bus.CallRaw(BusDaemon, "/org/freedesktop/DBus", BusDaemon, method, "s", body, expectedSender: BusDaemon);
        return RequireReply(reply, signature);
    }

    private static string ReadBusOwner(DBusConnection bus, string name)
    {
        var reader = ReadBusIdentity(bus, "GetNameOwner", name, "s");
        string owner = reader.String();
        RequireEnd(reader);
        return owner;
    }

    private static uint ReadBusUid(DBusConnection bus, string name)
    {
        var reader = ReadBusIdentity(bus, "GetConnectionUnixUser", name, "u");
        uint uid = reader.UInt32();
        RequireEnd(reader);
        return uid;
    }

    private static DBusWriter WriteTarget(LinuxBrokerTarget target, LinuxBrokerSections sections, byte[] token)
    {
        byte[] path = ExactUtf8.GetBytes(target.Path);
        if (path.Length is 0 or >= 4096 || path[0] != (byte)'/' || path.Contains((byte)0))
            throw new ArgumentException("The original path cannot be sent to the Linux permissions broker.", nameof(target));
        var body = new DBusWriter();
        body.UInt32(1);
        body.ByteArray(token);
        body.ByteArray(path);
        body.UInt32(0); // The sole descriptor's index in the D-Bus ancillary data.
        body.Pad(8);
        body.UInt64(target.Identity.Mount);
        body.UInt64(target.Identity.Inode);
        body.UInt32(target.Identity.DeviceMajor);
        body.UInt32(target.Identity.DeviceMinor);
        body.UInt16(target.Identity.Kind);
        body.UInt32((uint)sections);
        return body;
    }

    private static void WriteChange(DBusWriter body, LinuxBrokerChange change)
    {
        switch (change)
        {
            case LinuxBrokerChange.Mode mode:
                body.UInt32(1);
                body.Signature("u");
                body.UInt32(mode.Bits);
                break;
            case LinuxBrokerChange.Ownership ownership:
                body.UInt32(2);
                body.Signature("(uu)");
                body.Pad(8);
                body.UInt32(ownership.Uid ?? uint.MaxValue);
                body.UInt32(ownership.Gid ?? uint.MaxValue);
                break;
            case LinuxBrokerChange.Acl acl:
                if (acl.Bytes.Length > MaxAclBytes) throw new ArgumentException("The ACL exceeds the broker's size limit.");
                body.UInt32(acl.Default ? 4u : 3u);
                body.Signature("ay");
                body.ByteArray(acl.Bytes.AsSpan());
                break;
            case LinuxBrokerChange.Protection protection:
                body.UInt32(5);
                body.Signature("(uu)");
                body.Pad(8);
                body.UInt32(protection.Set);
                body.UInt32(protection.Clear);
                break;
            default: throw new ArgumentException("This change has no Linux broker operation.", nameof(change));
        }
    }

    private static LinuxBrokerOutcome ReadOutcome(DBusReader reader, LinuxBrokerTarget target, LinuxBrokerSections sections)
    {
        uint code = reader.UInt32();
        int setter = unchecked((int)reader.UInt32());
        int verification = unchecked((int)reader.UInt32());
        if (code > (uint)LinuxBrokerWriteState.Uncertain || setter > 0 || verification > 0)
            throw new IOException("The permissions broker returned an invalid write result.");
        var state = (LinuxBrokerWriteState)code;
        uint size = reader.UInt32();
        reader.Pad(8);
        if (reader.Remaining < 0 || (uint)reader.Remaining != size)
            throw new IOException("The permissions broker returned an invalid observation array.");
        var observed = size == 0 ? null : ReadSnapshot(reader, target, sections);
        if (state is LinuxBrokerWriteState.Verified or LinuxBrokerWriteState.Rejected && observed is null ||
            state == LinuxBrokerWriteState.NotSent && observed is not null)
            throw new IOException("The permissions broker returned inconsistent read-back evidence.");
        RequireEnd(reader);
        return new(state, setter, verification, observed);
    }

    private static LinuxBrokerSnapshot ReadSnapshot(DBusReader reader, LinuxBrokerTarget target, LinuxBrokerSections sections)
    {
        reader.Pad(8);
        uint selected = reader.UInt32();
        reader.Pad(8);
        var identity = new LinuxBrokerIdentity(reader.UInt64(), reader.UInt64(), reader.UInt32(), reader.UInt32(), reader.UInt16());
        if (identity != target.Identity || selected != (uint)sections)
            throw new IOException("The permissions broker returned an observation for a different item or section.");
        reader.Pad(8);
        uint mode = reader.UInt32(), uid = reader.UInt32(), gid = reader.UInt32(), links = reader.UInt32();
        ulong size = reader.UInt64(), attributes = reader.UInt64(), attributeMask = reader.UInt64(), mountAttributes = reader.UInt64();
        long changed = unchecked((long)reader.UInt64());
        uint changeNanos = reader.UInt32();
        long modified = unchecked((long)reader.UInt64());
        uint modificationNanos = reader.UInt32();
        var revision = ImmutableArray.Create(reader.ByteArray(32));
        if (revision.Length != 32 || (mode & 0xf000) != identity.Kind || mode > ushort.MaxValue ||
            uid == uint.MaxValue || gid == uint.MaxValue || changeNanos >= 1_000_000_000 || modificationNanos >= 1_000_000_000)
            throw new IOException("The permissions broker returned invalid native file facts.");
        var access = ReadAttribute(reader, MaxAclBytes);
        var defaults = ReadAttribute(reader, MaxAclBytes);
        var capabilities = ReadAttribute(reader, 24);
        reader.Pad(8);
        var flags = new LinuxBrokerFlags(ReadStatus(reader), ReadError(reader), reader.UInt32(), reader.UInt32());
        return new(target.Path, identity, sections, mode, uid, gid, links, size, attributes, attributeMask, mountAttributes,
            changed, changeNanos, modified, modificationNanos, revision, access, defaults, capabilities, flags);
    }

    private static LinuxBrokerAttribute ReadAttribute(DBusReader reader, int limit)
    {
        reader.Pad(8);
        return new(ReadStatus(reader), ReadError(reader), ImmutableArray.Create(reader.ByteArray(limit)));
    }

    private static LinuxBrokerReadStatus ReadStatus(DBusReader reader)
    {
        uint status = reader.UInt32();
        return status <= (uint)LinuxBrokerReadStatus.Failed ? (LinuxBrokerReadStatus)status :
            throw new IOException("The permissions broker returned an unknown observation status.");
    }

    private static int ReadError(DBusReader reader)
    {
        uint error = reader.UInt32();
        return error <= int.MaxValue ? (int)error : throw new IOException("The permissions broker returned an invalid observation error.");
    }

    private static void RequireSections(LinuxBrokerSections sections)
    {
        if (sections == 0 || (sections & ~LinuxBrokerSections.All) != 0)
            throw new ArgumentOutOfRangeException(nameof(sections));
    }

    private static DBusReader RequireReply(DBusReply reply, string signature, int descriptors = 0)
    {
        if (reply.Signature == signature && reply.Fds.Count == descriptors) return reply.Body();
        CloseDescriptors(reply);
        throw new IOException("The permissions broker sent a reply with the wrong types or descriptors.");
    }

    private static void CloseDescriptors(DBusReply reply)
    {
        foreach (int descriptor in reply.Fds) UnixNative.Close(descriptor);
        reply.Fds.Clear();
    }

    private static void RequireEnd(DBusReader body)
    {
        if (body.Remaining != 0) throw new IOException("The permissions broker returned unexpected data.");
    }
}
