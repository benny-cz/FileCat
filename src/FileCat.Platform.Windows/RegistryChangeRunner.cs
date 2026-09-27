using System.ComponentModel;
using System.Runtime.InteropServices;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Platform.Windows;

/// <summary>Where a Registry change records its steps: the job journal in FileCat, the result log in the elevated broker.</summary>
public interface IRegistryStepLog
{
    int Intent(string op, string path, string? target = null);
    void Done(int step, StepOutcome outcome, string? message = null);
    void Issue(JobIssue issue);
}

/// <summary>
/// Applies one captured Registry change with its guards (expected prior state, link refusal, subtree fingerprints)
/// and returns the guarded inverse that undoes it, or null when the change cannot be reverted (deleted subtrees).
/// Shared by the job executor and the per-plan elevated broker, so both have identical semantics.
/// </summary>
public static partial class RegistryChangeRunner
{
    public static RegistryChange? Apply(RegistryChange change, IRegistryStepLog log, Action checkpoint, CancellationToken ct)
    {
        if (change.Key.Scheme != Schemes.Registry || change.TargetKey is { Scheme: not Schemes.Registry })
            throw new ArgumentException("Registry plans require Registry locations.");
        ValidateName(change.Name, change.Action is RegistryAction.CreateKey or RegistryAction.RenameKey or RegistryAction.DeleteKey or RegistryAction.CopyKey);
        if (change.TargetName is { } targetName) ValidateName(targetName, change.Action is RegistryAction.RenameKey or RegistryAction.CopyKey);
        checkpoint();
        return change.Action switch
        {
            RegistryAction.SetValue => SetValue(change, log),
            RegistryAction.DeleteValue => DeleteValue(change, log),
            RegistryAction.CreateKey => CreateKey(change, log, ct),
            RegistryAction.RenameKey => RenameKey(change, log),
            RegistryAction.CopyValue => CopyValue(change, log, removeSource: false),
            RegistryAction.RenameValue => CopyValue(change, log, removeSource: true),
            RegistryAction.CopyKey => CopyKey(change, log, checkpoint, ct),
            RegistryAction.DeleteKey => DeleteKey(change, log, checkpoint, ct),
            _ => throw new NotSupportedException($"{change.Action} is not validated for Registry jobs."),
        };
    }

    /// <summary>One line for journals, issues, and plan displays.</summary>
    public static string Describe(RegistryChange c)
    {
        string Value(Location key, string name) => key.Path + "\\" + (name.Length == 0 ? "(Default)" : name);
        return c.Action switch
        {
            RegistryAction.SetValue => $"{(c.Expected is null ? "Create" : "Set")} value {Value(c.Key, c.Name)}",
            RegistryAction.DeleteValue => $"Delete value {Value(c.Key, c.Name)}",
            RegistryAction.CreateKey => $"Create key {c.Key.Path}\\{c.Name}",
            RegistryAction.RenameKey => $"Rename key {c.Key.Path}\\{c.Name} to {c.TargetName}",
            RegistryAction.CopyValue => $"Copy value {Value(c.Key, c.Name)} to {Value(c.TargetKey!, c.TargetName ?? c.Name)}",
            RegistryAction.RenameValue => $"Rename value {Value(c.Key, c.Name)} to {(c.TargetName is { Length: > 0 } n ? n : "(Default)")}",
            RegistryAction.CopyKey => $"Copy key {c.Key.Path}\\{c.Name} to {c.TargetKey?.Path}\\{c.TargetName}",
            RegistryAction.DeleteKey => $"Delete key {c.Key.Path}\\{c.Name} and its subtree",
            _ => c.Action.ToString(),
        } + (c.Key.Session is "32" or "64" ? $" ({WindowsRegistryProvider.ViewLabel(c.Key.Session)})" : "");
    }

    private static Location Child(Location key, string name) => key.WithPath(key.Path.Length == 0 ? name : key.Path + "\\" + name);

