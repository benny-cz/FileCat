using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;
using FileCat.Core.Inspect;

namespace FileCat.Platform.Windows;

/// <summary>
/// An encrypted item's EFS keys in its file-system record (D-56): the users who can decrypt it and the recovery agents,
/// each with the certificate that unlocks its copy of the file's key, as the EFS API reads them from the item's $EFS
/// data. Needs no administrator rights, and decrypts nothing.
/// </summary>
public sealed unsafe partial class WindowsFileRecords
{
    /// <summary>The most entries of a list read (a file has a handful), and the longest hash and text taken from one.</summary>
    private const int MaxEfsEntries = 256, MaxEfsHash = 64, MaxEfsText = 1024;

    /// <summary>One entry of an EFS certificate hash list: a user who can decrypt the item, or a recovery agent.</summary>
    /// <param name="Sid">The account's SID, or null when the entry names none (recovery agents often do not) or an invalid one.</param>
    /// <param name="Thumbprint">The certificate's hash (its thumbprint) in hex, or empty when the entry has none.</param>
    /// <param name="Display">What the certificate says of whom it is for, or null.</param>
    internal sealed record EfsEntry(string? Sid, string Thumbprint, string? Display);

    /// <summary>ENCRYPTION_CERTIFICATE_HASH_LIST: a count, and an array of pointers to entries.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct EfsHashList
    {
        public uint Count;
        public nint Entries;
    }

    /// <summary>ENCRYPTION_CERTIFICATE_HASH: the user's SID, the certificate's hash (an EFS_HASH_BLOB), and its display text.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct EfsHashEntry
    {
        public uint TotalLength;
        public nint Sid;
        public nint Hash;
        public nint Display;
    }

    /// <summary>EFS_HASH_BLOB: a length and the bytes.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct EfsHashBlob
    {
        public uint Length;
        public nint Data;
    }

    /// <summary>
    /// The entries of an ENCRYPTION_CERTIFICATE_HASH_LIST as the EFS API returns it (from the item's $EFS data, which
    /// anyone who made the file shaped). Each count and length is checked before it is used: at most
    /// <see cref="MaxEfsEntries"/> entries, a hash of at most 64 bytes, a SID Windows calls valid, and text of at most
    /// 1,024 characters with control characters shown as '?'. What does not fit is said in <paramref name="problems"/>.
    /// </summary>
    internal static List<EfsEntry> ReadEfsList(nint list, List<string> problems)
    {
        var entries = new List<EfsEntry>();
        if (list == 0) return entries;
        var header = (EfsHashList*)list;
        uint count = header->Count;
        if (count == 0) return entries;
        if (header->Entries == 0)
        {
            problems.Add($"The list says it has {count:N0} entries, but holds none.");
            return entries;
        }
        if (count > MaxEfsEntries)
        {
            problems.Add($"The list says it has {count:N0} entries; only the first {MaxEfsEntries:N0} were read.");
            count = MaxEfsEntries;
        }
        var array = (EfsHashEntry**)header->Entries;
        int empty = 0, badSid = 0, badHash = 0;
        for (int i = 0; i < count; i++)
        {
            var entry = array[i];
            if (entry == null)
            {
                empty++;
                continue;
            }
            string? sid = null;
            if (entry->Sid != 0)
            {
                if (IsValidSid(entry->Sid)) sid = new SecurityIdentifier(entry->Sid).Value;
                else badSid++;
            }
            string thumbprint = "";
            if (entry->Hash != 0)
            {
                var blob = (EfsHashBlob*)entry->Hash;
                if (blob->Length is > 0 and <= MaxEfsHash && blob->Data != 0) thumbprint = Convert.ToHexString(new ReadOnlySpan<byte>((void*)blob->Data, (int)blob->Length));
                else badHash++;
            }
            entries.Add(new EfsEntry(sid, thumbprint, EfsText(entry->Display)));
        }
        if (empty > 0) problems.Add($"{empty:N0} of its entries are empty.");
        if (badSid > 0) problems.Add($"{badSid:N0} of its entries name a SID that is not valid.");
        if (badHash > 0) problems.Add($"{badHash:N0} of its entries have no certificate hash, or one longer than {MaxEfsHash} bytes.");
        return entries;
    }

