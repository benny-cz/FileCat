using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Core.Archives;

/// <summary>Bounded visible native xattr inventory. Enumeration cannot prove absence of inaccessible/hidden policy state.</summary>
internal sealed partial class ArchiveUnixExtendedAttributes
{
    internal const int MaxNamesBytes = 64 * 1024, MaxAttributes = 1024;
    internal const int MaxBytes = ArchiveWritePolicy.MaxEntryMetadataBytes;
    internal sealed class Attribute
    {
        private readonly byte[] value;
        internal string Name { get; }
        internal ReadOnlySpan<byte> Value => value;
        internal Attribute(string name, ReadOnlySpan<byte> value)
        { _ = NameLength(name); if (value.Length > MaxBytes) throw Limit(); Name = name; this.value = value.ToArray(); }
    }
    private static readonly UTF8Encoding Utf8 = new(false, true);
    internal ReadOnlyCollection<Attribute> Attributes { get; }
    internal string Digest { get; }

    internal ArchiveUnixExtendedAttributes(IReadOnlyList<Attribute> attributes)
    {
        ArgumentNullException.ThrowIfNull(attributes);
        if (attributes.Count > MaxAttributes) throw Limit();
        var frozen = new Attribute[attributes.Count]; int namesBytes = 0, total = 12;
        for (int i = 0; i < frozen.Length; i++)
        {
            var attribute = attributes[i] ?? throw new ArgumentException("A native attribute is missing.");
            int length = NameLength(attribute.Name);
            if (i != 0 && string.CompareOrdinal(attributes[i - 1].Name, attribute.Name) >= 0) throw Invalid();
            namesBytes = checked(namesBytes + length + 1); total = checked(total + 8 + length + attribute.Value.Length);
            if (namesBytes > MaxNamesBytes || total > MaxBytes) throw Limit();
            frozen[i] = new(attribute.Name, attribute.Value);
        }
        Attributes = Array.AsReadOnly(frozen);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); hash.AppendData("FCUXATR1"u8);
        Span<byte> fields = stackalloc byte[8]; BinaryPrimitives.WriteInt32LittleEndian(fields, frozen.Length); hash.AppendData(fields[..4]);
        foreach (var attribute in frozen)
        {
            byte[] name = Utf8.GetBytes(attribute.Name);
            BinaryPrimitives.WriteInt32LittleEndian(fields, name.Length); BinaryPrimitives.WriteInt32LittleEndian(fields[4..], attribute.Value.Length);
            hash.AppendData(fields); hash.AppendData(name); hash.AppendData(attribute.Value);
        }
        Digest = Convert.ToHexString(hash.GetHashAndReset());
    }

    internal bool MatchesExcept(ArchiveUnixExtendedAttributes other, string excludedName)
    {
        int left = 0, right = 0;
        while (true)
        {
            while (left < Attributes.Count && Attributes[left].Name == excludedName) left++;
            while (right < other.Attributes.Count && other.Attributes[right].Name == excludedName) right++;
            if (left == Attributes.Count || right == other.Attributes.Count) return left == Attributes.Count && right == other.Attributes.Count;
            var a = Attributes[left++]; var b = other.Attributes[right++];
            if (a.Name != b.Name || !a.Value.SequenceEqual(b.Value)) return false;
        }
    }

    internal bool MatchesOrigin(ArchiveOriginSnapshot origin)
    {
        string name = origin.Format switch { ArchiveOriginFormat.MacQuarantine => "com.apple.quarantine", ArchiveOriginFormat.LinuxOriginUrl => "user.xdg.origin.url", _ => throw Invalid() };
        var attribute = Attributes.FirstOrDefault(a => a.Name == name);
        return origin.State == ArchiveOriginState.Absent ? attribute is null : origin.State == ArchiveOriginState.Present && attribute is not null && attribute.Value.SequenceEqual(origin.Bytes);
    }

    internal bool MatchesLinuxAcl(bool present, ReadOnlySpan<byte> acl)
    {
        var attribute = Attributes.FirstOrDefault(a => a.Name == "system.posix_acl_access");
        return present ? attribute is not null && attribute.Value.SequenceEqual(acl) : attribute is null;
    }

    internal static ArchiveUnixExtendedAttributes Capture(SafeFileHandle handle, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(handle); bool mac = OperatingSystem.IsMacOS();
        if (!mac && !OperatingSystem.IsLinux()) throw new PlatformNotSupportedException();
        bool retained = false;
        try
        {
            ct.ThrowIfCancellationRequested(); handle.DangerousAddRef(ref retained);
            if (handle.IsInvalid) throw Unavailable(); int fd = checked((int)handle.DangerousGetHandle());
            string[] names = Names(); var attributes = new List<Attribute>(names.Length); int total = 12;
            foreach (string name in names)
            {
                ct.ThrowIfCancellationRequested(); nint length = Get(name, null, 0);
                if (length < 0) throw Failure(Marshal.GetLastPInvokeError());
                int nameBytes = NameLength(name);
                if (length > MaxBytes - total - 8L - nameBytes) throw Limit();
                // A nonzero buffer distinguishes a present empty value from growth after the size query.
                byte[] value = new byte[Math.Max(1, (int)length)]; nint read = Get(name, value, value.Length);
                if (read < 0) throw Failure(Marshal.GetLastPInvokeError());
                if (read != length) throw Changed();
                attributes.Add(new(name, value.AsSpan(0, (int)length))); total += 8 + nameBytes + (int)length;
            }
            if (!names.SequenceEqual(Names(), StringComparer.Ordinal)) throw Changed();
            ct.ThrowIfCancellationRequested(); return new(attributes);

            string[] Names()
            {
                ct.ThrowIfCancellationRequested(); nint length = List(null, 0);
                if (length < 0) throw Failure(Marshal.GetLastPInvokeError());
                if (length > MaxNamesBytes) throw Limit();
                byte[] bytes = new byte[Math.Max(1, (int)length)]; nint read = List(bytes, bytes.Length);
                if (read < 0) throw Failure(Marshal.GetLastPInvokeError()); if (read != length) throw Changed();
                return ParseNames(bytes.AsSpan(0, (int)length));
            }
            nint List(byte[]? bytes, nint size) => mac ? MacList(fd, bytes, size, 0) : LinuxList(fd, bytes, size);
            nint Get(string name, byte[]? bytes, nint size) => mac ? MacGet(fd, name, bytes, size, 0, 0) : LinuxGet(fd, name, bytes, size);
            ArchiveWriteException Failure(int error) => error == 34 || error == (mac ? 93 : 61) ? Changed() : error == 7 ? Limit() : Unavailable();
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException) { throw Unavailable(); }
        finally { if (retained) handle.DangerousRelease(); }
    }

    internal static string[] ParseNames(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length > MaxNamesBytes) throw Limit();
        // Count/framing pass before creating any strings or collection entries.
        int count = 0;
        for (int offset = 0; offset < bytes.Length;)
        {
            int length = bytes[offset..].IndexOf((byte)0);
            if (length is <= 0 or > 255) throw Invalid(); if (++count > MaxAttributes) throw Limit(); offset += length + 1;
        }
        var names = new string[count]; int start = 0;
        for (int i = 0; i < count; i++)
        {
            int length = bytes[start..].IndexOf((byte)0);
            try { names[i] = Utf8.GetString(bytes.Slice(start, length)); } catch (DecoderFallbackException) { throw Invalid(); }
            start += length + 1;
        }
        Array.Sort(names, StringComparer.Ordinal);
        for (int i = 1; i < names.Length; i++) if (names[i - 1] == names[i]) throw Invalid();
        return names;
    }
    private static int NameLength(string name)
    {
        if (string.IsNullOrEmpty(name) || name.Length > 255 || name.Contains('\0')) throw Invalid();
        try { int length = Utf8.GetByteCount(name); if (length > 255) throw Invalid(); return length; }
        catch (EncoderFallbackException) { throw Invalid(); }
    }
    private static ArchiveWriteException Invalid() => new(ArchiveWriteReason.UnsupportedFeature, "The native extended-attribute name inventory is unsupported or ambiguous.");
    private static ArchiveWriteException Changed() => new(ArchiveWriteReason.SourceChanged, "The native extended-attribute inventory or values changed during capture.");
    private static ArchiveWriteException Limit() => new(ArchiveWriteReason.BudgetExceeded, "The native extended-attribute inventory exceeds its metadata bound.");
    private static ArchiveWriteException Unavailable() => new(ArchiveWriteReason.PublicationUnavailable, "The native extended attributes could not be fully read; absence must not be assumed.");
    [LibraryImport("libc", EntryPoint = "flistxattr", SetLastError = true)]
    private static partial nint LinuxList(int fd, byte[]? value, nint size);
    [LibraryImport("libc", EntryPoint = "flistxattr", SetLastError = true)]
    private static partial nint MacList(int fd, byte[]? value, nint size, int options);
    [LibraryImport("libc", EntryPoint = "fgetxattr", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial nint LinuxGet(int fd, string name, byte[]? value, nint size);
    [LibraryImport("libc", EntryPoint = "fgetxattr", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial nint MacGet(int fd, string name, byte[]? value, nint size, uint position, int options);
}
