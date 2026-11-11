using System.IO.Compression;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using FileCat.Core.Archives;
using FileCat.Core.FileSystem;

namespace FileCat.App.Views;

internal sealed record ArchiveCreationFormat(ArchiveWriteFormat Format, string Name, string Extension, params string[] Aliases)
{
    internal static IReadOnlyList<ArchiveCreationFormat> All { get; } = [
        new(ArchiveWriteFormat.Zip, "ZIP", ".zip"), new(ArchiveWriteFormat.Tar, "TAR", ".tar"),
        new(ArchiveWriteFormat.TarGzip, "TAR + gzip", ".tar.gz", ".tgz"),
        new(ArchiveWriteFormat.TarBzip2, "TAR + bzip2", ".tar.bz2", ".tbz2", ".tbz"),
        new(ArchiveWriteFormat.TarZstd, "TAR + Zstandard", ".tar.zst", ".tzst"),
        new(ArchiveWriteFormat.SevenZip, "7z", ".7z"), new(ArchiveWriteFormat.Rar5, "RAR5", ".rar"),
        // AW7: "Compress file". One regular file becomes one compressed stream named after it (report.txt.gz).
        new(ArchiveWriteFormat.Gzip, "gzip (compress one file)", ".gz"), new(ArchiveWriteFormat.Bzip2, "bzip2 (compress one file)", ".bz2"),
        new(ArchiveWriteFormat.Zstd, "Zstandard (compress one file)", ".zst"),
    ];
    internal bool IsStandalone => ArchiveStandalone.IsStandalone(Format);
    /// <summary>The TAR format with the same compression, offered when a standalone format cannot hold the selection.</summary>
    internal string TarAlternative => Format switch
    {
        ArchiveWriteFormat.Gzip => "TAR + gzip", ArchiveWriteFormat.Bzip2 => "TAR + bzip2", ArchiveWriteFormat.Zstd => "TAR + Zstandard", _ => Name,
    };
    internal ArchiveWriteProfile? DefaultProfile => Format switch
    {
        ArchiveWriteFormat.Zip => null,
        ArchiveWriteFormat.SevenZip => ArchiveWriteProfile.ForSevenZip(),
        ArchiveWriteFormat.Rar5 => ArchiveWriteProfile.ForRar5(),
        _ => new(Format),
    };
    public override string ToString() => Name;
}

internal sealed record ArchiveCreationSelection(string Path, ArchiveCreationFormat Format, ArchiveWriteProfile? Profile, CompressionLevel LegacyLevel);

/// <summary>Only public options survive this dialog. Password text is cleared on every exit or ownership transfer.</summary>
internal sealed class ArchiveCreationDialog : IDisposable
{
    private sealed record Choice<T>(string Name, T Value) { public override string ToString() => Name; }
    private readonly TextBox path, password, confirmation;
    private readonly ComboBox format, level, codec, filter, dictionary, header, protection;
    private readonly NumericUpDown delta;
    private readonly Control compressionRow, protectionRow, protectionRows, confirmationRow, dictionaryRow, deltaRow;
    private readonly TextBlock passwordHelp;
    private readonly Expander advanced;
    private readonly TextBlock problem, preview, estimate;
    private readonly Func<ArchivePlan, ArchiveJobAvailability> availability;
    private bool updating, generatedPath = true;
    private readonly string generatedStem;
    private readonly string? singleFileName;
    private string? externalProblem;
    internal bool ExistingTarget { get; private set; }
    internal StackPanel Content { get; }
    internal Control InitialFocus => path;
    internal ArchiveCreationSelection? Selection { get; private set; }
    internal bool CanConfirm => Selection is not null && externalProblem is null;