    private static RegistryChange? SetValue(RegistryChange c, IRegistryStepLog log)
    {
        var desired = c.Desired ?? throw new ArgumentException("The new value is missing.");
        using var key = WindowsRegistryProvider.Open(c.Key, writable: true);
        RequireExpected(key, c.Name, c.Expected);
        int step = log.Intent("reg-set", c.Key.ToString(), c.Name);
        RegistryRaw.Set(key, c.Name, desired.Type, desired.Data);
        Verify(key, c.Name, desired);
        log.Done(step, StepOutcome.Committed);
        return c.Expected is null
            ? new RegistryChange(RegistryAction.DeleteValue, c.Key, c.Name, Expected: desired)
            : new RegistryChange(RegistryAction.SetValue, c.Key, c.Name, Expected: desired, Desired: c.Expected);
    }

    private static RegistryChange? DeleteValue(RegistryChange c, IRegistryStepLog log)
    {
        if (c.Expected is null) throw new ArgumentException("Deletion requires the captured original value.");
        using var key = WindowsRegistryProvider.Open(c.Key, writable: true);
        RequireExpected(key, c.Name, c.Expected);
        int step = log.Intent("reg-delete-value", c.Key.ToString(), c.Name);
        RegistryRaw.Delete(key, c.Name);
        if (RegistryRaw.ReadIfPresent(key, c.Name) is not null) throw new IOException("Deletion could not be verified.");
        log.Done(step, StepOutcome.Committed);
        return new RegistryChange(RegistryAction.SetValue, c.Key, c.Name, Expected: null, Desired: c.Expected);
    }

    private static RegistryChange? CreateKey(RegistryChange c, IRegistryStepLog log, CancellationToken ct)
    {
        using var parent = WindowsRegistryProvider.Open(c.Key, writable: true);
        if (RegistryRaw.SubKeyExists(parent, c.Name)) throw new RegistryConflictException("The key already exists.");
        int step = log.Intent("reg-create-key", c.Key.ToString(), c.Name);
        using (RegistryRaw.CreateNewKey(parent, c.Name, c.Key.Session)) { }
        log.Done(step, StepOutcome.Committed);
        // Undo removes the key only while it is still exactly as created (empty).
        return new RegistryChange(RegistryAction.DeleteKey, c.Key, c.Name,
            TreeDigest: RegistryTree.Digest(RegistryTree.Scan(Child(c.Key, c.Name), ct)));
    }

    private static RegistryChange? RenameKey(RegistryChange c, IRegistryStepLog log)
    {
        if (c.TargetName is null) throw new ArgumentException("The new key name is missing.");
        using var parent = WindowsRegistryProvider.Open(c.Key, writable: true);
        if (RegistryRaw.LinkTarget(parent, c.Name) is not null)
            throw new NotSupportedException("Registry links are not renamed here: renaming could change the link's target instead.");
        if (RegistryRaw.SubKeyExists(parent, c.TargetName)) throw new RegistryConflictException("The destination key already exists.");
        int step = log.Intent("reg-rename-key", Child(c.Key, c.Name).ToString(), c.TargetName);
        int code = RegRenameKey(parent.Handle, c.Name, c.TargetName);
        if (code != 0) throw new Win32Exception(code);
        if (!RegistryRaw.SubKeyExists(parent, c.TargetName)) throw new IOException("Rename result could not be verified.");
        log.Done(step, StepOutcome.Committed);
        return new RegistryChange(RegistryAction.RenameKey, c.Key, c.TargetName, TargetName: c.Name);
    }

