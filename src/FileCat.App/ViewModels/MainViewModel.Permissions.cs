using System.Collections.Immutable;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.LogicalTree;
using Avalonia.Media;
using FileCat.App.Services;
using FileCat.App.Views;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Permissions;
using FileCat.Core.Resources;
using FileCat.Core.Threading;

namespace FileCat.App.ViewModels;

public sealed partial class MainViewModel
{
    private readonly Dictionary<Job, PermissionSelection> _permissionOrigins = [];
    private sealed record AttributePermissionTarget(ItemRef Source, PermissionInspection Inspection, string? Refusal);
    private int UnixAttributeBindingLimit => Services.Permissions switch
    {
        LinuxPermissionService => ReviewedPermissionOperation.TargetLimit,
        DarwinPermissionService { WritesEnabled: true } => ReviewedPermissionOperation.TargetLimit / 2, // Parent plus object or watch descriptor.
        _ => 0
    };

    private async Task ChangeUnixAttributesAsync(PermissionSelection origin, IReadOnlyList<ItemRef> selection, AttributeMetadata metadata,
        PreparationScope scope)
    {
        var targets = new List<AttributePermissionTarget>();
        bool darwin = Services.Permissions is DarwinPermissionService { WritesEnabled: true };
        string? unavailable = PermissionChangeRefusal();
        int targetLimit = UnixAttributeBindingLimit;
        if (unavailable is null && targetLimit == 0)
            unavailable = "Mode editing is unavailable until this platform can bind the reviewed object.";
        if (unavailable is null && selection.Count > targetLimit)
            unavailable = $"Select at most {targetLimit} objects to change mode.";

        try
        {
            if (unavailable is null)
                foreach (var item in selection)
                {
                    scope.Check();
                    string device = Services.Providers.For(item.Parent).GetDeviceKey(item.Parent);
                    var subject = AttributePermissionSubject(item, metadata.UnixSubjects![item]);
                    var inspection = await Services.Io.RunOwned(device, IoPriority.Interactive,
                        ct => Services.Permissions.Inspect(subject, PermissionReadSections | PermissionSections.Times, ct), scope.Stop.Token);
                    targets.Add(new(item, inspection, darwin ? DarwinPermissionService.ModeRefusal(inspection.Snapshot)
                        : LinuxPermissionService.ModeReviewRefusal(inspection.Snapshot)));
                }
            scope.Check();
            if (!origin.IsCurrent) throw new OperationCanceledException();
            // Preparation belongs to the listing until the user sees its result: from here the dialog keeps the selection it
            // shows, so a later listing change no longer closes it. A reviewed permission change still needs the original
            // marks when it applies.
            scope.Detach();
            await ShowUnixAttributesAsync(origin, selection, metadata, scope, targets, unavailable);
        }
        catch (OperationCanceledException) { }
        finally { foreach (var target in targets) target.Inspection.Dispose(); }
    }

    private static PermissionSubject AttributePermissionSubject(ItemRef item, UnixStat observed)
    {
        string path = item.FileSystemPath!;
        var kind = item.Flags.HasFlag(EntryFlags.Link) || observed.IsLink ? PermissionObjectKind.Link : item.Kind switch
        {
            EntryKind.File => PermissionObjectKind.File,
            EntryKind.Directory => PermissionObjectKind.Directory,
            _ => PermissionObjectKind.Virtual
        };
        return new(item.Parent, path, path, LocalPermissionAuthority, kind, PermissionLinkPolicy.NoFollow,
            ExpectedObject: observed.Identity,
            ListedSize: kind == PermissionObjectKind.File && item.Size >= 0 ? item.Size : null,
            ListedModifiedUtcTicks: kind == PermissionObjectKind.File && item.Modified > 0 ? item.Modified : null);
    }

