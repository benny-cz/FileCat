using System.Collections.Immutable;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using FileCat.App.Services;
using FileCat.App.Views;
using FileCat.Core.Analysis;
using FileCat.Core.FileSystem;
using FileCat.Platform.Windows;
using Microsoft.VisualBasic.FileIO;

namespace FileCat.Rendering.Tests;

public sealed partial class AnalysisRenderingTests
{
    [AvaloniaTheory]
    [InlineData(AnalysisReportScope.FilteredView)] [InlineData(AnalysisReportScope.SelectedRows)] [InlineData(AnalysisReportScope.EntireResult)]
    public async Task NativeCapturedDatasetCsvUsesCanonicalScopeAndRemainsOfflineAfterOriginalOwnersClose(AnalysisReportScope scope)
    {
        OrdinaryPreparation(); using var profile = new PreparationFixture(); await using var source = await NativeNamesFixture.CreateAsync(true);
        var token = TestContext.Current.CancellationToken; var lease = source.Lease!;
        var paths = (await lease.ReadPathsAsync(0, cancellation: token)).Rows;
        var root = paths.Single(path => path.ParentId == 0);
        var selected = paths.Where(path => path.Name is "plain.bin" or "ordinary.txt").Select(path => path.Id).ToImmutableArray(); Assert.Equal(2, selected.Length);
        await using var report = await lease.CaptureReportAsync(AnalysisMetric.FileSize, AnalysisReportFormat.Csv, scope, new("name:plain"), root.Id,
            selected, "Selected folder", AnalysisHierarchySort.Name, false, token);
        ulong expected = scope == AnalysisReportScope.EntireResult ? lease.Descriptor.Paths : 2UL;
        Assert.Equal(expected, report.Header.Rows); Assert.Equal(scope == AnalysisReportScope.SelectedRows, report.Header.SummaryDescribesSourceScope);
        Assert.Equal(scope == AnalysisReportScope.FilteredView ? 2UL : (ulong)source.Files.Count, report.Header.Logical.Files);
        Assert.Equal((UInt128)report.Header.Logical.Files * 4, report.Header.Logical.KnownBytes);
        await source.ReleaseLeaseAsync();
        string folder = Path.Combine(source.Root, "reports"); Directory.CreateDirectory(folder);
        string offline = source.SourcePath + "-offline", prefix = Path.GetFullPath(source.Root) + Path.DirectorySeparatorChar;
        Assert.StartsWith(prefix, Path.GetFullPath(source.SourcePath)); Assert.StartsWith(prefix, Path.GetFullPath(offline));
        Directory.Move(source.SourcePath, offline);
        try
        {
            await report.ExportNewAsync("captured.csv", PathUtil.GetDeviceKey(folder),
                () => OperatingSystem.IsWindows() ? WindowsAnalysisPrivateDirectory.OpenExportDestination(folder)
                    : UnixAnalysisPrivateDirectory.OpenExportDestination(folder, () => new UnixAnalysisPrivateDirectory(profile.Root, "report-staging")), false, token);
            var rows = CapturedCsv(File.ReadAllText(Path.Combine(folder, "captured.csv"))); var header = rows[0];
            Assert.Equal(expected + 2, (ulong)rows.Count);
            int item = Array.IndexOf(header, "item"), location = Array.IndexOf(header, "display_location");
            Assert.All(rows.Skip(2), row => Assert.Equal("item", row[0]));
            Assert.All(rows.Skip(2), row => Assert.Contains(source.SourcePath, row[location]));
            if (scope == AnalysisReportScope.FilteredView) Assert.All(rows.Skip(2), row => Assert.Contains("plain", row[item]));
            if (scope == AnalysisReportScope.SelectedRows) Assert.Contains(rows.Skip(2), row => row[item] == "ordinary.txt");
            Assert.False(Directory.Exists(source.SourcePath));
        }
        finally { Directory.Move(offline, source.SourcePath); }
        await report.DisposeAsync(); await report.DisposeAsync(); source.AssertPayloads(); Assert.Empty(Directory.GetFiles(source.PrivatePath));
    }

