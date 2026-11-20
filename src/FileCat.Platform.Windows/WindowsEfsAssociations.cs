using System.Collections.Immutable;
using System.ComponentModel;
using System.Runtime.InteropServices;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;

namespace FileCat.Platform.Windows;

/// <summary>Complete bounded user associations. The caller supplies a continuously held, validated path binding.</summary>
internal static unsafe partial class WindowsEfsAssociations
{
    internal static StorageEfsAssociationSnapshot Read(string apiPath, StorageObjectIdentity identity, string invokerSid,
        WindowsEfsKeys keys, Action<CancellationToken> validate, CancellationToken ct)
    {
        validate(ct); ct.ThrowIfCancellationRequested();
        ImmutableArray<StorageEfsCertificateAssociation> associations;
        using (StorageNativeAllowance.RequireCurrent().ClaimScratch())
        {
            uint error = QueryUsers(apiPath, out nint users);
            try
            {
                ct.ThrowIfCancellationRequested(); validate(ct);
                if (error != 0) throw Failure("efs-users-" + error, "The actual EFS users could not be read: " + new Win32Exception((int)error).Message);
                if (users == 0) throw Failure("efs-users-empty", "Windows returned no complete EFS certificate association.");
                var header = *(WindowsFileRecords.EfsHashList*)users;
                if (header.Count is 0 or > WindowsFileRecords.MaxEfsEntries || header.Entries == 0)
                    throw Failure("efs-users-limit", "The complete EFS user list is empty, incomplete or exceeds its 256-entry limit.");
                var problems = new List<string>();
                var entries = WindowsFileRecords.ReadEfsList(users, problems);
                if (problems.Count != 0 || entries.Count != header.Count || entries.Any(entry => entry.Thumbprint.Length != 40 || entry.Thumbprint.Any(c => !char.IsAsciiHexDigitUpper(c))))
                    throw Failure("efs-users-incomplete", "The actual EFS user associations could not be established completely; no partial list authorizes setup.");
                associations = entries.Select(entry => new StorageEfsCertificateAssociation(entry.Sid, entry.Thumbprint, entry.Display)).ToImmutableArray();
            }
            finally { if (users != 0) FreeUsers(users); }
        }
        var evidence = ImmutableArray.CreateBuilder<StorageEfsKeyEvidence>(); var checkedHashes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var association in associations)
        {
            ct.ThrowIfCancellationRequested(); validate(ct);
            if (association.Sid == invokerSid && checkedHashes.Add(association.CertificateSha1))
                evidence.Add(keys.ProbeAssociatedCertificate(invokerSid, association.CertificateSha1, ct));
        }
        validate(ct); ct.ThrowIfCancellationRequested();
        return new(identity, invokerSid, associations, evidence.ToImmutable());
    }
    private static StorageInspectionException Failure(string code, string message) => new(new(StorageReasonKind.Unknown, code, message, true));
    [LibraryImport("advapi32.dll", EntryPoint = "QueryUsersOnEncryptedFile", StringMarshalling = StringMarshalling.Utf16)] private static partial uint QueryUsers(string file, out nint users);
    [LibraryImport("advapi32.dll", EntryPoint = "FreeEncryptionCertificateHashList")] private static partial void FreeUsers(nint users);
}