    private async Task ShowUnixAttributesAsync(PermissionSelection origin, IReadOnlyList<ItemRef> selection, AttributeMetadata metadata,
        PreparationScope scope, IReadOnlyList<AttributePermissionTarget> inspected, string? unavailable)
    {
        bool mac = OperatingSystem.IsMacOS();
        var admitted = inspected.Where(target => target.Refusal is null).ToArray();
        var refused = inspected.Where(target => target.Refusal is not null).ToArray();
        bool treeAvailable = admitted.Any(target => CanReviewModeTree(target.Inspection.Snapshot));
        bool singleTreeMount = !treeAvailable || admitted.Select(target => target.Inspection.Snapshot.Binding!.Identity.MountInstance).Distinct().Count() == 1;
        bool? InitialBit(UnixFileMode bit)
        {
            if (admitted.Length == 0) return metadata.State(bit);
            bool first = (admitted[0].Inspection.Snapshot.Mode.Value!.Value & (ushort)bit) != 0;
            return admitted.All(target => ((target.Inspection.Snapshot.Mode.Value!.Value & (ushort)bit) != 0) == first) ? first : null;
        }

        var editor = new UnixPermissionModeEditor(InitialBit, admitted.Any(target =>
            target.Inspection.Snapshot.Access.Value?.Acl is PosixAcl acl && acl.Entries.Any(row => row.Tag == PosixAclTag.Mask)))
        { IsEnabled = admitted.Length > 0 };
        var includeBound = new CheckBox { Content = "Apply the combined change to bound items only", IsVisible = admitted.Length > 0 && refused.Length > 0 };
        var acceptSetId = new CheckBox { Content = "Accept clearing the reviewed set-ID bits", IsVisible = false };
        var resetMode = new Button { Content = "Discard mode changes", IsVisible = false };
        var modeDiff = new ListBox { MaxHeight = 155, IsVisible = false };
        AutomationProperties.SetName(modeDiff, "Mode and group-class changes");
        var maskDiff = new ListBox { MaxHeight = 145, IsVisible = false };
        AutomationProperties.SetName(maskDiff, "Affected ACL entries and effective rights");
        modeDiff.SelectionChanged += (_, _) =>
        {
            var affected = (modeDiff.SelectedItem as ModeChangePreview)?.AffectedMaskEntries() ?? [];
            maskDiff.ItemsSource = affected;
            maskDiff.IsVisible = affected.Length != 0;
        };

        bool? hiddenState = metadata.FinderHidden;
        var hidden = new CheckBox { Content = "Hidden in Finder", IsChecked = hiddenState, IsThreeState = hiddenState is null, IsVisible = mac };
        DateTime? originalModified = metadata.Single?.ModifiedUtc;
        var modified = TimeEditor("Modified", originalModified);
        var created = TimeEditor("Created", metadata.Single?.CreatedUtc);
        created.IsVisible = mac;
        var recursive = new CheckBox
        {
            Content = mac ? "Also apply timestamps and Finder Hidden inside folders" : "Also apply timestamps inside folders",
            IsVisible = metadata.HasFolders
        };
        var treeMode = new CheckBox { Content = "Also change mode inside selected folders", IsVisible = treeAvailable };
        var exactExecute = new CheckBox { Content = "Set requested execute bits on regular files that have no execute bit", IsVisible = false };
        var applyTimeOnly = new Button { Content = "Apply timestamp separately", IsVisible = false };
        var validation = PermissionNotice();
        var result = PermissionNotice();
        var stop = new Button { Content = "Stop operation", IsVisible = false };
        var inspectCurrent = new Button { Content = "Review current state…", IsVisible = false };
        var footer = new DialogButtonPresentation(new("Cancel"));
        var close = new DialogButtonPresentation(new("Close permissions and times"));
        var closer = new DialogCloser();
        var dialogs = Dialogs;

        var body = new StackPanel { Spacing = 7 };
        body.Children.Add(new TextBlock
        {
            Text = selection.Count == 1 ? Formatters.SafeName(selection[0].Name) : Formatters.Plural(selection.Count, "selected object", "selected objects"),
            FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap
        });
        if (unavailable is not null)
            body.Children.Add(Wrapped(unavailable));
        else
        {
            body.Children.Add(Wrapped($"Mode changes apply to the selected objects themselves. Bound objects {admitted.Length} of {selection.Count}."));
            if (admitted.Any(target => target.Inspection.Snapshot.Binding?.DarwinRoute == DarwinBindingRoute.ParentRelative))
                body.Children.Add(Wrapped(DarwinPermissionService.ParentRelativeReviewNotice));
            if (admitted.Any(target => target.Inspection.Snapshot.Binding?.DarwinRoute == DarwinBindingRoute.Descriptor))
                body.Children.Add(Wrapped(DarwinPermissionService.HeldDescriptorReviewNotice));
            if (refused.Length > 0)
            {
                var refusals = new ListBox { MaxHeight = 120, ItemsSource = refused.Select(target =>
                    $"{Formatters.SafeName(target.Source.Name)} · {Formatters.SafeName(target.Refusal!)}").ToArray() };
                AutomationProperties.SetName(refusals, "Objects unavailable for mode changes");
                body.Children.Add(refusals);
            }
        }
        var fields = new StackPanel { Spacing = 6 };
        fields.Children.Add(editor);
        fields.Children.Add(includeBound);
        fields.Children.Add(modeDiff);
        fields.Children.Add(maskDiff);
        fields.Children.Add(acceptSetId);
        fields.Children.Add(resetMode);
        fields.Children.Add(treeMode);
        fields.Children.Add(exactExecute);
        fields.Children.Add(hidden);
        fields.Children.Add(Wrapped("Modified"));
        fields.Children.Add(modified);
        if (mac) fields.Children.Add(Wrapped("Created"));
        fields.Children.Add(created);
        fields.Children.Add(recursive);
        fields.Children.Add(applyTimeOnly);
        body.Children.Add(fields);
        body.Children.Add(validation);
        body.Children.Add(result);
        body.Children.Add(stop);
        body.Children.Add(inspectCurrent);

        bool canApply = false, complete = false, preparing = false, needsFreshBinding = false, checkingDiscard = false;
        Job? submitted = null;
        Task? applying = null;
        using var viewLifetime = CancellationTokenSource.CreateLinkedTokenSource(scope.Stop.Token, Services.Io.StoppingToken);
        CancellationToken viewToken = viewLifetime.Token;
        stop.Click += (_, _) => { submitted?.Cancel(); stop.IsEnabled = false; };
        inspectCurrent.Click += async (_, _) =>
        {
            if (!inspectCurrent.IsEnabled || viewToken.IsCancellationRequested) return;
            inspectCurrent.IsEnabled = false;
            try { await ShowCurrentAttributePermissionsAsync(dialogs, admitted, submitted, viewToken); }
            catch (OperationCanceledException) when (viewToken.IsCancellationRequested) { }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                if (!viewToken.IsCancellationRequested) ShowResult("Could not read the current state. " + Formatters.SafeName(error.Message));
            }
            finally { if (!viewToken.IsCancellationRequested) inspectCurrent.IsEnabled = true; }
        };
        resetMode.Click += (_, _) => editor.Discard();
        applyTimeOnly.Click += (_, _) => { if (!preparing && !complete) applying = ApplyAsync(timestampOnly: true); };
        editor.Changed += () => { acceptSetId.IsChecked = false; UpdatePreview(); };
        includeBound.IsCheckedChanged += (_, _) => UpdatePreview();
        acceptSetId.IsCheckedChanged += (_, _) => UpdatePreview();
        recursive.IsCheckedChanged += (_, _) => UpdatePreview();
        treeMode.IsCheckedChanged += (_, _) => UpdatePreview();
        hidden.IsCheckedChanged += (_, _) => UpdatePreview();
        modified.TextChanged += (_, _) => UpdatePreview();
        created.TextChanged += (_, _) => UpdatePreview();
        UpdatePreview();