    [AvaloniaFact]
    public async Task NativeCapturedReportCloseCancelsButWaitsForActualOutputBeforeReleasingItsGraph()
    {
        OrdinaryPreparation(); using var profile = new PreparationFixture(); await using var source = await NativeNamesFixture.CreateAsync(true);
        var token = TestContext.Current.CancellationToken;
        var report = await source.Lease!.CaptureReportAsync(AnalysisMetric.FileSize, AnalysisReportFormat.Csv, AnalysisReportScope.EntireResult, new(""), cancellation: token);
        await source.ReleaseLeaseAsync(); string folder = Path.Combine(source.Root, "reports"); Directory.CreateDirectory(folder);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); using var release = new ManualResetEventSlim();
        var exporting = report.ExportNewAsync("canceled.csv", PathUtil.GetDeviceKey(folder), () => new ExportFaultDestination(
            OperatingSystem.IsWindows() ? WindowsAnalysisPrivateDirectory.OpenExportDestination(folder)
                : UnixAnalysisPrivateDirectory.OpenExportDestination(folder, () => new UnixAnalysisPrivateDirectory(profile.Root, "report-staging")), "cancel", entered, release), true, token);
        Task? closing = null;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(15), token); closing = report.DisposeAsync().AsTask();
            Assert.False(closing.IsCompleted); Assert.False(exporting.IsCompleted); release.Set();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => exporting); await closing;
            Assert.Empty(Directory.GetFiles(folder)); Assert.Empty(Directory.GetFiles(source.PrivatePath)); source.AssertPayloads();
        }
        finally
        {
            release.Set();
            try { await exporting; } catch (Exception error) when (error is IOException or OperationCanceledException) { }
            await (closing ?? report.DisposeAsync().AsTask()); await report.DisposeAsync();
        }
    }

    [AvaloniaFact]
    public async Task NativeCapturedReportRejectsInvalidSelectionAndScopeWithoutLeakingReaders()
    {
        OrdinaryPreparation(); await using var source = await NativeNamesFixture.CreateAsync(true); var token = TestContext.Current.CancellationToken;
        var lease = source.Lease!; int handles = AnalysisNativeHandles.Count;
        await Assert.ThrowsAsync<InvalidDataException>(() => lease.CaptureReportAsync(AnalysisMetric.FileSize, AnalysisReportFormat.Csv,
            AnalysisReportScope.SelectedRows, new(""), selected: [1, 1], cancellation: token));
        await Assert.ThrowsAnyAsync<ArgumentOutOfRangeException>(() => lease.CaptureReportAsync(AnalysisMetric.FileSize, AnalysisReportFormat.Html,
            AnalysisReportScope.FilteredView, new(""), folder: ulong.MaxValue, cancellation: token));
        Assert.Equal(handles, AnalysisNativeHandles.Count); source.AssertPayloads();
    }

    private static List<string[]> CapturedCsv(string text)
    {
        using var parser = new TextFieldParser(new StringReader(text)) { TextFieldType = FieldType.Delimited, HasFieldsEnclosedInQuotes = true, TrimWhiteSpace = false };
        parser.SetDelimiters(","); var rows = new List<string[]>(); while (!parser.EndOfData) rows.Add(parser.ReadFields()!); return rows;
    }

    [AvaloniaTheory]
    [InlineData(AnalysisReportFormat.Markdown, false)] [InlineData(AnalysisReportFormat.Markdown, true)]
    [InlineData(AnalysisReportFormat.Html, false)] [InlineData(AnalysisReportFormat.Html, true)]
    [InlineData(AnalysisReportFormat.Csv, false)] [InlineData(AnalysisReportFormat.Csv, true)]
    public async Task NativeCapturedReportsUseReviewedAppOutputBoundaryAndKeepExistingFiles(AnalysisReportFormat format, bool redacted)
    {
        OrdinaryPreparation(); using var profile = new PreparationFixture(); await using var source = await NativeNamesFixture.CreateAsync(true);
        var token = TestContext.Current.CancellationToken; var options = new DiskSpaceWindow.ReportOptions(format, AnalysisReportScope.EntireResult, redacted);
        await using var report = await source.Lease!.CaptureReportAsync(AnalysisMetric.FileSize, format, options.Scope, new("name:doesnotmatch"), cancellation: token);
        string folder = Path.Combine(source.Root, "reports"); Directory.CreateDirectory(folder); string name = "reviewed" + options.Extension;
        await using (var prepared = await AnalysisIndexedAcquisition.PrepareExportAsync(profile.Services, token))
        {
            var receipt = await prepared.ExportReportAsync(report, folder, name, redacted, token);
            Assert.Equal(new FileInfo(Path.Combine(folder, name)).Length, receipt.Bytes);
        }
        string text = File.ReadAllText(Path.Combine(folder, name)); byte[] original = File.ReadAllBytes(Path.Combine(folder, name));
        Assert.Equal(format != AnalysisReportFormat.Csv, report.Header.DetailLimited);
        if (redacted) { Assert.DoesNotContain(source.SourcePath, text); Assert.DoesNotContain("ordinary", text); Assert.DoesNotContain("plain", text); }
        else Assert.Contains(format == AnalysisReportFormat.Markdown ? AnalysisHumanReport.Markdown(source.SourcePath) : source.SourcePath, text);
        if (format == AnalysisReportFormat.Csv) Assert.Equal((int)report.Header.Rows + 2, CapturedCsv(text).Count);
        await using (var prepared = await AnalysisIndexedAcquisition.PrepareExportAsync(profile.Services, token))
            await Assert.ThrowsAsync<AnalysisExportTargetExistsException>(() => prepared.ExportReportAsync(report, folder, name, redacted, token));
        Assert.Equal(original, File.ReadAllBytes(Path.Combine(folder, name))); source.AssertPayloads();
    }

    [AvaloniaTheory]
    [InlineData(AnalysisReportFormat.Markdown)] [InlineData(AnalysisReportFormat.Html)] [InlineData(AnalysisReportFormat.Csv)]
    public async Task NativeCapturedReportsCannotWriteIntoProtectedAppState(AnalysisReportFormat format)
    {
        OrdinaryPreparation(); using var profile = new PreparationFixture(); await using var source = await NativeNamesFixture.CreateAsync(true);
        var token = TestContext.Current.CancellationToken;
        await using var report = await source.Lease!.CaptureReportAsync(AnalysisMetric.FileSize, format, AnalysisReportScope.EntireResult, new(""), cancellation: token);
        var options = new DiskSpaceWindow.ReportOptions(format, AnalysisReportScope.EntireResult, true);
        string folder = profile.Services.Paths.AnalysisSnapshotsDirectory; Directory.CreateDirectory(folder);
        string name = "blocked" + options.Extension;
        await using (var prepared = await AnalysisIndexedAcquisition.PrepareExportAsync(profile.Services, token))
            await Assert.ThrowsAnyAsync<IOException>(() => prepared.ExportReportAsync(report, folder, name, true, token));
        Assert.False(File.Exists(Path.Combine(folder, name))); source.AssertPayloads();
    }

    [AvaloniaFact]
    public async Task NativeCapturedReportsProtectRedirectedFileCatDirectoriesByIdentity()
    {
        OrdinaryPreparation();
        using var profile = new PreparationFixture();
        await using var source = await NativeNamesFixture.CreateAsync(true);
        var token = TestContext.Current.CancellationToken;
        await using var report = await source.Lease!.CaptureReportAsync(AnalysisMetric.FileSize, AnalysisReportFormat.Csv,
            AnalysisReportScope.EntireResult, new(""), cancellation: token);

        string target = Directory.CreateDirectory(Path.Combine(source.Root, "redirected-backups")).FullName;
        string child = Directory.CreateDirectory(Path.Combine(target, "descendant")).FullName;
        string sibling = Directory.CreateDirectory(Path.Combine(source.Root, "ordinary-output")).FullName;
        string retained = Path.Combine(child, "older-backup.reg");
        byte[] original = [1, 2, 3, 4];
        File.WriteAllBytes(retained, original);
        string link = profile.Services.Paths.RegistryBackupDirectory;
        if (OperatingSystem.IsWindows()) Junction.Create(link, target);
        else Directory.CreateSymbolicLink(link, target);
        try
        {
            foreach (string folder in new[] { target, child })
            {
                await using var prepared = await AnalysisIndexedAcquisition.PrepareExportAsync(profile.Services, token);
                var error = await Assert.ThrowsAsync<IOException>(() => prepared.ExportReportAsync(report, folder, "blocked.csv", true, token));
                Assert.Contains("protected FileCat", error.Message, StringComparison.Ordinal);
                Assert.False(File.Exists(Path.Combine(folder, "blocked.csv")));
            }
            await using (var prepared = await AnalysisIndexedAcquisition.PrepareExportAsync(profile.Services, token))
                Assert.True((await prepared.ExportReportAsync(report, sibling, "allowed.csv", true, token)).Bytes > 0);
            Assert.Equal(original, File.ReadAllBytes(retained));
            source.AssertPayloads();
        }
        finally { Directory.Delete(link); }
    }

    [AvaloniaTheory]
    [InlineData(true, 0)] [InlineData(true, 2)] [InlineData(false, 0)] [InlineData(false, 2)]
    public async Task CapturedReportReviewDefaultsToRedactionAndKeepsUnavailableViewExplicit(bool folderView, int marks)
    {
        using var styles = new ProductStyles(); using var ui = new HistoryDialogFixture(480, 620, 20); var token = TestContext.Current.CancellationToken;
        var pending = DiskSpaceWindow.ReviewReportOptionsAsync(ui.Dialogs, folderView, marks, token);
        await Until(() => ui.Dialogs.IsOpen); Frame(ui.Window); Assert.True(ui.Find("Cancel").IsDefault);
        var format = ui.Window.GetVisualDescendants().OfType<ComboBox>().Single(value => AutomationLabel(value) == "Report format");
        var scope = ui.Window.GetVisualDescendants().OfType<ComboBox>().Single(value => AutomationLabel(value) == "Report scope");
        Assert.Equal(folderView ? 2 + (marks == 0 ? 0 : 1) : 1 + (marks == 0 ? 0 : 1), scope.ItemCount);
        Assert.Equal(folderView, scope.Items.Cast<string>().Any(value => value.Contains("filtered folder", StringComparison.Ordinal)));
        format.SelectedIndex = 2; scope.SelectedIndex = scope.ItemCount - 1;
        ui.Click("Choose destination…"); var reviewed = Assert.IsType<DiskSpaceWindow.ReportOptions>(await pending);
        Assert.Equal(AnalysisReportFormat.Csv, reviewed.Format); Assert.Equal(AnalysisReportScope.EntireResult, reviewed.Scope); Assert.True(reviewed.RedactPaths);
        Assert.Contains("every real namespace row", reviewed.Description); Assert.Equal(".csv", reviewed.Extension);
    }

    [AvaloniaTheory]
    [InlineData(AnalysisReportViewKind.NamespaceRows)] [InlineData(AnalysisReportViewKind.GroupOverview)] [InlineData(AnalysisReportViewKind.GroupMembers)]
    [InlineData(AnalysisReportViewKind.SmallItems)] [InlineData(AnalysisReportViewKind.SmallGroups)] [InlineData(AnalysisReportViewKind.SmallGroupMembers)]
    public async Task SupportedReportViewsReviewCapturesEveryCurrentScopeWithRedactionAndCancelDefault(AnalysisReportViewKind kind)
    {
        using var styles = new ProductStyles(); using var ui = new HistoryDialogFixture(480, 620, 20);
        var pending = DiskSpaceWindow.ReviewReportOptionsAsync(ui.Dialogs, true, 2, TestContext.Current.CancellationToken, kind);
        await Until(() => ui.Dialogs.IsOpen); Frame(ui.Window); Assert.True(ui.Find("Cancel").IsDefault);
        var format = ui.Window.GetVisualDescendants().OfType<ComboBox>().Single(value => AutomationLabel(value) == "Report format");
        var scope = ui.Window.GetVisualDescendants().OfType<ComboBox>().Single(value => AutomationLabel(value) == "Report scope");
        Assert.Equal(3, scope.ItemCount); Assert.Equal(0, scope.SelectedIndex); format.SelectedIndex = 2;
        ui.Click("Choose destination…"); var result = Assert.IsType<DiskSpaceWindow.ReportOptions>(await pending);
        Assert.Equal(AnalysisReportScope.FilteredView, result.Scope); Assert.Equal(kind, result.ViewKind); Assert.True(result.RedactPaths);
        Assert.Contains("Table pagination does not limit", result.Description);
        if (kind is AnalysisReportViewKind.GroupOverview or AnalysisReportViewKind.SmallGroups) Assert.Contains("accounting-group rows", result.Description);
        if (kind is AnalysisReportViewKind.GroupMembers or AnalysisReportViewKind.SmallGroupMembers) Assert.Contains("group member paths", result.Description);
    }

    [AvaloniaTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task CapturedReportFilenameReviewRequiresMatchingFormatAndPreservingDefault(bool accept)
    {
        using var styles = new ProductStyles(); using var ui = new HistoryDialogFixture(480, 620, 20);
        var options = new DiskSpaceWindow.ReportOptions(AnalysisReportFormat.Csv, AnalysisReportScope.SelectedRows, true);
        var pending = DiskSpaceWindow.ReviewExternalOutputAsync(ui.Dialogs, "captured/folder", "Partial observations remain partial.", "report.html", options, TestContext.Current.CancellationToken);
        await Until(() => ui.Dialogs.IsOpen); Frame(ui.Window); Assert.True(ui.Find("Cancel").IsDefault); Assert.False(ui.Find("Export new report").IsEnabled);
        var name = ui.Window.GetVisualDescendants().OfType<TextBox>().Single(value => AutomationLabel(value) == "Report filename");
        name.Text = "report.csv"; await Until(() => ui.Find("Export new report").IsEnabled); Frame(ui.Window);
        ui.Click(accept ? "Export new report" : "Cancel"); Assert.Equal(accept ? "report.csv" : null, await pending);
    }
}
