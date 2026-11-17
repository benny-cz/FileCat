using FileCat.Core.Jobs;

namespace FileCat.Core.FileSystem;

/// <summary>Explicit E-only zero-byte setup. Call lazily for a change-needed item, inside its existing native owner.</summary>
public interface IStorageEfsSetup
{
    StorageEfsSetupResult InspectSetup(string invokerSid, StorageEfsConsent scopeConsent, bool explicitSetupAccepted, CancellationToken ct);
}