    /// <param name="singleFileName">The name of the one selected regular file, or null when several items or a folder are packed;
    /// only then can a standalone compressed file be created.</param>
    internal ArchiveCreationDialog(string suggestedPath, string selectedSummary, IReadOnlyList<ArchiveCreationFormat> formats,
        Func<ArchivePlan, ArchiveJobAvailability> availability, string? singleFileName = null)
    {
        this.availability = availability; this.singleFileName = singleFileName;
        string? known = KnownSuffix(suggestedPath);
        generatedStem = known is null ? suggestedPath : suggestedPath[..^known.Length];
        path = new() { Text = suggestedPath, MinWidth = 240 };
        format = Box("Archive format", formats); format.SelectedIndex = 0;
        level = Box("Compression", Array.Empty<object>());
        codec = Box("7z compression method", new[] {
            new Choice<ArchiveSevenZipCodec>("LZMA2", ArchiveSevenZipCodec.Lzma2), new("LZMA", ArchiveSevenZipCodec.Lzma),
            new("PPMd", ArchiveSevenZipCodec.Ppmd7), new("Bzip2", ArchiveSevenZipCodec.Bzip2),
            new("Deflate", ArchiveSevenZipCodec.Deflate), new("Zstandard (compatible reader required)", ArchiveSevenZipCodec.Zstandard),
            new("Store", ArchiveSevenZipCodec.Copy),
        });
        filter = Box("7z preprocessing", new[] {
            new Choice<ArchiveSevenZipFilter>("None", ArchiveSevenZipFilter.None), new("Delta", ArchiveSevenZipFilter.Delta),
            new("x86", ArchiveSevenZipFilter.X86), new("ARM", ArchiveSevenZipFilter.Arm), new("ARM Thumb", ArchiveSevenZipFilter.ArmThumb),
            new("ARM64", ArchiveSevenZipFilter.Arm64), new("PowerPC", ArchiveSevenZipFilter.Ppc), new("IA-64", ArchiveSevenZipFilter.Ia64),
            new("SPARC", ArchiveSevenZipFilter.Sparc), new("RISC-V", ArchiveSevenZipFilter.RiscV), new("Swap 2 bytes", ArchiveSevenZipFilter.Swap2),
            new("Swap 4 bytes", ArchiveSevenZipFilter.Swap4), new("BCJ2 (x86)", ArchiveSevenZipFilter.Bcj2),
        });
        dictionary = Box("7z dictionary", new[] { new Choice<int>("8 MiB", 8 << 20), new("1 MiB", 1 << 20),
            new("2 MiB", 2 << 20), new("4 MiB", 4 << 20), new("16 MiB", 16 << 20) });
        header = Box("7z header compression", new[] { new Choice<ArchiveSevenZipHeaderCompression>("LZMA2", ArchiveSevenZipHeaderCompression.Lzma2),
            new("Uncompressed", ArchiveSevenZipHeaderCompression.Raw) });
        protection = Box("Password protection", new[] { new Choice<ArchiveEncryptionMode>("No password", ArchiveEncryptionMode.None),
            new("Protect contents", ArchiveEncryptionMode.Data), new("Protect contents and names", ArchiveEncryptionMode.DataAndNames) });
        password = new() { PasswordChar = '•', MinWidth = 240 }; confirmation = new() { PasswordChar = '•', MinWidth = 240 };
        delta = new() { Minimum = 1, Maximum = 256, Increment = 1, Value = 1, FormatString = "0", MinWidth = 100 };
        AutomationProperties.SetName(path, "Archive path"); AutomationProperties.SetName(password, "Archive password");
        AutomationProperties.SetName(confirmation, "Confirm archive password"); AutomationProperties.SetName(delta, "Delta distance");
        problem = Text("");
        AutomationProperties.SetLiveSetting(problem, AutomationLiveSetting.Polite);
        // Plan §11.2 step 6: checked source count and estimated extra space, or an honest unknown, before submission.
        estimate = Text("Checking what will be packed…"); estimate.Classes.Add("muted");
        AutomationProperties.SetName(estimate, "Size and space estimate");
        preview = Text("");
        compressionRow = Row("Compression:", level);
        protectionRow = Row("Protection:", protection);
        confirmationRow = Row("Confirm password:", confirmation);
        passwordHelp = Text("Enter the existing archive's password, or leave it empty for an unprotected archive. Its protection mode is preserved.");
        protectionRows = new StackPanel { Spacing = 6, Children = { passwordHelp, Row("Password:", password), confirmationRow } };
        dictionaryRow = Row("Dictionary:", dictionary); deltaRow = Row("Delta distance:", delta);
        advanced = new() { Header = "7z options", HorizontalAlignment = HorizontalAlignment.Stretch,
            Content = new StackPanel { Spacing = 8, Children = { Row("Method:", codec), Row("Preprocessing:", filter),
                dictionaryRow, deltaRow, Row("Header compression:", header) } } };
        var included = new Expander { Header = "What is included", Content = Text(
            "Regular files and folders are checked before writing; links are not followed. Unsupported inputs are reviewed or refused. " +
            "An archive is not a complete backup of file permissions and security metadata. Creating it needs additional free disk space.") };
        // Screen readers name an unnamed expander by its type ("Expander panel" in Orca); give each its header's words.
        AutomationProperties.SetName(advanced, "7z options"); AutomationProperties.SetName(included, "What is included");
        Content = new() { Spacing = 8, Children = { Text("Pack " + selectedSummary + " into:"), path,
            Row("Format:", format), compressionRow, advanced, protectionRow, protectionRows,
            preview, estimate, problem, included } };
        // TextChanged is queued, so it cannot distinguish an automatic suffix change from an edit once
        // the updating scope has ended. Property changes are synchronous and keep that ownership exact.
        path.PropertyChanged += (_, change) =>
        {
            if (change.Property != TextBox.TextProperty) return;
            if (!updating) { generatedPath = false; if (ExistingTarget) { ExistingTarget = false; ClearSecrets(); } }
            externalProblem = null; Validate();
        };
        password.PropertyChanged += (_, change) => { if (change.Property == TextBox.TextProperty) { if (!updating) externalProblem = null; Validate(); } };
        confirmation.PropertyChanged += (_, change) => { if (change.Property == TextBox.TextProperty) { if (!updating) externalProblem = null; Validate(); } };
        format.SelectionChanged += (_, _) => ChangeFormat();
        foreach (var box in new[] { level, codec, filter, dictionary, header, protection }) box.SelectionChanged += (_, _) => Validate();
        delta.ValueChanged += (_, _) => Validate();
        ChangeFormat();
    }