    private static RegistryChange? CopyValue(RegistryChange c, IRegistryStepLog log, bool removeSource)
    {
        if (c.TargetKey is null || c.TargetName is null || c.Expected is null)
            throw new ArgumentException("Copy requires source snapshot and destination.");
        using var source = WindowsRegistryProvider.Open(c.Key, writable: removeSource);
        using var target = WindowsRegistryProvider.Open(c.TargetKey, writable: true);
        RequireExpected(source, c.Name, c.Expected);
        RequireExpected(target, c.TargetName, null);
        int copy = log.Intent("reg-copy-value", c.Key.ToString() + "/" + c.Name, c.TargetKey.ToString() + "/" + c.TargetName);
        RegistryRaw.Set(target, c.TargetName, c.Expected.Type, c.Expected.Data);
        Verify(target, c.TargetName, c.Expected);
        log.Done(copy, StepOutcome.Committed);
        var removeCopy = new RegistryChange(RegistryAction.DeleteValue, c.TargetKey, c.TargetName, Expected: c.Expected);
        if (!removeSource) return removeCopy;
        try
        {
            RequireExpected(source, c.Name, c.Expected);
            int delete = log.Intent("reg-rename-delete-source", c.Key.ToString(), c.Name);
            RegistryRaw.Delete(source, c.Name);
            log.Done(delete, StepOutcome.Committed);
            return new RegistryChange(RegistryAction.RenameValue, c.TargetKey, c.TargetName, c.Expected, TargetKey: c.Key, TargetName: c.Name);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception)
        {
            log.Issue(new JobIssue(IssueSeverity.Error, c.Key.ToString(),
                $"The new value was created, but the source remains: {ex.Message}", StepOutcome.PartiallyApplied));
            return removeCopy;
        }
    }

    private static RegistryChange? CopyKey(RegistryChange c, IRegistryStepLog log, Action checkpoint, CancellationToken ct)
    {
        if (c.TargetKey is null || c.TargetName is null) throw new ArgumentException("A destination key and name are required.");
        var sourceRoot = Child(c.Key, c.Name);
        var snapshot = RegistryTree.Scan(sourceRoot, ct);
        if (snapshot.LinkCount > 0) throw new NotSupportedException("This subtree contains Registry links. It was not copied; links are never traversed implicitly.");
        if (c.TreeDigest is null || !string.Equals(c.TreeDigest, RegistryTree.Digest(snapshot), StringComparison.Ordinal))
            throw new RegistryConflictException("The source subtree changed since confirmation. Review it again.");
        using (var targetParent = WindowsRegistryProvider.Open(c.TargetKey, writable: true))
            if (RegistryRaw.SubKeyExists(targetParent, c.TargetName)) throw new RegistryConflictException("The destination key already exists.");
        var targetRoot = Child(c.TargetKey, c.TargetName);
        int step = log.Intent("reg-copy-key", sourceRoot.ToString(), targetRoot.ToString());
        int created = 0;
        try
        {
            foreach (var entry in snapshot.Keys)
            {
                checkpoint();
                var relative = entry.RelativePath;
                var sourceLocation = relative.Length == 0 ? sourceRoot : Child(sourceRoot, relative);
                var targetLocation = relative.Length == 0 ? targetRoot : Child(targetRoot, relative);
                using var source = WindowsRegistryProvider.Open(sourceLocation, false);
                if (RegistryTree.ValueHash(source) != entry.ValueHash)
                    throw new RegistryConflictException("Source values changed while copying. The partial destination was kept for review.");
                int slash = targetLocation.Path.LastIndexOf('\\');
                using var parent = WindowsRegistryProvider.Open(targetLocation.WithPath(targetLocation.Path[..slash]), writable: true);
                using var target = RegistryRaw.CreateNewKey(parent, targetLocation.Path[(slash + 1)..], c.TargetKey.Session);
                created++;
                foreach (var name in RegistryRaw.ValueNames(source))
                {
                    var value = RegistryRaw.Read(source, name);
                    if (value.Data.Length != value.Length) throw new IOException("A value is too large to copy safely.");
                    RegistryRaw.Set(target, name, value.Type, value.Data);
                    var verified = RegistryRaw.Read(target, name);
                    if (verified.Type != value.Type || !verified.Data.AsSpan().SequenceEqual(value.Data))
                        throw new IOException("Copied Registry value could not be verified.");
                }
            }
            if (RegistryTree.Digest(RegistryTree.Scan(sourceRoot, ct)) != c.TreeDigest)
                throw new RegistryConflictException("Source changed during copy. The destination is a partial snapshot; source was kept.");
            log.Done(step, StepOutcome.Committed);
            return new RegistryChange(RegistryAction.DeleteKey, c.TargetKey, c.TargetName,
                TreeDigest: RegistryTree.Digest(RegistryTree.Scan(targetRoot, ct)));
        }
        catch
        {
            log.Done(step, created > 0 ? StepOutcome.PartiallyApplied : StepOutcome.Failed, $"{created} of {snapshot.KeyCount} keys created");
            if (created > 0) log.Issue(new JobIssue(IssueSeverity.Error, targetRoot.ToString(),
                $"Created {created} of {snapshot.KeyCount} destination keys. The source was kept; inspect the destination before retrying.", StepOutcome.PartiallyApplied));
            throw;
        }
    }

