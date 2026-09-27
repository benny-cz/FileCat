using System.ComponentModel;
using System.Runtime.InteropServices;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Platform.Windows;

/// <summary>One narrow, captured Registry plan per job. Every step records intent before mutating.</summary>
internal sealed partial class RegistryExecutor(Job job, JobJournal journal) : IJobExecutor
{
    public void Execute()
    {
        var change = job.Request.Registry ?? throw new InvalidOperationException("Missing Registry plan.");
        if (change.Key.Scheme != Schemes.Registry || change.TargetKey is { Scheme: not Schemes.Registry })
            throw new ArgumentException("Registry plans require Registry locations.");
        ValidateName(change.Name, change.Action is RegistryAction.CreateKey or RegistryAction.RenameKey or RegistryAction.DeleteKey);
        if (change.TargetName is { } targetName) ValidateName(targetName, change.Action is RegistryAction.RenameKey or RegistryAction.CopyKey);
        job.AddTotals(1, change.Desired?.Data.Length ?? 0);
        job.Checkpoint();
        switch (change.Action)
        {
            case RegistryAction.SetValue: SetValue(change); break;
            case RegistryAction.DeleteValue: DeleteValue(change); break;
            case RegistryAction.CreateKey: CreateKey(change); break;
            case RegistryAction.RenameKey: RenameKey(change); break;
            case RegistryAction.CopyValue: CopyValue(change, removeSource: false); break;
            case RegistryAction.RenameValue: CopyValue(change, removeSource: true); break;
            case RegistryAction.CopyKey: CopyKey(change); break;
            case RegistryAction.DeleteKey: DeleteKey(change); break;
            default: throw new NotSupportedException($"{change.Action} is not validated for Registry jobs.");
        }
        job.ItemDone();
        job.RootCompleted(0);
    }

    private void DeleteKey(RegistryChange c)
    {
        var root = c.Key.WithPath(c.Key.Path + "\\" + c.Name);
        var snapshot = RegistryTree.Scan(root, job.Token);
        if (c.TreeDigest is null || !string.Equals(c.TreeDigest, RegistryTree.Digest(snapshot), StringComparison.Ordinal))
            throw new RegistryConflictException("The key subtree changed since confirmation. Review its scope again.");
        int step = journal.Intent("reg-delete-key", root.ToString());
        int removed = 0;
        try
        {
            foreach (var entry in snapshot.Keys.Reverse())
            {
                job.Checkpoint();
                var location = entry.RelativePath.Length == 0 ? root : root.WithPath(root.Path + "\\" + entry.RelativePath);
                var slash = location.Path.LastIndexOf('\\');
                var parentLocation = location.WithPath(location.Path[..slash]);
                var name = location.Path[(slash + 1)..];
                using var parent = WindowsRegistryProvider.Open(parentLocation, writable: true);
                var linkNow = RegistryRaw.LinkTarget(parent, name);
                if (entry.IsLink != (linkNow is not null)) throw new RegistryConflictException("A key changed into or out of a Registry link during deletion.");
                if (!entry.IsLink && RegistryTree.ValueHash(location) != entry.ValueHash)
                    throw new RegistryConflictException("A key's values changed during deletion. Remaining keys were kept.");
                RegistryRaw.DeleteKey(parent, name, c.Key.Session);
                removed++;
            }
            journal.Done(step, StepOutcome.Committed);
        }
        catch
        {
            journal.Done(step, removed > 0 ? StepOutcome.PartiallyApplied : StepOutcome.Failed,
                $"{removed} of {snapshot.KeyCount} keys removed");
            if (removed > 0) job.AddIssue(new JobIssue(IssueSeverity.Error, root.ToString(),
                $"Deleted {removed} of {snapshot.KeyCount} keys before stopping. Inspect the remaining subtree.", StepOutcome.PartiallyApplied));
            throw;
        }
    }

