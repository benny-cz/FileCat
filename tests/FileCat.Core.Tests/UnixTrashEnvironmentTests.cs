using FileCat.Core.FileSystem;

namespace FileCat.Core.Tests;

/// <summary>Trash location resolution is explicit, side-effect free, and never borrows the working directory.</summary>
public sealed class UnixTrashEnvironmentTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("relative-profile")]
    [InlineData("../profile")]
    [InlineData("bad\0profile")]
    public void Linux_honors_an_absolute_configured_data_directory_without_a_valid_profile(string? profile)
    {
        string configured = Path.Combine(_dir.Path, "unused", "..", "configured data Č🐱");
        var result = UnixTrash.ResolveHomeTrashBaseDirectory(profile, configured, macOS: false);
        Assert.Equal(Path.Combine(_dir.Path, "configured data Č🐱"), result);
        Assert.Empty(Directory.EnumerateFileSystemEntries(_dir.Path));
    }

    [Fact]
    public void Linux_configured_data_has_priority_over_an_absolute_profile()
    {
        string profile = Path.Combine(_dir.Path, "profile");
        string configured = Path.Combine(_dir.Path, "separate-data");
        Assert.Equal(configured, UnixTrash.ResolveHomeTrashBaseDirectory(profile, configured, macOS: false));
        Assert.Empty(Directory.EnumerateFileSystemEntries(_dir.Path));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("relative-data")]
    [InlineData("../data")]
    [InlineData("C:relative-data")]
    [InlineData("bad\0data")]
    public void Linux_ignores_missing_relative_or_malformed_data_paths_and_uses_the_profile_default(string? configured)
    {
        string profile = Path.Combine(_dir.Path, "unused", "..", "profile");
        Assert.Equal(Path.Combine(_dir.Path, "profile", ".local", "share"),
            UnixTrash.ResolveHomeTrashBaseDirectory(profile, configured, macOS: false));
        Assert.Empty(Directory.EnumerateFileSystemEntries(_dir.Path));
    }

    [Fact]
    public void An_absolute_data_path_containing_NUL_is_ignored_without_throwing_or_creating_anything()
    {
        string profile = Path.Combine(_dir.Path, "profile");
        string configured = Path.Combine(_dir.Path, "invalid\0data");
        Assert.Equal(Path.Combine(profile, ".local", "share"),
            UnixTrash.ResolveHomeTrashBaseDirectory(profile, configured, macOS: false));
        Assert.Null(UnixTrash.ResolveHomeTrashBaseDirectory(null, configured, macOS: false));
        Assert.Empty(Directory.EnumerateFileSystemEntries(_dir.Path));
    }

    [Theory]
    [InlineData(false, null)]
    [InlineData(false, "")]
    [InlineData(false, "relative-profile")]
    [InlineData(false, "bad\0profile")]
    [InlineData(true, null)]
    [InlineData(true, "")]
    [InlineData(true, "relative-profile")]
    [InlineData(true, "bad\0profile")]
    public void A_missing_or_invalid_profile_cannot_turn_a_relative_data_path_into_a_trash_location(bool macOS, string? profile)
    {
        Assert.Null(UnixTrash.ResolveHomeTrashBaseDirectory(profile, "relative-data", macOS));
        Assert.Empty(Directory.EnumerateFileSystemEntries(_dir.Path));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void An_absolute_profile_containing_NUL_cannot_supply_a_fallback(bool macOS)
    {
        string profile = Path.Combine(_dir.Path, "invalid\0profile");
        Assert.Null(UnixTrash.ResolveHomeTrashBaseDirectory(profile, null, macOS));
        Assert.Empty(Directory.EnumerateFileSystemEntries(_dir.Path));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MacOS_uses_only_the_profile_even_when_XDG_data_is_configured(bool malformedData)
    {
        string profile = Path.Combine(_dir.Path, "profile");
        string configured = Path.Combine(_dir.Path, malformedData ? "invalid\0data" : "separate-data");
        Assert.Equal(profile, UnixTrash.ResolveHomeTrashBaseDirectory(profile, configured, macOS: true));
        Assert.Null(UnixTrash.ResolveHomeTrashBaseDirectory(null, configured, macOS: true));
        Assert.Empty(Directory.EnumerateFileSystemEntries(_dir.Path));
    }
}