        try
        {
            await dialogs.ShowCustomAsync("Permissions and times", body,
                [new("Cancel", "close", IsCancel: true) { Presentation = footer, Validate = MayClose }, new("Apply", "apply", IsDefault: true)
                    { IsAvailable = () => canApply, InvokeAsync = () => applying = ApplyAsync() }],
                initialFocus: FirstField(body), resizable: true, closer: closer, behavior: new DialogBehavior
                {
                    DismissOnBackdrop = false, KeepOperationsUsableDuringAction = true, ConfirmOnOptionEnter = false, ConfirmOnListItemEnter = false,
                    ClosePresentation = close, PreferredSize = new Avalonia.Size(640, 640), SizeKey = "permissions.attributes",
                    OnImplicitDismiss = () => { if (MayClose()) closer.Close("close"); }
                });
        }
        finally
        {
            viewLifetime.Cancel();
            if (applying is not null) await applying;
        }

        bool MayClose()
        {
            if (complete) return true;
            var draft = Preview();
            if (!draft.ChangeMode && !draft.Modification.HasValue && !draft.Creation.HasValue && draft.Hidden is null && draft.Problem is null)
                return true;
            if (!checkingDiscard) _ = ConfirmDiscardAsync();
            return false;
        }

        async Task ConfirmDiscardAsync()
        {
            checkingDiscard = true;
            try
            {
                if (await dialogs.ConfirmAsync("Discard changes?", "Discard the unsent changes to permissions and times?", "Discard", cancelText: "Keep editing"))
                    closer.Close("close");
            }
            finally { checkingDiscard = false; }
        }

