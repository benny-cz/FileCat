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
        if (change.TargetName is { } targetName) ValidateName(targetName, change.Action == RegistryAction.RenameKey);
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
            default: throw new NotSupportedException($"{change.Action} is not validated for Registry jobs.");
        }
        job.ItemDone();
        job.RootCompleted(0);
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
        using var created = parent.CreateSubKey(c.Name, writable: true) ?? throw new IOException("The key could not be created.");
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
