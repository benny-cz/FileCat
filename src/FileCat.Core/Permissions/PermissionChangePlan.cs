using System.Collections.Immutable;

namespace FileCat.Core.Permissions;

internal sealed record PermissionChangeStage(PermissionDelta Change, PermissionSnapshot Expected, string NativeCall);
internal sealed record PermissionChangePlan(PermissionSnapshot Expected, ImmutableArray<PermissionChangeStage> Stages, bool NeedsSetIdConsent);
