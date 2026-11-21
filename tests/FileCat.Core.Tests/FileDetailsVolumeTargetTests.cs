using FileCat.Core.Records;

namespace FileCat.Core.Tests;

/// <summary>
/// F-SCOPE: link targets stored as volume GUID paths. Only the exact <c>\??\Volume{GUID}\…</c> form, or its
/// <c>\\?\</c> spelling, resolves to a qualified local navigation path, never a network one. Malformed GUIDs, a missing
/// closing brace or separator, and stream suffixes are refused.
/// </summary>
public sealed class FileDetailsVolumeTargetTests
{
    private const string Guid = "{1b2c3d4e-5f60-4718-9a2b-3c4d5e6f7a8b}";

    private static FileDetailsSnapshot Link(string stored, uint tag) => new(@"C:\folder\link", DateTimeOffset.UtcNow, System.Guid.NewGuid())
    {
        Kind = tag == FileSystem.ReparseLinks.MountPointTag ? FileObjectKind.Directory : FileObjectKind.SymbolicLink,
        ReparseTag = ObservedFact<uint>.Known(tag, FactScope.Entry, "fixture"),
        Target = ObservedFact<FileSystem.StoredLinkTarget>.Known(new(stored, @"C:\display-is-not-used", false), FactScope.Entry, "fixture"),
    };

    [Theory]
    [InlineData(@"\??\Volume" + Guid + @"\data\file.txt", FileSystem.ReparseLinks.SymbolicLinkTag, @"\\?\Volume" + Guid + @"\data\file.txt")]
    [InlineData(@"\\?\Volume" + Guid + @"\data", FileSystem.ReparseLinks.SymbolicLinkTag, @"\\?\Volume" + Guid + @"\data")]
    [InlineData(@"\??\Volume" + Guid + @"\", FileSystem.ReparseLinks.MountPointTag, @"\\?\Volume" + Guid + @"\")]
    public void Exact_volume_guid_targets_resolve_to_local_volume_paths(string stored, uint tag, string expected)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows volume GUID paths.");
        Assert.True(FileDetailsTargets.TryResolve(Link(stored, tag), windows: true, out string path, out bool network));
        Assert.Equal(expected, path);
        Assert.False(network);
    }

    [Theory]
    [InlineData(@"\??\Volume{not-a-guid}\data")]
    [InlineData(@"\??\Volume{1b2c3d4e-5f60-4718-9a2b-3c4d5e6f7a8b\data")]
    [InlineData(@"\??\Volume" + Guid + "data")]
    [InlineData(@"\??\Volume" + Guid)]
    [InlineData(@"\??\Volume" + Guid + @"\data\file.txt:stream")]
    [InlineData(@"\??\GLOBALROOT\Volume" + Guid + @"\data")]
    public void Malformed_or_stream_qualified_volume_targets_are_refused(string stored)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows volume GUID paths.");
        Assert.False(FileDetailsTargets.TryResolve(Link(stored, FileSystem.ReparseLinks.SymbolicLinkTag), windows: true, out _, out _), stored);
    }
}