        void UpdatePreview()
        {
            canApply = false;
            if (preparing || complete) return;
            var preview = Preview();
            resetMode.IsVisible = preview.ModeDraft;
            modeDiff.IsVisible = preview.ModeRows.Length != 0;
            var previouslySelected = modeDiff.SelectedItem as ModeChangePreview;
            modeDiff.ItemsSource = preview.ModeRows;
            int position = previouslySelected is null ? -1 : Array.FindIndex(preview.ModeRows, row => row.Source == previouslySelected.Source);
            modeDiff.SelectedIndex = position >= 0 ? position : preview.ModeRows.Length > 0 ? 0 : -1;
            acceptSetId.IsVisible = preview.ClearsSetId;
            exactExecute.IsVisible = treeAvailable && treeMode.IsChecked == true;
            includeBound.Content = preview.TreeMode ? "Review only the bound items and their trees" : "Apply the combined change to bound items only";
            applyTimeOnly.IsVisible = preview.TreeMode && preview.Modification.HasValue;
            validation.Text = preview.Problem;
            validation.IsVisible = preview.Problem is not null;
            canApply = preview.Problem is null && (preview.ChangeMode || preview.Modification.HasValue || preview.Creation.HasValue || preview.Hidden is not null);
        }

        UnixAttributePreview Preview(bool timestampOnly = false)
        {
            var rows = new List<ModeChangePreview>();
            bool clearsSetId = false;
            var deep = recursive.IsChecked == true;
            var finderHidden = mac && hidden.IsChecked != hiddenState ? hidden.IsChecked : null;
            bool validMode = editor.TryGetChange(out var mode);
            bool modeDraft = !timestampOnly && (!validMode || mode.TouchedBits != 0);
            bool reviewTree = treeAvailable && modeDraft && treeMode.IsChecked == true;
            string? problem = null;
            if (modeDraft && admitted.Length > 0)
            {
                if (!validMode)
                    problem = "Correct the octal value or discard the mode changes.";
                else if (needsFreshBinding)
                    problem = "Reopen this dialog to review fresh permissions, or discard the mode changes to apply timestamps separately.";
                else if (reviewTree)
                {
                    if (!singleTreeMount) problem = "Review a mode tree on one mounted filesystem at a time.";
                    else if (includeBound.IsVisible && includeBound.IsChecked != true)
                        problem = "Confirm the bound objects above before reviewing their folder trees.";
                }
                else
                {
                    try
                    {
                        foreach (var target in admitted)
                        {
                            var snapshot = target.Inspection.Snapshot;
                            ushort before = snapshot.Mode.Value!.Value;
                            ushort after = mac ? DarwinPermissionService.PreviewMode(snapshot, mode).Mode.Value!.Value
                                : LinuxPermissionService.PreviewMode(snapshot, mode);
                            if (after == before) continue;
                            clearsSetId |= (before & (ushort)(UnixFileMode.SetUser | UnixFileMode.SetGroup) & ~after) != 0;
                            rows.Add(new(target.Source, snapshot.Access.Value?.Acl as PosixAcl, before, after, snapshot.Binding!.DarwinRoute));
                        }
                    }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException)
                    { problem = Formatters.SafeName(error.Message); }
                }
                if (problem is null && rows.Count != 0)
                {
                    if (deep) problem = treeAvailable ? "Use the folder-tree option for recursive mode changes, or turn off recursive timestamps."
                        : "Mode changes cannot include descendants here. Turn off recursion or discard the mode changes.";
                    else if (includeBound.IsVisible && includeBound.IsChecked != true)
                        problem = "Confirm the bound objects above or discard the mode changes to edit timestamps separately.";
                    else if (clearsSetId && acceptSetId.IsChecked != true)
                        problem = "Accept the reviewed set-ID clearance to apply these changes.";
                }
            }
            if (!TryReadTime(modified, originalModified, out var mod))
                problem ??= "Enter a valid modified time or clear the field.";
            if (reviewTree && mod.HasValue)
                problem ??= "Apply the timestamp separately or clear its field before reviewing tree mode.";
            DateTime? cre = null;
            if (mac && !TryReadTime(created, metadata.Single?.CreatedUtc, out cre))
                problem ??= "Enter a valid creation time or clear the field.";
            if (reviewTree && (cre.HasValue || finderHidden is not null))
                problem ??= "Apply the creation time or Finder Hidden change separately before reviewing tree mode.";
            if (!mac && rows.Count != 0 && mod is { } time &&
                (!LinuxPermissionService.CanWriteModifiedTime(time) || admitted.Any(target => target.Inspection.Snapshot.Times is not
                    { Status: PermissionReadStatus.Present, Value.Modified: not null })))
                problem ??= "The modified time cannot be verified in the combined change. Apply it separately.";
            if (mac && (rows.Count > 0 || !deep && admitted.Length == selection.Count) && (mod.HasValue || cre.HasValue || finderHidden is not null))
            {
                if (mod.HasValue || cre.HasValue)
                {
                    var times = new ChangePermissionTimes(mod, cre);
                    problem ??= admitted.Select(target => DarwinPermissionService.TimeReviewRefusal(target.Inspection.Snapshot, times))
                        .FirstOrDefault(reason => reason is not null);
                }
                if (rows.Any(row => row.Route == DarwinBindingRoute.ParentRelative && row.Source.Kind == EntryKind.Directory && (row.After & 0x12) != 0))
                    problem ??= "Apply the mode change separately because it changes who can write a selected folder.";
            }
            return new(mode, modeDraft, rows.ToArray(), clearsSetId, mod, cre, finderHidden, deep, reviewTree, problem);
        }

        async Task ApplyAsync(bool timestampOnly = false)
        {
            if (preparing || complete) return;
            var draft = Preview();
            var preview = timestampOnly ? Preview(timestampOnly: true) : draft;
            bool available = timestampOnly ? draft.TreeMode && preview.Modification.HasValue : canApply;
            bool boundMetadata = mac && !preview.Recursive && admitted.Length == selection.Count &&
                (preview.Modification.HasValue || preview.Creation.HasValue || preview.Hidden is not null);
            bool useReview = preview.ChangeMode || boundMetadata;
            // A reviewed permission change needs the original marks, because its bindings came from them. A timestamp or
            // attribute job keeps the selection this dialog shows, as on Windows, whatever the panels show by now.
            bool originalSelection = !useReview || origin.HasOriginalMarks;
            if (preview.Problem is not null || !available || !originalSelection || viewToken.IsCancellationRequested)
            {
                if (!originalSelection && !viewToken.IsCancellationRequested)
                    ShowResult("The original selection changed. Reopen the dialog to review it again.");
                return;
            }
            preparing = true;
            Volatile.Write(ref submitted, null);
            canApply = false;
            fields.IsEnabled = false;
            var completion = new TaskCompletionSource<Job>(TaskCreationOptions.RunContinuationsAsynchronously);
            bool jobAdmitted = false;
            void Finished(Job job)
            {
                if (ReferenceEquals(job, Volatile.Read(ref submitted))) completion.TrySetResult(job);
            }
            Services.Jobs.JobFinished += Finished;
            try
            {
                if (useReview)
                {
                    ShowResult("Checking the reviewed objects…");
                    using var operation = preview.TreeMode
                        ? await CaptureAttributeModeTreeAsync(admitted, preview.Mode, exactExecute.IsChecked == true, inspected.Count, dialogs, viewToken)
                        : await PrepareAttributePermissionsAsync(admitted, preview, viewToken);
                    if (operation is null) { result.IsVisible = false; return; }
                    viewToken.ThrowIfCancellationRequested();
                    if (!origin.HasOriginalMarks) throw new PermissionBindingConflictException("The original selection changed before submission.");
                    Services.Jobs.SubmitPermissions(operation, job =>
                    {
                        Volatile.Write(ref submitted, job);
                        _permissionOrigins[job] = origin;
                    });
                }
                else
                {
                    var set = preview.Hidden == true ? FileAttributes.Hidden : 0;
                    var clear = preview.Hidden == false ? FileAttributes.Hidden : 0;
                    Services.Jobs.SubmitWithRegistration(new()
                    {
                        Kind = JobKind.Attributes, Sources = timestampOnly ? selection.ToArray() : selection,
                        Attributes = new(set, clear, preview.Modification, preview.Creation, preview.Recursive)
                    }, job =>
                    {
                        Volatile.Write(ref submitted, job);
                        if (!timestampOnly) Track(job, origin.Tab);
                    });
                    if (!timestampOnly) scope.SelectionTransferred = true;
                }
                jobAdmitted = true;

                // Refreshes after an admitted job no longer cancel the dialog's result observer.
                scope.Detach();
                ShowResult("Applying changes. Follow progress in Operations.");
                stop.IsVisible = true;
                stop.IsEnabled = true;
                close.Update(new("Close view", "Close view and continue in Operations", "This keeps the running operation in Operations."));
                var finished = await completion.Task.WaitAsync(viewToken);
                stop.IsVisible = false;
                ShowResult(DescribePermissionJobResult(finished));
                if (timestampOnly)
                {
                    if (finished.State == JobState.Completed && finished.ItemsFailed == 0 && finished.ItemsSkipped == 0)
                        originalModified = preview.Modification;
                    close.Update(new("Close permissions and times"));
                    return;
                }
                complete = true;
                inspectCurrent.IsVisible = finished.Kind == JobKind.Permissions;
                footer.Update(new("Close"));
                close.Update(new("Close permissions and times"));
            }
            catch (OperationCanceledException) when (viewToken.IsCancellationRequested) { }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException or InvalidOperationException
                or ArgumentException or InvalidDataException)
            {
                if (preview.ChangeMode) needsFreshBinding = true;
                if (!viewToken.IsCancellationRequested) ShowResult(Formatters.SafeName(error.Message));
            }
            finally
            {
                Services.Jobs.JobFinished -= Finished;
                if (!jobAdmitted && submitted is { } job)
                {
                    _permissionOrigins.Remove(job);
                    _jobOrigins.Remove(job);
                }
                preparing = false;
                if (!viewToken.IsCancellationRequested) { fields.IsEnabled = !complete; UpdatePreview(); }
            }
        }

        void ShowResult(string message) { result.Text = message; result.IsVisible = true; }
    }

    private async Task UnmarkVerifiedPermissionsAsync(PermissionSelection origin, Job job)
    {
        // A captured root is complete only with a verified tree. Preserve marks when scope or outcomes are incomplete.
        if (job.PermissionReviewScope == PermissionScope.ReviewedTree &&
            (job.ReviewedTreeSummary is not { InaccessibleBranches: 0, ExcludedEntries: 0, ProtectedEntries: 0 } ||
             job.PermissionResults.Count != job.Request.Sources.Count ||
             job.PermissionResults.Any(result => result.Verdict is not (PermissionVerdict.Verified or PermissionVerdict.AlreadyMatching)))) return;
        var verified = job.PermissionResults.Where(result => result.Verdict is
            PermissionVerdict.Verified or PermissionVerdict.AlreadyMatching or PermissionVerdict.AppliedWithFolderDrift);
        var stillListed = new List<int>();
        try
        {
            foreach (var result in verified)
            {
                if (!origin.HasOriginalMarks) return;
                var source = job.Request.Sources[result.SourceIndex];
                if (source.FileSystemPath is not { } path) continue;
                if (result.Identity is not { } expected) continue;
                var subject = new PermissionSubject(source.Parent, path, path, expected.Authority, expected.Kind,
                    PermissionLinkPolicy.NoFollow, ExpectedIdentity: expected);
                string device = Services.Providers.For(source.Parent).GetDeviceKey(source.Parent);
                using var observed = await Services.Io.RunOwned(device, IoPriority.Interactive,
                    token => Services.Permissions.Inspect(subject, PermissionSections.None, token));
                if (observed.Snapshot.Binding?.Identity == expected) stillListed.Add(result.SourceIndex);
            }
            if (origin.HasOriginalMarks && stillListed.Count != 0) origin.Tab.Listing.UnmarkCapturedResults(job.Request.Sources, stillListed);
        }
        catch (Exception error) when (error is OperationCanceledException or ObjectDisposedException or IOException or UnauthorizedAccessException) { }
    }

    private async Task ShowCurrentAttributePermissionsAsync(IDialogService dialogs, IReadOnlyList<AttributePermissionTarget> targets,
        Job? job, CancellationToken ct)
    {
        bool tree = job?.PermissionReviewScope == PermissionScope.ReviewedTree;
        var subjects = tree && job is not null ? job.PermissionResults.Select(result =>
        {
            var source = job.Request.Sources[result.SourceIndex];
            var identity = result.Identity!;
            string path = source.FileSystemPath!;
            return (Source: source, Subject: new PermissionSubject(source.Parent, path, path, identity.Authority, identity.Kind,
                PermissionLinkPolicy.NoFollow, ExpectedIdentity: identity));
        }) : targets.Select(target =>
        {
            var original = target.Inspection.Snapshot;
            var subject = original.Subject with { ExpectedIdentity = original.Binding!.Identity, ListedSize = null, ListedModifiedUtcTicks = null };
            return (target.Source, Subject: subject);
        });
        var current = new List<(string Name, PermissionSnapshot Snapshot)>();
        foreach (var (source, subject) in subjects)
        {
            ct.ThrowIfCancellationRequested();
            string device = Services.Providers.For(source.Parent).GetDeviceKey(source.Parent);
            using var observation = await Services.Io.RunOwned(device, IoPriority.Interactive,
                token => Services.Permissions.Inspect(subject, PermissionReadSections | PermissionSections.Times, token), ct);
            current.Add((tree ? subject.DisplayPath : source.Name, observation.Snapshot));
        }
        ct.ThrowIfCancellationRequested();
        if (current.Count == 0) return;
        var clipboard = View.Clipboard;
        await dialogs.ShowCustomAsync("Current permissions and times", new PermissionInspectorView(current.ToArray(),
            clipboard is null ? null : clipboard.SetTextAsync, principalNames: Services.PermissionPrincipalNames),
            [new("Close", "close", IsDefault: true, IsCancel: true)], resizable: true,
            behavior: new DialogBehavior { DismissOnBackdrop = false, PreferredSize = new Avalonia.Size(860, 640), SizeKey = "permissions.current" });
    }

    private async Task<ReviewedPermissionOperation> PrepareAttributePermissionsAsync(IReadOnlyList<AttributePermissionTarget> targets,
        UnixAttributePreview preview, CancellationToken ct)
    {
        var changes = ImmutableArray.CreateBuilder<PermissionDelta>();
        if (preview.ChangeMode) changes.Add(preview.Mode);
        if (preview.Modification.HasValue || preview.Creation.HasValue) changes.Add(new ChangePermissionTimes(
            preview.Modification is { } modified ? new DateTimeOffset(modified) : null,
            preview.Creation is { } created ? new DateTimeOffset(created) : null));
        if (preview.Hidden is { } hidden) changes.Add(new ChangePermissionFlags(hidden ? MacFinderHidden.Hidden : 0u, MacFinderHidden.Hidden));
        var draft = new PermissionDraft(PermissionScope.Objects, changes.ToImmutable());
        var prepared = ImmutableArray.CreateBuilder<ReviewedPermissionTarget>(targets.Count);
        var checkedObjects = new HashSet<(PermissionAuthority Authority, FileIdentity Object)>();
        try
        {
            foreach (var target in targets)
            {
                // An alias has the same reviewed access and would otherwise require a second write on the same inode.
                var identity = target.Inspection.Snapshot.Binding!.Identity;
                if (!checkedObjects.Add((identity.Authority, identity.Object)))
                    throw new NotSupportedException("Select one name for each hard-linked object in the same permissions review.");
                string device = Services.Providers.For(target.Source.Parent).GetDeviceKey(target.Source.Parent);
                var review = await Services.Io.RunOwned(device, IoPriority.Interactive, token =>
                    Services.Permissions is DarwinPermissionService
                        ? DarwinPermissionService.PrepareTarget(target.Inspection, target.Source, draft, preview.ClearsSetId, token)
                        : LinuxPermissionService.PrepareTarget(target.Inspection, target.Source, draft, preview.ClearsSetId, token), ct);
                prepared.Add(review.Target);
            }
            ct.ThrowIfCancellationRequested();
            return new(PermissionScope.Objects, prepared.ToImmutable());
        }
        catch
        {
            foreach (var target in prepared) target.Mutation.Dispose();
            throw;
        }
    }

    private sealed record UnixAttributePreview(ChangePermissionMode Mode, bool ModeDraft, ModeChangePreview[] ModeRows, bool ClearsSetId,
        DateTime? Modification, DateTime? Creation, bool? Hidden, bool Recursive, bool TreeMode, string? Problem)
    {
        internal bool ChangeMode => TreeMode || ModeRows.Length > 0;
    }

    private sealed record ModeChangePreview(ItemRef Source, PosixAcl? Access, ushort Before, ushort After, DarwinBindingRoute Route)
    {
        internal string? DisplayPath { get; init; }
        private int OldMask => (Before >> 3) & 7;
        private int NewMask => (After >> 3) & 7;
        private bool ChangesMask => OldMask != NewMask && Access is { } acl && acl.Entries.Any(row => row.Tag == PosixAclTag.Mask);

        public override string ToString()
        {
            string text = $"{Formatters.SafeName(DisplayPath ?? Source.Name)} · {UnixPermissions.Octal((UnixFileMode)Before)} → {UnixPermissions.Octal((UnixFileMode)After)}";
            return ChangesMask ? $"{text} · group-class limit {Rights(OldMask)} → {Rights(NewMask)}" : text;
        }

        internal string[] AffectedMaskEntries() => !ChangesMask ? [] : Access!.Entries
            .Where(row => (row.Tag is PosixAclTag.OwningGroup or PosixAclTag.NamedUser or PosixAclTag.NamedGroup) &&
                (row.Rights & OldMask) != (row.Rights & NewMask))
            .Select(row => $"{PrincipalLabel(row)} · configured {Rights(row.Rights)} · effective {Rights(row.Rights & OldMask)} → {Rights(row.Rights & NewMask)}")
            .ToArray();

        private static string PrincipalLabel(PosixAclEntry row) => row.Tag switch
        {
            PosixAclTag.NamedUser => "User " + Formatters.SafeName(row.Principal!.Value.Id),
            PosixAclTag.NamedGroup => "Group " + Formatters.SafeName(row.Principal!.Value.Id),
            _ => "Owning group"
        };

        private static string Rights(int mask) => $"{((mask & 4) != 0 ? 'r' : '-')}{((mask & 2) != 0 ? 'w' : '-')}{((mask & 1) != 0 ? 'x' : '-')}";
    }

    private static TextBox TimeEditor(string name, DateTime? initial)
    {
        var box = new TextBox { Text = initial?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? string.Empty, PlaceholderText = "unchanged (yyyy-MM-dd HH:mm:ss)" };
        AutomationProperties.SetName(box, name);
        return box;
    }

    private static bool TryReadTime(TextBox field, DateTime? initial, out DateTime? changed)
    {
        changed = null;
        if (string.IsNullOrWhiteSpace(field.Text)) return true;
        if (!DateTime.TryParse(field.Text, out var parsed)) return false;
        var utc = parsed.ToUniversalTime();
        if (initial is null || Math.Abs((utc - initial.Value).TotalSeconds) >= 1) changed = utc;
        return true;
    }

    private static TextBlock Wrapped(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap };

    /// <summary>
    /// The wait while an administrator decides on an authorized inspection. Focus starts on Cancel, and a screen reader
    /// reads that focused button, not the sentence beside it (NVDA said only "Inspect with authorization dialog, Cancel
    /// button"), so the sentence is also the button's description.
    /// </summary>
    internal static Task<object?> ShowAuthorizationWaitAsync(IDialogService dialogs, string path, string approval, DialogCloser closer) =>
        dialogs.ShowCustomAsync("Inspect with authorization", new StackPanel { Spacing = 8, Children = { Wrapped(path), Wrapped(approval) } },
            [new("Cancel", "cancel", IsDefault: true, IsCancel: true) { HelpText = approval }], closer: closer,
            behavior: new DialogBehavior { DismissOnBackdrop = false });

    // An editor whose Apply is still unavailable opens on its first shown field, not on the dialog card: a screen reader
    // then names that field along with the dialog, and the keyboard can change it at once.
    internal static Control? FirstField(Control content) => content.GetSelfAndLogicalDescendants().OfType<Control>().FirstOrDefault(
        control => control is ListBox { ItemCount: > 0 } or ComboBox or TextBox or Button && control.IsEnabled &&
            control.GetSelfAndLogicalAncestors().All(node => node is not (Control { IsVisible: false } or Expander { IsExpanded: false })));

    private static TextBlock PermissionNotice()
    {
        var label = new TextBlock { TextWrapping = TextWrapping.Wrap, IsVisible = false };
        AutomationProperties.SetLiveSetting(label, AutomationLiveSetting.Polite);
        return label;
    }

    private static string DescribePermissionJobResult(Job job)
    {
        if (job.Kind == JobKind.Permissions)
        {
            if (job.PermissionReviewScope == PermissionScope.NativePropagation)
                return PermissionOperationReport.DescribePropagation(job.PermissionResults.FirstOrDefault());
            var counts = job.PermissionResults.GroupBy(root => root.Verdict).Select(group =>
                $"{PermissionOperationReport.Describe(group.Key)} {group.Count()}");
            string detail = job.PermissionUsesServerPath ? "Review the server-path results in Operations." : "Review object details in Operations.";
            return $"{string.Join(" · ", counts)}. {detail}";
        }
        return $"{job.State.Describe()} · {job.Summary}";
    }
}
