using System.Collections.Immutable;
using FileCat.Core.FileSystem;

namespace FileCat.Core.Jobs;

/// <summary>Separate choices; neither acknowledgment starts selected or substitutes for key/output approval.</summary>
public sealed record StorageEfsAcknowledgments(bool AcceptConcurrentLinkChange = false, bool AcceptAllStreamDecompression = false);

/// <summary>Immutable evidence for one displayed EFS scope. This record alone grants no mutation authority.</summary>
public sealed record StorageEfsConsent(Guid ConsentId, Guid ReviewRevision, StorageAction Action, StorageScope Scope,
    ImmutableArray<StorageObjectIdentity> RootIdentities, StorageEfsAcknowledgments Acknowledgments)
{
    internal static StorageEfsConsent Capture(Guid revision, StorageAction action, StorageScope scope,
        ImmutableArray<StorageObjectIdentity> identities, StorageEfsAcknowledgments acknowledgments)
    {
        var consent = new StorageEfsConsent(Guid.NewGuid(), revision, action, scope, identities, acknowledgments);
        Require(consent, revision, action, scope, identities);
        return consent;
    }

    internal static void Require(StorageEfsConsent? consent, Guid revision, StorageAction action, StorageScope scope,
        ImmutableArray<StorageObjectIdentity> identities)
    {
        if (action is StorageAction.Compress or StorageAction.RemoveCompression)
        {
            if (consent is not null) throw new ArgumentException("Compression cannot carry an EFS consent.");
            return;
        }
        if (action is not (StorageAction.EncryptEfs or StorageAction.DecryptEfs) || revision == Guid.Empty || !Enum.IsDefined(scope) ||
            consent is null || consent.ConsentId == Guid.Empty || consent.ReviewRevision != revision || consent.Action != action || consent.Scope != scope ||
            identities.IsDefault || identities.Length is < 1 or > 64 || consent.RootIdentities.IsDefault ||
            !consent.RootIdentities.SequenceEqual(identities) || consent.Acknowledgments is not { AcceptConcurrentLinkChange: true } ||
            consent.Acknowledgments.AcceptAllStreamDecompression != (action == StorageAction.EncryptEfs))
            throw new ArgumentException("EFS needs fresh, separate acknowledgments for the exact displayed action, scope, revision and identities.");
        foreach (var identity in identities)
            if (identity is null || identity.ObjectId is not { Length: 32 } || identity.ObjectId.Any(c => !char.IsAsciiHexDigitUpper(c)) ||
                identity.VolumeBinding is not { Length: <= 128 } binding || !Guid.TryParse(binding, out _))
                throw new ArgumentException("EFS consent requires complete native object and volume bindings.");
    }
}
