using System.Diagnostics;
using FileCat.Core.FileSystem;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

/// <summary>
/// A folder link that denies listing itself while the folder it leads to lists — Windows' compatibility junctions such
/// as "Local Settings", whose own ACL denies everyone "List folder" — is reported with that folder, so a panel can show it
/// instead of stopping at "Access is denied", as Open Salamander does. Paths through such a link always worked. A link
/// whose target refuses as well, and a folder that refuses by itself, stay ordinary access-denied failures.
/// </summary>
public sealed class RefusedLinkListingTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly List<string> _links = [];
    private readonly List<string> _deniedFolders = [];

    public void Dispose()
    {
        // A recursive delete does not remove junctions here: they go first, by themselves, then the denials that would
        // keep the rest from being listed.
        foreach (var link in Enumerable.Reverse(_links))
        {
            try { Directory.Delete(link); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        foreach (var folder in _deniedFolders) Icacls(folder, "/remove:d", "*S-1-1-0");
        _dir.Dispose();
    }

    [Fact]
    public async Task A_junction_that_denies_listing_itself_reports_the_folder_it_leads_to()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "Only Windows gives a link permissions of its own.");
        string target = _dir.Dir(Path.Combine("AppData", "Local"));
        _dir.File(Path.Combine("AppData", "Local", "kept.txt"));
        _dir.File(Path.Combine("AppData", "Local", "Microsoft", "inner.txt"));
        string link = Junction("Local Settings", target, denyListing: true);

        var refused = await Assert.ThrowsAsync<LinkListingRefusedException>(() => ListAsync(link));
        Assert.Equal(link, refused.Link);
        Assert.Equal(Schemes.FileSystem, refused.Target.Scheme);
        Assert.Equal(target, refused.Target.Path, ignoreCase: true);
        Assert.StartsWith($"Access to the path '{link}' is denied.", refused.Message);
        Assert.Contains($"It is a link to {target}, which can be listed instead.", refused.Message);
        // The provider answers with what the panel will list: the target lists, and so does any path through the link.
        Assert.Equal(["Microsoft", "kept.txt"], (await ListAsync(refused.Target.Path)).Order(StringComparer.Ordinal));
        Assert.Equal(["inner.txt"], await ListAsync(Path.Combine(link, "Microsoft")));
    }

    [Fact]
    public async Task A_listable_junction_lists_as_before()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "Junctions are Windows links.");
        string target = _dir.Dir("data");
        _dir.File("data/kept.txt");
        string link = Junction("plain link", target, denyListing: false);

        Assert.Equal(["kept.txt"], await ListAsync(link));
    }

    [Fact]
    public async Task A_chain_of_refusing_links_reports_the_folder_it_ends_at()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "Only Windows gives a link permissions of its own.");
        string end = _dir.Dir("end");
        _dir.File("end/marker.txt");
        string middle = Junction("middle", end, denyListing: true);
        string first = Junction("first", middle, denyListing: true);

        var refused = await Assert.ThrowsAsync<LinkListingRefusedException>(() => ListAsync(first));
        // Past the link it names, which refuses too, to the real folder (named by its final path, long or short).
        Assert.NotEqual(middle, refused.Target.Path, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(0, (int)(File.GetAttributes(refused.Target.Path) & FileAttributes.ReparsePoint));
        Assert.Equal(["marker.txt"], await ListAsync(refused.Target.Path));
    }

    [Fact]
    public async Task A_link_whose_target_refuses_too_and_a_refusing_folder_stay_access_denied()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "Only Windows gives a link permissions of its own.");
        string target = _dir.Dir("closed");
        _dir.File("closed/secret.txt");
        Deny(target, link: false);
        string link = Junction("closed link", target, denyListing: true);

        // Exactly access denied: nothing to show instead.
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => ListAsync(link));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => ListAsync(target));
        Assert.Null(LocalFileSystemProvider.ListableTargetOfRefusedLink(target));
    }

    [Fact]
    public async Task The_profiles_own_Local_Settings_junction_reports_AppData_Local()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "Windows profiles only.");
        string profile = Environment.GetEnvironmentVariable("USERPROFILE") ?? "";
        Assert.SkipWhen(profile.Length == 0, "USERPROFILE is not set.");
        string link = Path.Combine(profile, "Local Settings");
        Assert.SkipWhen(!Directory.Exists(link) || (File.GetAttributes(link) & FileAttributes.ReparsePoint) == 0,
            "This profile has no \"Local Settings\" compatibility junction.");

        // Read only: the profile's own junction is listed, never changed.
        var refused = await Assert.ThrowsAsync<LinkListingRefusedException>(() => ListAsync(link));
        Assert.Equal(Path.Combine(profile, "AppData", "Local"), refused.Target.Path, ignoreCase: true);
        Assert.NotEmpty(await ListAsync(refused.Target.Path));
    }

    private static async Task<List<string>> ListAsync(string path)
    {
        var names = new List<string>();
        await new LocalFileSystemProvider().EnumerateAsync(Location.FileSystem(path), new Sink(names), TestContext.Current.CancellationToken);
        return names;
    }

    /// <summary>A junction (any user may make one), with the compatibility junctions' "Everyone: deny List folder" on itself.</summary>
    private string Junction(string name, string target, bool denyListing)
    {
        string link = Path.Combine(_dir.Path, name);
        Run(Path.Join(Environment.SystemDirectory, "cmd.exe"), "/d", "/c", "mklink", "/J", link, target);
        _links.Add(link);
        if (denyListing)
        {
            Deny(link, link: true);
            Assert.Throws<UnauthorizedAccessException>(() => Directory.EnumerateFileSystemEntries(link).GetEnumerator().Dispose());
        }
        return link;
    }

    /// <summary>Denies everyone "List folder" on the folder, or with <paramref name="link"/> on the link itself (icacls /L).</summary>
    private void Deny(string path, bool link)
    {
        if (link) Icacls(path, "/deny", "*S-1-1-0:(RD)", "/L");
        else
        {
            Icacls(path, "/deny", "*S-1-1-0:(RD)");
            _deniedFolders.Add(path);
        }
    }

    private static void Icacls(string path, params string[] arguments) =>
        Run(Path.Join(Environment.SystemDirectory, "icacls.exe"), [path, .. arguments]);

    private static void Run(string program, params string[] arguments)
    {
        var start = new ProcessStartInfo(program) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        Assert.True(process.WaitForExit(10_000), $"{Path.GetFileName(program)} timed out.");
        Assert.True(process.ExitCode == 0, $"{Path.GetFileName(program)} {string.Join(' ', arguments)} failed: {output}");
    }

    private sealed class Sink(List<string> names) : IEnumerationSink
    {
        public void AddBatch(ReadOnlySpan<EntryData> entries)
        {
            foreach (var e in entries) names.Add(e.Name);
        }

        public void ReportIssue(string message) { }
    }
}
