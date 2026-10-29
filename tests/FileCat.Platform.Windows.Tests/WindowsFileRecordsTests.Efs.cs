using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;

namespace FileCat.Platform.Windows.Tests;

/// <summary>D-56: who can decrypt an EFS-encrypted item, read through the EFS API.</summary>
public sealed partial class WindowsFileRecordsTests
{
    /// <summary>Tests that change this computer's state beyond a temporary folder run only where asked, or on a disposable CI runner.</summary>
    private static bool Opted(string variable) =>
        Environment.GetEnvironmentVariable(variable) == "1" || Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true";

    [Fact]
    public unsafe void An_EFS_certificate_list_is_read_with_its_counts_and_lengths_checked()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "The EFS list is Windows'.");
        // Built the way advapi32 hands it out: a count, an array of pointers, each entry with a SID, a hash blob, and a text.
        var memory = new List<nint>();
        nint Alloc(int size)
        {
            var p = (nint)NativeMemory.AllocZeroed((nuint)size);
            memory.Add(p);
            return p;
        }
        nint Bytes(byte[] bytes)
        {
            nint p = Alloc(bytes.Length);
            Marshal.Copy(bytes, 0, p, bytes.Length);
            return p;
        }
        nint Text(string text)
        {
            nint p = Alloc((text.Length + 1) * 2);
            Marshal.Copy(text.ToCharArray(), 0, p, text.Length);
            return p;
        }
        nint Blob(uint length, nint data)
        {
            nint p = Alloc(sizeof(WindowsFileRecords.EfsHashBlob));
            *(WindowsFileRecords.EfsHashBlob*)p = new WindowsFileRecords.EfsHashBlob { Length = length, Data = data };
            return p;
        }
        nint Entry(nint sid, nint hash, nint display)
        {
            nint p = Alloc(sizeof(WindowsFileRecords.EfsHashEntry));
            *(WindowsFileRecords.EfsHashEntry*)p = new WindowsFileRecords.EfsHashEntry { TotalLength = 32, Sid = sid, Hash = hash, Display = display };
            return p;
        }
        nint HashList(uint count, params nint[] entries)
        {
            nint array = entries.Length == 0 ? 0 : Alloc(entries.Length * IntPtr.Size);
            for (int i = 0; i < entries.Length; i++) ((nint*)array)[i] = entries[i];
            nint p = Alloc(sizeof(WindowsFileRecords.EfsHashList));
            *(WindowsFileRecords.EfsHashList*)p = new WindowsFileRecords.EfsHashList { Count = count, Entries = array };
            return p;
        }
        try
        {
            var administrators = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
            var sid = new byte[administrators.BinaryLength];
            administrators.GetBinaryForm(sid, 0);
            byte[] hash = Enumerable.Range(1, 20).Select(i => (byte)i).ToArray();
            // A user with all three; an entry with an invalid SID (revision 9) and an overlong hash; an empty entry.
            byte[] invalid = (byte[])sid.Clone();
            invalid[0] = 9;
            var problems = new List<string>();
            var entries = WindowsFileRecords.ReadEfsList(HashList(3,
                Entry(Bytes(sid), Blob(20, Bytes(hash)), Text("admin(admin@HOST)" + (char)7)),
                Entry(Bytes(invalid), Blob(200, Bytes(new byte[200])), 0),
                0), problems);
            Assert.Equal(2, entries.Count);
            Assert.Equal(new WindowsFileRecords.EfsEntry("S-1-5-32-544", Convert.ToHexString(hash), "admin(admin@HOST)?"), entries[0]);
            Assert.Equal(new WindowsFileRecords.EfsEntry(null, "", null), entries[1]);
            Assert.Equal(["1 of its entries are empty.", "1 of its entries name a SID that is not valid.", "1 of its entries have no certificate hash, or one longer than 64 bytes."], problems);

            // A count beyond any real list is read only as far as the cap; a count with no array is said.
            problems.Clear();
            Assert.Empty(WindowsFileRecords.ReadEfsList(HashList(1_000_000, new nint[256]), problems));
            Assert.Equal(2, problems.Count);
            Assert.Contains("only the first", problems[0], StringComparison.Ordinal);
            problems.Clear();
            Assert.Empty(WindowsFileRecords.ReadEfsList(HashList(5), problems));
            Assert.Contains("holds none", Assert.Single(problems), StringComparison.Ordinal);
            Assert.Empty(WindowsFileRecords.ReadEfsList(0, problems));

            // A text without its end within 1,024 characters is cut there.
            problems.Clear();
            var cut = WindowsFileRecords.ReadEfsList(HashList(1, Entry(0, 0, Text(new string('x', 1100)))), problems);
            Assert.Equal(new string('x', 1024) + "…", Assert.Single(cut).Display);
        }
        finally
        {
            foreach (var p in memory) NativeMemory.Free((void*)p);
        }
    }

    [Fact]
    public void An_encrypted_file_lists_who_can_decrypt_it_without_changing_it()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "EFS is Windows'.");
        // Encrypting a file makes an EFS certificate (and key) for this user when it has none: only where that is wanted.
        if (!Opted("FILECAT_TEST_EFS")) Assert.Skip("Set FILECAT_TEST_EFS=1 to encrypt a test file with EFS (it makes this user an EFS certificate if there is none).");
        string dir = NewFolder();
        try
        {
            string file = Path.Combine(dir, "secret.txt");
            File.WriteAllText(file, "only for its users");
            if (!OnNtfs(file)) Assert.Skip("The test folder is not on NTFS.");
            File.Encrypt(file);
            var before = (File.GetLastWriteTimeUtc(file), File.GetLastAccessTimeUtc(file), File.GetAttributes(file));
            var report = Read(file, privileged: false);
            var efs = Section(report, "Encryption (EFS)");
            TestContext.Current.TestOutputHelper?.WriteLine(Excerpt(report, "Encryption (EFS)"));
            string user = WindowsIdentity.GetCurrent().User!.Value;
            Assert.StartsWith("yes (EFS)", Field(efs, "Encrypted"), StringComparison.Ordinal);
            Assert.StartsWith("1 user", Field(efs, "Can decrypt it"), StringComparison.Ordinal);
            var row = Assert.Single(efs.Table!.Rows, r => r[0] == "user");
            Assert.EndsWith($"({user})", row[3], StringComparison.Ordinal);
            Assert.Matches("^[0-9A-F]{40,}$", row[1]);
            // The thumbprint is that of this user's EFS certificate (enhanced key usage 1.3.6.1.4.1.311.10.3.4).
            using (var store = new X509Store(StoreName.My, StoreLocation.CurrentUser))
            {
                store.Open(OpenFlags.ReadOnly);
                var efsCertificates = store.Certificates.Where(c => c.Extensions.OfType<X509EnhancedKeyUsageExtension>()
                    .Any(e => e.EnhancedKeyUsages.Cast<System.Security.Cryptography.Oid>().Any(o => o.Value == "1.3.6.1.4.1.311.10.3.4"))).ToList();
                Assert.Contains(efsCertificates, c => string.Equals(c.Thumbprint, row[1], StringComparison.OrdinalIgnoreCase));
            }
            // Reading who can decrypt it changed nothing about it.
            Assert.Equal(before, (File.GetLastWriteTimeUtc(file), File.GetLastAccessTimeUtc(file), File.GetAttributes(file)));
            // As administrator, the record holds the EFS data the API read: $LOGGED_UTILITY_STREAM named $EFS.
            if (Environment.IsPrivilegedProcess)
                Assert.Contains(Section(Read(file), "MFT record").Table!.Rows, r => r[1] == "$LOGGED_UTILITY_STREAM" && r[2] == "$EFS");

            // An encrypted folder: what is made in it is encrypted.
            string folder = Directory.CreateDirectory(Path.Combine(dir, "sealed")).FullName;
            File.Encrypt(folder);
            var sealedReport = Read(folder, privileged: Environment.IsPrivilegedProcess);
            TestContext.Current.TestOutputHelper?.WriteLine(Excerpt(sealedReport, "Encryption (EFS)", "MFT record"));
            var sealedEfs = Section(sealedReport, "Encryption (EFS)");
            Assert.Equal("yes (EFS): files made in it are encrypted", Field(sealedEfs, "Encrypted"));
            Assert.StartsWith("1 user", Field(sealedEfs, "Encrypted for"), StringComparison.Ordinal);
            Assert.EndsWith($"({user})", Assert.Single(sealedEfs.Table!.Rows, r => r[0] == "user")[3], StringComparison.Ordinal);
            // As administrator: its $EFS data in the record, and when it is kept in clusters of its own, their runs.
            if (Environment.IsPrivilegedProcess)
            {
                var record = Section(sealedReport, "MFT record");
                var efsRow = Assert.Single(record.Table!.Rows, r => r[1] == "$LOGGED_UTILITY_STREAM" && r[2] == "$EFS");
                if (efsRow[3] != "resident")
                    Assert.Contains(record.Children.Single(c => c.Title == "Runs of its non-resident attributes").Children,
                        c => c.Title.StartsWith("$LOGGED_UTILITY_STREAM:$EFS (", StringComparison.Ordinal));
            }
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