    private void ChangeFormat()
    {
        if (updating || format.SelectedItem is not ArchiveCreationFormat selected) return;
        if (ExistingTarget) { ExistingTarget = false; ClearSecrets(); }
        updating = true;
        try
        {
            if (generatedPath)
            {
                // A compressed file keeps the whole file name (report.txt.gz); an archive uses the stem (report.zip).
                string stem = selected.IsStandalone && singleFileName is not null
                    ? Path.Combine(Path.GetDirectoryName(generatedStem) ?? "", singleFileName) : generatedStem;
                path.Text = stem + selected.Extension;
            }
            level.ItemsSource = selected.Format switch
            {
                ArchiveWriteFormat.Zip => new[] { new Choice<int>("Normal compression", 2), new("Fastest", 1), new("No compression (store)", 0) },
                ArchiveWriteFormat.Rar5 => new[] { new Choice<int>("Normal", 3), new("Fastest", 1), new("Fast", 2), new("Good", 4), new("Best", 5), new("Store", 0) },
                _ => new[] { new Choice<int>("Normal", 2), new("Fast", 1), new("Smaller", 3) },
            };
            level.SelectedIndex = 0; externalProblem = null;
            if (selected.Format is not (ArchiveWriteFormat.Rar5 or ArchiveWriteFormat.SevenZip))
            { protection.SelectedIndex = 0; ClearSecrets(); }
        }
        finally { updating = false; }
        Validate();
    }

