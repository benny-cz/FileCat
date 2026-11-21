using System.Diagnostics;
using FileCat.Core.Compare;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Listing;
using FileCat.Core.Operations;
using FileCat.Core.Resources;
using FileCat.Core.Search;
using FileCat.Core.Threading;

namespace FileCat.Core.Tests;

/// <summary>
/// F-OPS: one legacy Link / raw-reparse corpus through the operational consumers named in plan §6.3 that can be
/// driven without native reparse points. Within each operational-flag group the corpus spans link and non-link
/// presentation (L versus P, different kind labels), so a decision that changed with the observed tag would show here.
/// Every decision below is a function of <see cref="EntryFlags.Link"/> alone.
/// Not covered by this corpus: MainViewModel size requests (App), SftpJobs (Remote), NativeEditSnapshot admission
/// (needs a native edit-session store and directory leases), native copy/delete (held native tags), and Search's
/// traversal for tags other than a real directory symbolic link (it reads raw attributes from the OS, never the tag).
/// </summary>
public sealed class ReparseOperationPolicyTests
{
    private const string LinkSkip = "Links are not followed implicitly while copying; open the target explicitly to copy its contents.";
    private static readonly long Then = new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc).Ticks;

    private static readonly uint[] Tags =
    [
        0,           // tag unavailable
        0xA000000C,  // symbolic link
        0xA0000003,  // mount point: junction or volume mount
        0x9000001A,  // Cloud Files
        0x9000F01A,  // Cloud Files subtag
        0x80000017,  // WOF
        0x8000001B,  // app execution alias
        0xA000FFFF,  // unknown name surrogate
        0x8000FFFF,  // unknown non-surrogate
    ];

    internal static EntryData[] Corpus(EntryKind kind, string prefix = "")
    {
        uint folder = kind == EntryKind.Directory ? (uint)FileAttributes.Directory : 0;
        long size = kind == EntryKind.File ? 1 : -1;
        long modified = kind == EntryKind.File ? Then : 0;
        var corpus = new List<EntryData>
        {
            new(prefix + "plain", kind, size, modified) { Attributes = folder },
            new(prefix + "flag-only", kind, size, modified) { Attributes = folder, Flags = EntryFlags.Link },
        };
        foreach (uint tag in Tags)
            foreach (bool link in new[] { true, false })
                corpus.Add(new($"{prefix}rp-{tag:X8}-{(link ? "flag" : "noflag")}", kind, size, modified)
                {
                    Attributes = folder | (uint)FileAttributes.ReparsePoint,
                    NativeReparseTag = tag,
                    Flags = link ? EntryFlags.Link : EntryFlags.None,
                });
        return [.. corpus];
    }

    private static EntryData Inner() => new("inner.txt", EntryKind.File, 1, Then);

    private static string[] Names(IEnumerable<EntryData> entries, bool link) =>
        [.. entries.Where(e => e.Has(EntryFlags.Link) == link).Select(e => e.Name).Order(StringComparer.Ordinal)];

    [Fact]
    public void The_corpus_spans_link_and_non_link_presentation_within_each_operational_flag_group()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows presentation interprets native reparse tags.");
        var origin = Location.FileSystem(Path.GetTempPath());
        foreach (bool flag in new[] { true, false })
        {
            var group = Corpus(EntryKind.Directory).Where(e => e.Has(EntryFlags.Link) == flag &&
                (e.Attributes & (uint)FileAttributes.ReparsePoint) != 0).ToArray();
            Assert.Contains(group, e => EntryPresentation.IsLink(e, origin));
            Assert.Contains(group, e => !EntryPresentation.IsLink(e, origin));
            Assert.True(group.Select(e => EntryPresentation.Kind(e, origin)).Distinct().Count() >= 6);
        }
    }

    [Fact]
    public void Tree_compare_recursion_and_its_sync_plan_depend_only_on_the_operational_flag()
    {
        var corpus = Corpus(EntryKind.Directory);
        string id = Guid.NewGuid().ToString("N");
        string left = Path.Join(Path.GetTempPath(), "policy-left-" + id), right = Path.Join(Path.GetTempPath(), "policy-right-" + id);
        var provider = new CorpusProvider(Schemes.FileSystem);
        provider.Folders[left] = corpus;
        provider.Folders[right] = corpus;
        foreach (var entry in corpus)
        {
            provider.Folders[Path.Join(left, entry.Name)] = [Inner()];
            provider.Folders[Path.Join(right, entry.Name)] = [Inner()];
        }
        var providers = new ProviderRegistry();
        providers.Register(provider);

        var result = TreeCompare.Compare(providers, Location.FileSystem(left), Location.FileSystem(right),
            CompareCriteria.Size | CompareCriteria.Time, TimeSpan.FromSeconds(2), caseInsensitiveNames: false, TestContext.Current.CancellationToken);

        Assert.True(result.Complete);
        var byPath = result.Entries.ToDictionary(e => e.RelativePath, StringComparer.Ordinal);
        foreach (var entry in corpus)
        {
            if (entry.Has(EntryFlags.Link))
            {
                var link = byPath[entry.Name];
                Assert.True(link.Kind == TreeDiffKind.Unknown, entry.Name);
                Assert.Equal("A link to a folder: not followed", link.Detail);
                Assert.False(byPath.ContainsKey(entry.Name + "/inner.txt"), entry.Name);
            }
            else
            {
                Assert.False(byPath.ContainsKey(entry.Name), entry.Name);
                Assert.True(byPath[entry.Name + "/inner.txt"].Kind == TreeDiffKind.Same, entry.Name);
            }
        }
        string[] walked = [.. provider.Enumerated.Where(p => p != left && p != right).Select(p => Path.GetFileName(p)).Distinct().Order(StringComparer.Ordinal)];
        Assert.Equal(Names(corpus, link: false), walked);

        foreach (var mode in new[] { SyncMode.Update, SyncMode.Mirror })
        {
            var plan = SyncPlanner.Propose(result, sourceIsLeft: true, mode, targetIgnoresCase: false);
            var links = plan.Where(item => item.Entry.Left is { } e && e.IsContainer).ToArray();
            Assert.Equal(Names(corpus, link: true), links.Select(i => i.Entry.RelativePath).Order(StringComparer.Ordinal));
            Assert.All(links, item =>
            {
                Assert.Equal(SyncAction.None, item.Action);
                Assert.Equal("A link to a folder: not followed", item.Reason);
            });
        }
    }

    [Fact]
    public async Task One_sided_folder_contents_are_read_only_for_folders_without_the_operational_flag()
    {
        using var temp = new TempDir();
        string left = temp.Dir("left"), right = temp.Dir("right");
        var corpus = Corpus(EntryKind.Directory);
        // Every corpus folder exists as an ordinary folder: only the listed operational flag can prevent a read.
        foreach (var entry in corpus) temp.File(Path.Join("left", entry.Name, "inner.txt"));
        var provider = new CorpusProvider(Schemes.FileSystem);
        provider.Folders[left] = corpus;
        provider.Folders[right] = [];
        var providers = new ProviderRegistry();
        providers.Register(provider);

        var result = await TreeCompare.CompareAsync(providers, Location.FileSystem(left), Location.FileSystem(right),
            CompareCriteria.Size | CompareCriteria.Time, TimeSpan.FromSeconds(2), caseInsensitiveNames: false,
            TestContext.Current.CancellationToken, reviewRemovals: false);

        foreach (var entry in corpus)
        {
            var only = Assert.Single(result.Entries, e => e.RelativePath == entry.Name);
            Assert.Equal(TreeDiffKind.LeftOnly, only.Kind);
            if (entry.Has(EntryFlags.Link))
            {
                Assert.True(only.Contents is null, entry.Name);
                Assert.True(only.Detail is null, entry.Name);
            }
            else
            {
                Assert.True(only.Contents is { Files: 1, Folders: 0, Bytes: 1 }, entry.Name);
                Assert.StartsWith("Holds ", only.Detail);
            }
        }
        // Neither group is listed through the provider: one-sided contents use the bounded folder reader.
        Assert.Equal([left, right], provider.Enumerated.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Mirror_plans_for_target_only_folders_depend_only_on_the_operational_flag()
    {
        var corpus = Corpus(EntryKind.Directory);
        var review = new ReviewedSourceTree(new FileTreeReview("target", [], AllowLinks: true), new CorpusProvider("policy-sync"));
        var comparison = new TreeCompareResult(
            [.. corpus.Select(e => new TreeDiffEntry(e.Name, TreeDiffKind.RightOnly, null, e) { RemovalReview = review })],
            Complete: true, FoldersVisited: 1);

        var mirror = SyncPlanner.Propose(comparison, sourceIsLeft: true, SyncMode.Mirror, targetIgnoresCase: false);
        Assert.Equal(corpus.Length, mirror.Count);
        foreach (var item in mirror)
        {
            // A link never has contents to read; an ordinary folder whose contents were not read is left alone.
            bool link = item.Entry.Right!.Value.Has(EntryFlags.Link);
            Assert.True((link ? SyncAction.Remove : SyncAction.None) == item.Action, item.Entry.RelativePath);
            Assert.Equal(link ? "Only in the target: Mirror removes it with all it holds"
                : "Only in the target, but the comparison did not read all it holds: Mirror leaves it", item.Reason);
        }
        Assert.All(SyncPlanner.Propose(comparison, sourceIsLeft: true, SyncMode.Update, targetIgnoresCase: false),
            item => Assert.Equal("Only in the target: Update keeps it", item.Reason));
    }

    [Fact]
    public async Task Folder_size_requests_skip_exactly_the_operational_links()
    {
        using var ui = new TestDispatcher();
        using var io = new DeviceIoScheduler();
        var provider = new CorpusProvider("policy-listing");
        var corpus = Corpus(EntryKind.Directory).Concat(Corpus(EntryKind.File, "file-")).ToArray();
        provider.Folders["root"] = corpus;
        var providers = new ProviderRegistry();
        providers.Register(provider);
        var listing = await ui.InvokeAsync(() => new ListingModel(providers, io, ui));
        try
        {
            await ui.InvokeAsync(() => listing.Load(new Location(provider.Scheme, "root")));
            await ui.WaitUntilAsync(() => listing.State == ListingState.Complete);
            var requested = await ui.InvokeAsync(() =>
            {
                listing.MarkAll(true);
                Assert.Equal(corpus.Length, listing.MarkedCount);
                return listing.MarkedUnsizedFolders().Select(index => listing.Store[index]).ToArray();
            });
            Assert.All(requested, e => Assert.Equal(EntryKind.Directory, e.Kind));
            Assert.Equal(Names(corpus.Where(e => e.Kind == EntryKind.Directory), link: false),
                requested.Select(e => e.Name).Order(StringComparer.Ordinal));
            // The stored rows still carry the observed tags that presentation reads.
            Assert.Equal(corpus.Select(e => (e.Name, e.NativeReparseTag)).Order(),
                await ui.InvokeAsync(() => Enumerable.Range(0, listing.Store.Count).Select(i => (listing.Store[i].Name, listing.Store[i].NativeReparseTag)).Order().ToArray()));
        }
        finally { await ui.InvokeAsync(listing.Dispose); }
    }

    [Fact]
    public async Task Recursive_file_list_export_enters_exactly_the_folders_without_the_operational_flag()
    {
        string root = Path.Join(Path.GetTempPath(), "policy-export-" + Guid.NewGuid().ToString("N"));
        var corpus = Corpus(EntryKind.Directory).Concat(Corpus(EntryKind.File, "file-")).ToArray();
        var provider = new CorpusProvider(Schemes.FileSystem);
        provider.Folders[root] = corpus;
        foreach (var entry in corpus.Where(e => e.Kind == EntryKind.Directory)) provider.Folders[Path.Join(root, entry.Name)] = [Inner()];
        var providers = new ProviderRegistry();
        providers.Register(provider);

        string text = await FileListExport.GenerateAsync(providers, Location.FileSystem(root), null,
            new FileListRule("Policy", "{RelativePath}", Recursive: true), TestContext.Current.CancellationToken);

        var lines = text.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        foreach (var entry in corpus)
        {
            Assert.Contains(entry.Name, lines);
            Assert.True((entry.Kind == EntryKind.Directory && !entry.Has(EntryFlags.Link)) == lines.Contains(entry.Name + "/inner.txt"), entry.Name);
        }
        Assert.Equal(corpus.Length + Names(corpus.Where(e => e.Kind == EntryKind.Directory), link: false).Length, lines.Count);
        Assert.Equal(Names(corpus.Where(e => e.Kind == EntryKind.Directory), link: false),
            provider.Enumerated.Where(p => p != root).Select(p => Path.GetFileName(p)).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Stream_transfer_admission_skips_exactly_the_operational_links_at_root_and_inside_folders()
    {
        using var temp = new TempDir();
        string destination = temp.Dir("destination");
        var provider = new CorpusProvider("policy-stream");
        var folders = Corpus(EntryKind.Directory);
        var files = Corpus(EntryKind.File, "file-");
        provider.Folders["box"] = [.. folders, .. files];
        foreach (var folder in folders)
        {
            provider.Folders["box/" + folder.Name] = [Inner()];
            provider.Folders[folder.Name] = [Inner()];
        }
        var providers = new ProviderRegistry();
        providers.Register(new LocalFileSystemProvider());
        providers.Register(provider);
        var parent = new Location(provider.Scheme, "");
        var sources = new List<ItemRef> { new(parent, "box", EntryKind.Directory) };
        sources.AddRange(folders.Select(e => ItemRef.FromEntry(parent, e)));
        var jobs = new JobManager(new PortableFileOperations(), providers, Path.Join(temp.Path, "journal"));
        var decisions = new List<DecisionRequest>();
        jobs.DecisionRequested += pending => { decisions.Add(pending.Request); pending.Resolve(new Decision(DecisionAction.CancelJob)); };

        var job = jobs.Submit(new JobRequest { Kind = JobKind.Copy, Sources = sources, Destination = Location.FileSystem(destination) });
        var clock = Stopwatch.StartNew();
        while (!job.State.IsFinished() || jobs.HasActiveWork)
        {
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(30), "The owned copy job did not finish.");
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        Assert.Empty(decisions);
        Assert.True(job.State is JobState.Completed or JobState.CompletedWithIssues, job.State.ToString());
        foreach (var entry in folders)
        {
            bool copied = !entry.Has(EntryFlags.Link);
            Assert.True(copied == File.Exists(Path.Join(destination, entry.Name, "inner.txt")), entry.Name);
            Assert.True(copied == Directory.Exists(Path.Join(destination, entry.Name)), entry.Name);
            Assert.True(copied == File.Exists(Path.Join(destination, "box", entry.Name, "inner.txt")), "box/" + entry.Name);
            Assert.True(copied == Directory.Exists(Path.Join(destination, "box", entry.Name)), "box/" + entry.Name);
        }
        foreach (var entry in files)
            Assert.True(!entry.Has(EntryFlags.Link) == File.Exists(Path.Join(destination, "box", entry.Name)), entry.Name);
        var skipped = job.Issues.Where(i => i.Outcome == StepOutcome.Skipped).ToArray();
        Assert.All(skipped, issue => Assert.Equal(LinkSkip, issue.Message));
        Assert.Equal(Names(folders, link: true).Concat(Names(folders, link: true)).Concat(Names(files, link: true)).Order(StringComparer.Ordinal),
            skipped.Select(i => i.Path).Order(StringComparer.Ordinal));
        // Linked children are skipped before a listing or content open: their folders are never enumerated.
        Assert.DoesNotContain(provider.Enumerated, path => Names(folders, link: true).Any(name => path == name || path == "box/" + name));
        Assert.DoesNotContain(provider.Opened, name => files.Any(e => e.Has(EntryFlags.Link) && e.Name == name));
    }

    [Fact]
    public void Search_does_not_descend_a_raw_reparse_directory()
    {
        using var temp = new TempDir();
        string root = temp.Dir("root"), outside = temp.Dir("outside");
        temp.File(Path.Join("root", "ordinary", "needle.txt"));
        temp.File(Path.Join("outside", "needle.txt"));
        string link = Path.Join(root, "linked");
        try { Directory.CreateSymbolicLink(link, outside); }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            Assert.Skip("This account cannot create a directory symbolic link: " + ex.Message);
        }
        Assert.True((File.GetAttributes(link) & FileAttributes.ReparsePoint) != 0);
        Assert.True(File.Exists(Path.Join(link, "needle.txt")), "The link resolves for an explicit open.");

        Assert.True(Selection.Mask.TryParse("needle.txt", out var needle, out _));
        Assert.True(Selection.Mask.TryParse("linked", out var linked, out _));
        var set = new ResultSet("policy", "policy", "policy");
        var session = new SearchSession(new SearchQuery { Roots = [root], Names = needle, IncludeDirectories = false }, set);
        session.Run(TestContext.Current.CancellationToken);

        // The engine may report the root in its canonical spelling; compare below the root.
        Assert.True(set.IsComplete);
        Assert.Equal([Path.Join("ordinary", "needle.txt")], set.Snapshot().Select(r => Path.GetRelativePath(root, r.Item.FileSystemPath!)));
        var named = new ResultSet("policy-link", "policy-link", "policy-link");
        new SearchSession(new SearchQuery { Roots = [root], Names = linked }, named).Run(TestContext.Current.CancellationToken);
        Assert.Equal(["linked"], named.Snapshot().Select(r => Path.GetRelativePath(root, r.Item.FileSystemPath!))); // listed as itself, never entered
    }

    /// <summary>Listings by exact path; records every enumeration and content open.</summary>
    private sealed class CorpusProvider(string scheme) : ResourceProvider
    {
        private readonly object _gate = new();
        private readonly List<string> _enumerated = [], _opened = [];
        public Dictionary<string, EntryData[]> Folders { get; } = new(StringComparer.Ordinal);
        public string[] Enumerated { get { lock (_gate) return [.. _enumerated]; } }
        public string[] Opened { get { lock (_gate) return [.. _opened]; } }
        public override string Scheme => scheme;
        public override string GetDisplayPath(Location location) => scheme + ":" + location.Path;
        public override Location? GetParent(Location location) => null;
        public override LocationCapabilities GetCapabilities(Location location) => LocationCapabilities.Enumerate | LocationCapabilities.ReadContent;
        public override Location? GetChildLocation(Location parent, in EntryData entry) => entry.Kind != EntryKind.Directory ? null
            : parent.WithPath(scheme == Schemes.FileSystem ? Path.Join(parent.Path, entry.Name) : parent.Path.Length == 0 ? entry.Name : parent.Path + "/" + entry.Name);

        public override Task EnumerateAsync(Location location, IEnumerationSink sink, CancellationToken ct)
        {
            lock (_gate) _enumerated.Add(location.Path);
            if (!Folders.TryGetValue(location.Path, out var entries)) throw new DirectoryNotFoundException("No fixture listing for " + location.Path);
            sink.AddBatch(entries);
            return Task.CompletedTask;
        }

        public override IContentSource? OpenContent(ItemRef item)
        {
            if (item.Kind != EntryKind.File) return null;
            lock (_gate) _opened.Add(item.Name);
            return new MemoryContentSource(item.Name, "x"u8.ToArray());
        }
    }
}