    private static RegistryChange? DeleteKey(RegistryChange c, IRegistryStepLog log, Action checkpoint, CancellationToken ct)
    {
        var root = Child(c.Key, c.Name);
        var snapshot = RegistryTree.Scan(root, ct);
        if (c.TreeDigest is null || !string.Equals(c.TreeDigest, RegistryTree.Digest(snapshot), StringComparison.Ordinal))
            throw new RegistryConflictException("The key subtree changed since confirmation. Review its scope again.");
        int step = log.Intent("reg-delete-key", root.ToString());
        int removed = 0;
        try
        {
            // Children before parents. Each key is opened as itself (a link as the link), checked through that same
            // handle, and deleted through it, so a key swapped for a link cannot redirect the deletion.
            foreach (var entry in snapshot.Keys.Reverse())
            {
                checkpoint();
                var location = entry.RelativePath.Length == 0 ? root : Child(root, entry.RelativePath);
                int slash = location.Path.LastIndexOf('\\');
                using var parent = WindowsRegistryProvider.Open(location.WithPath(location.Path[..slash]), writable: false);
                using var victim = RegistryRaw.OpenForDelete(parent, location.Path[(slash + 1)..]);
                bool link = RegistryRaw.IsLink(victim);
                if (entry.IsLink != link) throw new RegistryConflictException("A key changed into or out of a Registry link during deletion.");
                if (!link && RegistryTree.ValueHash(victim) != entry.ValueHash)
                    throw new RegistryConflictException("A key's values changed during deletion. Remaining keys were kept.");
                RegistryRaw.DeleteOpenKey(victim);
                removed++;
            }
            log.Done(step, StepOutcome.Committed);
            return null;
        }
        catch
        {
            log.Done(step, removed > 0 ? StepOutcome.PartiallyApplied : StepOutcome.Failed, $"{removed} of {snapshot.KeyCount} keys removed");
            if (removed > 0) log.Issue(new JobIssue(IssueSeverity.Error, root.ToString(),
                $"Deleted {removed} of {snapshot.KeyCount} keys before stopping. Inspect the remaining subtree.", StepOutcome.PartiallyApplied));
            throw;
        }
    }

    private static void RequireExpected(RegistryKey key, string name, RegistryValueSnapshot? expected)
    {
        var actual = RegistryRaw.ReadIfPresent(key, name);
        if (actual is not null && actual.Data.Length != actual.Length)
            throw new IOException("The current value is too large to compare safely.");
        if (expected is null ? actual is not null : actual is null || actual.Type != expected.Type || !actual.Data.AsSpan().SequenceEqual(expected.Data))
            throw new RegistryConflictException(expected is null
                ? "A value with this name exists now. Refresh and review it before retrying."
                : "The Registry value changed since it was read. Refresh and review it before retrying.");
    }

    private static void Verify(RegistryKey key, string name, RegistryValueSnapshot desired)
    {
        var actual = RegistryRaw.ReadIfPresent(key, name);
        if (actual is null || actual.Type != desired.Type || !actual.Data.AsSpan().SequenceEqual(desired.Data))
            throw new IOException("The Registry write could not be verified. Inspect the value before retrying.");
    }

    private static void ValidateName(string name, bool key)
    {
        if (name.Contains('\0') || name.Length > 16383 || key && (name.Length == 0 || name.Contains('\\')))
            throw new ArgumentException("Invalid Registry name.");
    }

    /// <summary>Access denied, which an administrator retry may resolve (as opposed to conflicts or missing keys).</summary>
    public static bool IsAccessDenied(Exception ex) =>
        ex is UnauthorizedAccessException or System.Security.SecurityException || ex is Win32Exception { NativeErrorCode: 5 };

    [LibraryImport("advapi32.dll", EntryPoint = "RegRenameKey", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int RegRenameKey(SafeRegistryHandle key, string subKeyName, string newName);
}