    private void Validate()
    {
        if (updating) return;
        Selection = null;
        if (format.SelectedItem is not ArchiveCreationFormat selected) return;
        bool seven = selected.Format == ArchiveWriteFormat.SevenZip, encryptedFormat = seven || selected.Format == ArchiveWriteFormat.Rar5;
        protectionRow.IsVisible = encryptedFormat && !ExistingTarget;
        advanced.IsVisible = seven;
        compressionRow.IsVisible = selected.Format is not (ArchiveWriteFormat.Tar or ArchiveWriteFormat.TarBzip2 or ArchiveWriteFormat.Bzip2) &&
            (!seven || Value<ArchiveSevenZipCodec>(codec) != ArchiveSevenZipCodec.Copy);
        dictionaryRow.IsVisible = seven && Value<ArchiveSevenZipCodec>(codec) is ArchiveSevenZipCodec.Lzma or ArchiveSevenZipCodec.Lzma2;
        deltaRow.IsVisible = seven && Value<ArchiveSevenZipFilter>(filter) == ArchiveSevenZipFilter.Delta;
        var mode = !encryptedFormat ? ArchiveEncryptionMode.None : ExistingTarget
            ? string.IsNullOrEmpty(password.Text) ? ArchiveEncryptionMode.None : ArchiveEncryptionMode.Data
            : Value<ArchiveEncryptionMode>(protection);
        if (!ExistingTarget && mode == ArchiveEncryptionMode.None) ClearSecrets();
        protectionRows.IsVisible = encryptedFormat && ExistingTarget || mode != ArchiveEncryptionMode.None;
        confirmationRow.IsVisible = !ExistingTarget; passwordHelp.IsVisible = ExistingTarget;
        string? error = externalProblem;
        try
        {
            string destination = ResolvePath(path.Text ?? "", selected);
            if (selected.IsStandalone && singleFileName is null)
                throw new ArgumentException($"A compressed file holds exactly one file. To pack several items or a folder, choose {selected.TarAlternative}.");
            int effort = Value<int>(level);
            int distance = checked((int)(delta.Value ?? 1));
            if (seven && Value<ArchiveSevenZipFilter>(filter) == ArchiveSevenZipFilter.Delta && delta.Value != distance)
                throw new ArgumentException("Delta distance must be a whole number from 1 to 256.");
            var encryption = mode == ArchiveEncryptionMode.None ? ArchiveEncryptionOptions.None : new ArchiveEncryptionOptions(mode);
            ArchiveWriteProfile? profile = selected.Format switch
            {
                ArchiveWriteFormat.Zip => null,
                ArchiveWriteFormat.Rar5 => ArchiveWriteProfile.ForRar5((ArchiveRar5Compression)effort, encryption),
                ArchiveWriteFormat.SevenZip => ArchiveWriteProfile.ForSevenZip(new(Value<ArchiveSevenZipCodec>(codec), Value<ArchiveSevenZipFilter>(filter),
                    deltaDistance: Value<ArchiveSevenZipFilter>(filter) == ArchiveSevenZipFilter.Delta ? distance : 1,
                    headerCompression: Value<ArchiveSevenZipHeaderCompression>(header)),
                    Value<ArchiveSevenZipCodec>(codec) == ArchiveSevenZipCodec.Copy ? 0 : effort,
                    Value<ArchiveSevenZipCodec>(codec) is ArchiveSevenZipCodec.Lzma or ArchiveSevenZipCodec.Lzma2 ? Value<int>(dictionary) : 8 << 20, encryption),
                ArchiveWriteFormat.Tar or ArchiveWriteFormat.TarBzip2 or ArchiveWriteFormat.Bzip2 => new(selected.Format),
                _ => new(selected.Format, compressionLevel: effort),
            };
            if (mode != ArchiveEncryptionMode.None)
            {
                if (string.IsNullOrEmpty(password.Text)) throw new ArgumentException("Enter a password or choose No password.");
                ArchiveJobPassword.ValidateEncoding(selected.Format, password.Text.AsSpan());
                if (!ExistingTarget && !string.Equals(password.Text, confirmation.Text, StringComparison.Ordinal)) throw new ArgumentException("The passwords do not match.");
            }
            if (profile is not null && availability(new(destination, null, [], Profile: profile)) is { Available: false } refused)
                throw new ArgumentException(refused.Reason ?? "Writing this format is unavailable here. Choose another format or location.");
            if (error is null) Selection = new(destination, selected, profile,
                effort switch { 0 => CompressionLevel.NoCompression, 1 => CompressionLevel.Fastest, _ => CompressionLevel.Optimal });
            preview.Text = "Filename: " + Path.GetFileName(destination);
        }
        catch (Exception failure) when (failure is ArgumentException or IOException or NotSupportedException or OverflowException)
        { error = failure.Message; preview.Text = ""; }
        problem.Text = error ?? ""; AutomationProperties.SetName(problem, error ?? ""); problem.IsVisible = error is not null;
    }