    /// <summary>A NUL-terminated string the API returned, at most <see cref="MaxEfsText"/> characters, control characters shown as '?'.</summary>
    private static string? EfsText(nint text)
    {
        if (text == 0) return null;
        var chars = (char*)text;
        int length = 0;
        while (length < MaxEfsText && chars[length] != '\0') length++;
        var span = new Span<char>(chars, length).ToArray();
        for (int i = 0; i < span.Length; i++)
            if (char.IsControl(span[i])) span[i] = '?';
        return new string(span) + (length == MaxEfsText ? "…" : "");
    }

    private sealed partial class Reader
    {
        private InspectionSection? EfsSection()
        {
            if ((_attributes & 0x4000) == 0) return null;
            const string Title = "Encryption (EFS)";
            var fields = new List<(string, string)>
            {
                ("Encrypted", _isFolder
                    ? "yes (EFS): files made in it are encrypted"
                    : "yes (EFS): its content is encrypted with a key of its own, which its $EFS data keeps encrypted for each user and recovery agent below"),
            };
            // A server would be asked over the network (its EFS service), which reading a record never does.
            if (_remote) return new InspectionSection(Title, fields) { Lines = Wrap("Who can decrypt it is not asked: on a network file that would ask the server's EFS service.") };
            var lines = new List<string>();
            var rows = new List<string[]>();
            foreach (bool users in (bool[])[true, false])
            {
                var problems = new List<string>();
                string role = users ? "user" : "recovery agent";
                var (entries, failed) = QueryEfs(users, problems);
                // A folder's $EFS names whom it was encrypted for; files made in it get keys of their own.
                fields.Add((users ? _isFolder ? "Encrypted for" : "Can decrypt it" : "Recovery agents", failed is not null
                    ? "not read: " + failed
                    : entries.Count == 0 ? "none"
                    : $"{entries.Count:N0} {(users ? entries.Count == 1 ? "user" : "users" : entries.Count == 1 ? "agent" : "agents")}"));
                foreach (var e in entries)
                    rows.Add([role, e.Thumbprint.Length > 0 ? e.Thumbprint : "—", e.Display ?? "—", Who(e.Sid)]);
                foreach (var problem in problems) lines.AddRange(Wrap((users ? "Its users: " : "Its recovery agents: ") + problem, "• "));
            }
            lines.InsertRange(0, Wrap("Each certificate (named by its thumbprint) unlocks its own copy of the item's key: a user's lets that user open it, a recovery agent's lets whoever holds that agent's key recover it. Read through the EFS API; nothing is decrypted."));
            return new InspectionSection(Title, fields)
            {
                Table = rows.Count > 0 ? new InspectionTable(["Role", "Certificate thumbprint", "Certificate is for", "Account"], rows) : null,
                Lines = lines,
            };
        }

        /// <summary>The item's users (or recovery agents) as the EFS API reports them, or why it did not.</summary>
        private (List<EfsEntry> Entries, string? Failed) QueryEfs(bool users, List<string> problems)
        {
            nint list = 0;
            uint error;
            string name = WindowsFileOperations.Long(path);
            try { error = users ? QueryUsersOnEncryptedFile(name, out list) : QueryRecoveryAgentsOnEncryptedFile(name, out list); }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { return ([], "the EFS API is not available on this Windows"); }
            try { return error != 0 ? ([], new Win32Exception((int)error).Message) : (ReadEfsList(list, problems), null); }
            finally
            {
                if (list != 0) FreeEncryptionCertificateHashList(list);
            }
        }

        private static string Who(string? sid) =>
            sid is null ? "—" : AccountName(sid) is { } name ? $"{name} ({sid})" : sid + " (no account by this ID here)";
    }

    [LibraryImport("advapi32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint QueryUsersOnEncryptedFile(string fileName, out nint users);

    [LibraryImport("advapi32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint QueryRecoveryAgentsOnEncryptedFile(string fileName, out nint agents);

    [LibraryImport("advapi32.dll")]
    private static partial void FreeEncryptionCertificateHashList(nint list);

    [LibraryImport("advapi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsValidSid(nint sid);
}
