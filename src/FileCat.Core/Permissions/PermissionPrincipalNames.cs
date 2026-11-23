using FileCat.Core.FileSystem;
using System.Globalization;

namespace FileCat.Core.Permissions;

/// <summary>Exact, on-demand display-name resolution for principals from the local permissions adapter.</summary>
public sealed class PermissionPrincipalNames : IDisposable
{
    private readonly PrincipalNames _local;

    internal PermissionPrincipalNames(IOwnership ownership, Func<Principal, PrincipalName>? darwinUuidLookup = null) =>
        _local = new PrincipalNames(new LookupSource(ownership, darwinUuidLookup), () => Environment.TickCount64, maxQueued: 25, maxWorkers: 2);

    public async Task<PrincipalName> ResolveAsync(PermissionAuthority authority, Principal principal, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (authority.Kind != PermissionAuthorityKind.Local || principal.Authority != authority.Id)
            return new(NameStatus.Unavailable, Reason: "A name must be resolved by the authority that returned the principal.");
        if (principal.Id.Length is 0 or > 184)
            return new(NameStatus.Unavailable, Reason: "The principal exceeds the lookup limit.");
        var key = new Principal(principal.Kind, principal.Id,
            authority.Generation.ToString(CultureInfo.InvariantCulture) + ":" + authority.Id);

        await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken).ConfigureAwait(false);
        var completion = new TaskCompletionSource<PrincipalName>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Publish()
        {
            var answer = _local.Peek(key);
            if (answer.Status is NameStatus.Resolved or NameStatus.NotFound or NameStatus.Unavailable) completion.TrySetResult(answer);
        }
        _local.Changed += Publish;
        try
        {
            var requested = _local.Request(key);
            if (requested.Status != NameStatus.Pending) return requested;
            return await completion.Task.WaitAsync(TimeSpan.FromSeconds(3), cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException) { return new(NameStatus.Unavailable, Reason: "The account service did not return a name in time."); }
        finally { _local.Changed -= Publish; }
    }

    public void Dispose() => _local.Dispose();

    private sealed class LookupSource(IOwnership ownership, Func<Principal, PrincipalName>? darwinUuidLookup) : IOwnership
    {
        public bool IsSupported => ownership.IsSupported;
        public OwnerRead ReadOwner(string path) => new(OwnerReadStatus.Unsupported);
        public OwnerRead ReadGroup(string path) => new(OwnerReadStatus.Unsupported);
        public PrincipalName LookUp(Principal key)
        {
            // Only ResolveAsync can enqueue a key, after checking the snapshot's authority.
            var local = new Principal(key.Kind, key.Id);
            return key.Kind == PrincipalKind.DarwinUuid
                ? darwinUuidLookup?.Invoke(local) ?? new(NameStatus.Unavailable, Reason: "The Darwin UUID resolver is unavailable.")
                : ownership.LookUp(local);
        }
    }
}