    internal static string ResolvePath(string text, ArchiveCreationFormat format)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("Choose an archive filename.");
        string full = Path.GetFullPath(PathUtil.ExpandUserInput(text.Trim()));
        if (Path.GetFileName(full).Length == 0) throw new ArgumentException("Choose an archive filename.");
        string? known = KnownSuffix(full);
        // A compressed single file named like a TAR archive (notes.tar.gz) would be read back as an archive it is not.
        if (format.IsStandalone && known is not null && !format.Aliases.Prepend(format.Extension).Contains(known, StringComparer.OrdinalIgnoreCase))
        {
            var tar = ArchiveCreationFormat.All.First(item => item.Aliases.Prepend(item.Extension).Contains(known, StringComparer.OrdinalIgnoreCase));
            throw new ArgumentException($"The name ends in {known}, which names a {tar.Name} archive. Choose {tar.Name}, or give the compressed file a name ending in {format.Extension}.");
        }
        if (format.Aliases.Prepend(format.Extension).Any(suffix => full.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))) return full;
        string suffix = known ?? Path.GetExtension(full);
        if (suffix.Length != 0)
        {
            var typed = ArchiveCreationFormat.All.FirstOrDefault(item => item.Aliases.Prepend(item.Extension).Contains(suffix, StringComparer.OrdinalIgnoreCase));
            throw new ArgumentException($"The name ends in {suffix}, but the selected format is {format.Name}. " +
                (typed is null ? "Change the extension." : $"Change the extension or choose {typed.Name}."));
        }
        return full + format.Extension;
    }
    private static string? KnownSuffix(string path) => ArchiveCreationFormat.All.SelectMany(item => item.Aliases.Prepend(item.Extension))
        .OrderByDescending(value => value.Length).FirstOrDefault(value => path.EndsWith(value, StringComparison.OrdinalIgnoreCase));
    internal ArchiveJobPassword? TakePassword()
    {
        if (Selection is null) throw new InvalidOperationException("Archive creation options are invalid.");
        var selected = Selection; Selection = null;
        try { return selected.Profile?.Encryption.Mode is ArchiveEncryptionMode.Data or ArchiveEncryptionMode.DataAndNames ? new(password.Text.AsSpan()) : null; }
        finally { ClearSecrets(); }
    }
    internal ArchiveJobPassword? CopyPassword()
    {
        if (Selection is null) throw new InvalidOperationException("Archive options are invalid.");
        return Selection.Profile?.Encryption.Mode is ArchiveEncryptionMode.Data or ArchiveEncryptionMode.DataAndNames ? new(password.Text.AsSpan()) : null;
    }
    internal void CompleteTransfer() { Selection = null; ClearSecrets(); }
    internal void MarkExistingTarget() { ExistingTarget = true; externalProblem = null; Validate(); }
    internal void ShowProblem(string message) { externalProblem = message; Validate(); }
    private void ClearSecrets()
    {
        bool previous = updating; updating = true;
        try { password.Text = ""; confirmation.Text = ""; }
        finally { updating = previous; }
    }
    public void Dispose() => ClearSecrets();
    private static T Value<T>(ComboBox box) => ((Choice<T>)box.SelectedItem!).Value;
    private static ComboBox Box(string name, System.Collections.IEnumerable choices)
    { var box = new ComboBox { ItemsSource = choices, SelectedIndex = 0, MinWidth = 210 }; AutomationProperties.SetName(box, name); return box; }
    private static TextBlock Text(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, MaxWidth = 620 };
    internal string EstimateText => estimate.Text ?? string.Empty;
    internal void ShowEstimate(string text) => estimate.Text = text;
    private static Control Row(string label, Control control) => new WrapPanel { ItemSpacing = 8, LineSpacing = 4,
        Children = { new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center }, control } };
}
