using System.Collections.Immutable;

namespace FileCat.Core.FileSystem;

public enum StorageEfsRecoveryChoice { DeclaredArrangement, WindowsReportedExport }

/// <summary>Historical evidence about the reviewed setup key. It carries no authority to use a key.</summary>
public sealed record StorageEfsReviewedKey(StorageObjectIdentity SetupIdentity, string InvokerSid, string CertificateSha1,
    StorageEfsRecoveryChoice Choice);

public sealed record StorageEfsObservedKey(string CertificateSha1, StorageEfsKeyState State);

/// <summary>Relevant current-user associations observed after the native encryption attempt.</summary>
public sealed record StorageEfsResultKeys(bool UserAssociationsEstablished, ImmutableArray<StorageEfsObservedKey> Keys)
{
    internal bool Matches(StorageEfsReviewedKey reviewed) => UserAssociationsEstablished && !Keys.IsDefault && Keys.Length == 1 &&
        Keys[0].CertificateSha1 == reviewed.CertificateSha1 && Keys[0].State == StorageEfsKeyState.MatchedAvailable;

    internal static StorageEfsResultKeys FromAssociation(StorageEfsAssociationSnapshot association, string invokerSid)
    {
        var states = association.CurrentUserKeys.Where(key => key.InvokerSid == invokerSid).ToDictionary(key => key.CertificateSha1,
            key => key.State == StorageEfsKeyState.MatchedAvailable && (key.CertificatePresent != true || key.ProviderAssociationPresent != true)
                ? StorageEfsKeyState.Unknown : key.State, StringComparer.Ordinal);
        var hashes = association.Users.Where(user => user.Sid == invokerSid).Select(user => user.CertificateSha1).Distinct(StringComparer.Ordinal);
        return new(association.InvokerSid == invokerSid && association.Users.All(user => user.Sid is not null),
            hashes.Select(hash => new StorageEfsObservedKey(hash, states.GetValueOrDefault(hash, StorageEfsKeyState.Unknown))).ToImmutableArray());
    }
}