    private void CopyKey(RegistryChange c)
    {
        if (c.TargetKey is null || c.TargetName is null) throw new ArgumentException("A destination key and name are required.");
        var sourceRoot = c.Key.WithPath(c.Key.Path + "\\" + c.Name);
        var snapshot = RegistryTree.Scan(sourceRoot, job.Token);
        if (snapshot.LinkCount > 0) throw new NotSupportedException("This subtree contains Registry links. It was not copied; links are never traversed implicitly.");
        if (c.TreeDigest is null || !string.Equals(c.TreeDigest, RegistryTree.Digest(snapshot), StringComparison.Ordinal))
            throw new RegistryConflictException("The source subtree changed since confirmation. Review it again.");
        using var targetParent = WindowsRegistryProvider.Open(c.TargetKey, writable: true);
        using (var existing = targetParent.OpenSubKey(c.TargetName))
            if (existing is not null) throw new RegistryConflictException("The destination key already exists.");
        var targetRoot = c.TargetKey.WithPath(c.TargetKey.Path + "\\" + c.TargetName);
        int step = journal.Intent("reg-copy-key", sourceRoot.ToString(), targetRoot.ToString());
        int created = 0;
        try
        {
            foreach (var entry in snapshot.Keys)
            {
                job.Checkpoint();
                var relative = entry.RelativePath;
                var sourceLocation = relative.Length == 0 ? sourceRoot : sourceRoot.WithPath(sourceRoot.Path + "\\" + relative);
                var targetLocation = relative.Length == 0 ? targetRoot : targetRoot.WithPath(targetRoot.Path + "\\" + relative);
                using var source = WindowsRegistryProvider.Open(sourceLocation, false);
                if (RegistryTree.ValueHash(sourceLocation) != entry.ValueHash)
                    throw new RegistryConflictException("Source values changed while copying. The partial destination was kept for review.");
                var slash = targetLocation.Path.LastIndexOf('\\');
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
            var after = RegistryTree.Scan(sourceRoot, job.Token);
            if (RegistryTree.Digest(after) != c.TreeDigest)
                throw new RegistryConflictException("Source changed during copy. The destination is a partial snapshot; source was kept.");
            journal.Done(step, StepOutcome.Committed);
        }
        catch
        {
            journal.Done(step, created > 0 ? StepOutcome.PartiallyApplied : StepOutcome.Failed,
                $"{created} of {snapshot.KeyCount} keys created");
            if (created > 0) job.AddIssue(new JobIssue(IssueSeverity.Error, targetRoot.ToString(),
                $"Created {created} of {snapshot.KeyCount} destination keys. The source was kept; inspect the destination before retrying.", StepOutcome.PartiallyApplied));
            throw;
        }
    }

    private void SetValue(RegistryChange c)
    {
        var desired = c.Desired ?? throw new ArgumentException("The new value is missing.");
        using var key = WindowsRegistryProvider.Open(c.Key, writable: true);
        RequireExpected(key, c.Name, c.Expected);
        int step = journal.Intent("reg-set", c.Key.ToString(), c.Name);
        RegistryRaw.Set(key, c.Name, desired.Type, desired.Data);
        Verify(key, c.Name, desired);
        journal.Done(step, StepOutcome.Committed);
    }

    private void DeleteValue(RegistryChange c)
    {
        if (c.Expected is null) throw new ArgumentException("Deletion requires the captured original value.");
        using var key = WindowsRegistryProvider.Open(c.Key, writable: true);
        RequireExpected(key, c.Name, c.Expected);
        int step = journal.Intent("reg-delete-value", c.Key.ToString(), c.Name);
        RegistryRaw.Delete(key, c.Name);
        if (RegistryRaw.ReadIfPresent(key, c.Name) is not null) throw new IOException("Deletion could not be verified.");
        journal.Done(step, StepOutcome.Committed);
    }

    private void CreateKey(RegistryChange c)
    {
        using var parent = WindowsRegistryProvider.Open(c.Key, writable: true);
        using (var existing = parent.OpenSubKey(c.Name))
            if (existing is not null) throw new RegistryConflictException("The key already exists.");
        int step = journal.Intent("reg-create-key", c.Key.ToString(), c.Name);
        using var created = RegistryRaw.CreateNewKey(parent, c.Name, c.Key.Session);
        journal.Done(step, StepOutcome.Committed);
    }

    private void RenameKey(RegistryChange c)
    {
        if (c.TargetName is null) throw new ArgumentException("The new key name is missing.");
        using var parent = WindowsRegistryProvider.Open(c.Key, writable: true);
        using (var existing = parent.OpenSubKey(c.TargetName))
            if (existing is not null) throw new RegistryConflictException("The destination key already exists.");
        int step = journal.Intent("reg-rename-key", c.Key.ToString() + "\\" + c.Name, c.TargetName);
        int code = RegRenameKey(parent.Handle, c.Name, c.TargetName);
        if (code != 0) throw new Win32Exception(code);
        using var renamed = parent.OpenSubKey(c.TargetName) ?? throw new IOException("Rename result could not be verified.");
        journal.Done(step, StepOutcome.Committed);
    }

    private void CopyValue(RegistryChange c, bool removeSource)
    {
        if (c.TargetKey is null || c.TargetName is null || c.Expected is null)
            throw new ArgumentException("Copy requires source snapshot and destination.");
        using var source = WindowsRegistryProvider.Open(c.Key, writable: removeSource);
        using var target = WindowsRegistryProvider.Open(c.TargetKey, writable: true);
        RequireExpected(source, c.Name, c.Expected);
        RequireExpected(target, c.TargetName, null);
        int copy = journal.Intent("reg-copy-value", c.Key.ToString() + "/" + c.Name,
            c.TargetKey.ToString() + "/" + c.TargetName);
        RegistryRaw.Set(target, c.TargetName, c.Expected.Type, c.Expected.Data);
        Verify(target, c.TargetName, c.Expected);
        journal.Done(copy, StepOutcome.Committed);
        if (!removeSource) return;
        try
        {
            RequireExpected(source, c.Name, c.Expected);
            int delete = journal.Intent("reg-rename-delete-source", c.Key.ToString(), c.Name);
            RegistryRaw.Delete(source, c.Name);
            journal.Done(delete, StepOutcome.Committed);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception or RegistryConflictException)
        {
            job.AddIssue(new JobIssue(IssueSeverity.Error, c.Key.ToString(),
                $"The new value was created, but the source remains: {ex.Message}", StepOutcome.PartiallyApplied));
        }
    }

    private static void RequireExpected(RegistryKey key, string name, RegistryValueSnapshot? expected)
    {
        var actual = RegistryRaw.ReadIfPresent(key, name);
        if (actual is not null && actual.Data.Length != actual.Length)
            throw new IOException("The current value is too large to compare safely.");
        if (expected is null ? actual is not null : actual is null || actual.Type != expected.Type || !actual.Data.AsSpan().SequenceEqual(expected.Data))
            throw new RegistryConflictException("The Registry value changed since the dialog opened. Refresh and review it before retrying.");
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

    [LibraryImport("advapi32.dll", EntryPoint = "RegRenameKey", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int RegRenameKey(SafeRegistryHandle key, string subKeyName, string newName);
}

public sealed class RegistryConflictException(string message) : IOException(message);
