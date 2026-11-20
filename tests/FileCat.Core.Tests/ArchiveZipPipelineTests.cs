using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using FileCat.Archives.Writing;
using FileCat.Core.Archives;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using Child = FileCat.Core.Tests.ArchiveAssessmentClientTests.Child;
using Raw = FileCat.Core.Tests.ArchiveZipCatalogTests;

namespace FileCat.Core.Tests;

/// <summary>
/// AW2 task 5: the worker ZIP profile through real assessment, transform and fresh-verification processes, with the runtime's
/// own ZIP reader as an independent oracle and a functional comparison with the existing ZIP route.
/// </summary>
[Collection(ArchiveWorkerIntegrationCollection.Name)]
public sealed class ArchiveZipPipelineTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly ArchiveWriteProfile Zip = new(ArchiveWriteFormat.Zip);
    private static readonly byte[] Text = Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat("worker ZIP pipeline payload ", 400)));
    private static string Hash(byte[] b) => Convert.ToHexString(SHA256.HashData(b));
    private static ArchiveMemberKey Key(ArchiveAssessment source, string name, ArchiveMemberType type = ArchiveMemberType.File) =>
        new(source.CatalogRevision, name, type, 0);

    private static Task<ArchiveAssessment> Assess(byte[] zip) => new ArchiveAssessmentClient((n, _) => Child.Start(n))
        .AssessAsync(new(Zip, zip.Length, Hash(zip)), () => new MemoryStream(zip, false), Ct);

    private static async Task<byte[]> Transform(ArchiveZipTransformPlan plan, byte[]? original, IReadOnlyDictionary<int, byte[]> payloads)
    {
        var invocation = ArchiveTransformWire.Prepare(plan, Ct); using var stage = new MemoryStream(); Child? transform = null, verifier = null;
        var receipt = await new ArchiveTransformClient((n, _) => transform = Child.Start(n, "archive-transform"))
            .RebuildAsync(invocation, stage, () => new MemoryStream(original!, false), id => new MemoryStream(payloads[id], false), Ct);
        byte[] result = stage.ToArray(); Assert.Equal(result.Length, receipt.Length);
        Assert.InRange(result.Length, ArchiveTransformWire.MinimumOutputLength(ArchiveWriteFormat.Zip), invocation.Request.OutputLengthBound);
        var verified = await new ArchiveVerificationClient((n, _) => verifier = Child.Start(n, "archive-verify"))
            .VerifyAsync(new("owned-parent-stage", result.Length, Hash(result)), plan.Expected, () => new MemoryStream(result, false), Ct);
        Assert.Equal(plan.Expected.Digest, verified.ManifestDigest); Assert.True(transform!.Reaped); Assert.True(verifier!.Reaped);
        return result;
    }

    private static byte[] Content(ZipArchive archive, string name)
    { using var stream = archive.GetEntry(name)!.Open(); using var copy = new MemoryStream(); stream.CopyTo(copy); return copy.ToArray(); }

    private static ReadOnlySpan<byte> RawData(byte[] zip, ZipWriteMember member) => zip.AsSpan((int)member.DataOffset, (int)member.CompressedLength);

    [Fact]
    public async Task A_new_zip_is_read_by_the_runtime_with_local_dos_times_and_the_exact_utc_second()
    {
        var modified = new ArchiveTimestamp(1700000000, 123_000_000);
        ArchiveTransformInput[] inputs = [new(1, Text.Length, Hash(Text), modified, 0x1ed), new(2, 0, Hash([]), modified, 0x180)];
        var plan = ArchiveZipTransformPlan.Create(Zip, null, inputs, [],
            [new("docs", new ArchiveTransformDirectoryMetadata(modified)), new("docs/run.sh", ArchiveMemberType.File, 1), new("ñame.txt", ArchiveMemberType.File, 2)], Ct);
        Assert.Equal(new[] { "docs/", "docs/run.sh", "ñame.txt" }, plan.Expected.Members.Select(m => m.Name));
        byte[] zip = await Transform(plan, null, new Dictionary<int, byte[]> { [1] = Text, [2] = [] });

        using var archive = new ZipArchive(new MemoryStream(zip, false), ZipArchiveMode.Read);
        Assert.Equal(new[] { "docs/", "docs/run.sh", "ñame.txt" }, archive.Entries.Select(e => e.FullName));
        Assert.Equal(Text, Content(archive, "docs/run.sh")); Assert.Empty(Content(archive, "ñame.txt"));
        var instant = DateTimeOffset.FromUnixTimeSeconds(1700000000); var local = instant.ToOffset(TimeZoneInfo.Local.GetUtcOffset(instant)).DateTime;
        Assert.Equal(new DateTime(local.Year, local.Month, local.Day, local.Hour, local.Minute, local.Second / 2 * 2), archive.GetEntry("docs/run.sh")!.LastWriteTime.DateTime);
        Assert.Equal(unchecked((int)(0x81ed0000u)), archive.GetEntry("docs/run.sh")!.ExternalAttributes);
        var written = StrictZipCatalog.Read(new MemoryStream(zip, false), Ct);
        var metadata = ArchiveZipMetadata.Decode(written.Members[1].Metadata);
        Assert.Equal((1, 1700000000, (int)ArchiveZipMetadata.UnixHost), ((int)metadata.TimeFlags, metadata.Modified, metadata.VersionMadeBy >> 8));
        Assert.Equal(0x41ed0010u, ArchiveZipMetadata.Decode(written.Members[0].Metadata).ExternalAttributes);
    }

    [Fact]
    public async Task An_update_keeps_unchanged_members_byte_for_byte_and_applies_each_reviewed_change()
    {
        byte[] random = new byte[5000]; new Random(5).NextBytes(random);
        using var built = new MemoryStream();
        using (var archive = new ZipArchive(built, ZipArchiveMode.Create, leaveOpen: true))
        {
            archive.Comment = "kept archive comment";
            foreach (var (name, data, level) in new[] { ("keep.txt", Text, CompressionLevel.Optimal), ("stored.bin", random, CompressionLevel.NoCompression),
                ("old.txt", Text, CompressionLevel.Fastest), ("rename-me.txt", Text, CompressionLevel.Optimal), ("replace.txt", Text, CompressionLevel.Optimal) })
            {
                var entry = archive.CreateEntry(name, level); entry.LastWriteTime = new DateTimeOffset(2020, 2, 3, 4, 5, 6, TimeSpan.Zero);
                if (name == "replace.txt") entry.Comment = "replaced member keeps this comment";
                using var stream = entry.Open(); stream.Write(data);
            }
            archive.CreateEntry("dir/");
        }
        byte[] original = built.ToArray();
        var source = await Assess(original);
        byte[] replacement = "replacement contents"u8.ToArray(), added = "added contents"u8.ToArray();
        var at = new ArchiveTimestamp(1710000000, 0);
        ArchiveTransformInput[] inputs = [new(1, replacement.Length, Hash(replacement), at), new(2, added.Length, Hash(added), at)];
        ArchiveTransformEdit[] edits = [new(ArchiveChangeKind.Delete, Key(source, "old.txt")), new(ArchiveChangeKind.Rename, Key(source, "rename-me.txt"), "renamed.txt"),
            new(ArchiveChangeKind.Replace, Key(source, "replace.txt"), inputId: 1)];
        var plan = ArchiveZipTransformPlan.Create(Zip, source, inputs, edits, [new("dir/added.txt", ArchiveMemberType.File, 2)], Ct);
        byte[] result = await Transform(plan, original, new Dictionary<int, byte[]> { [1] = replacement, [2] = added });

        using var after = new ZipArchive(new MemoryStream(result, false), ZipArchiveMode.Read);
        Assert.Equal(new[] { "keep.txt", "stored.bin", "renamed.txt", "replace.txt", "dir/", "dir/added.txt" }, after.Entries.Select(e => e.FullName));
        Assert.Equal("kept archive comment", after.Comment);
        Assert.Equal(Text, Content(after, "keep.txt")); Assert.Equal(random, Content(after, "stored.bin")); Assert.Equal(Text, Content(after, "renamed.txt"));
        Assert.Equal(replacement, Content(after, "replace.txt")); Assert.Equal(added, Content(after, "dir/added.txt"));
        var before = StrictZipCatalog.Read(new MemoryStream(original, false), Ct); var rebuilt = StrictZipCatalog.Read(new MemoryStream(result, false), Ct);
        Assert.True(RawData(original, before.Members[0]).SequenceEqual(RawData(result, rebuilt.Members[0])), "Unchanged compressed bytes are copied.");
        Assert.True(RawData(original, before.Members[3]).SequenceEqual(RawData(result, rebuilt.Members[2])), "A rename keeps the compressed bytes.");
        Assert.Equal(0, rebuilt.Members[1].Method); // a stored member stays stored
        // A replacement changes only the payload: the recorded time, attributes and comment stay.
        Assert.Equal(before.Members[4].Metadata, rebuilt.Members[3].Metadata);
        Assert.Equal("replaced member keeps this comment", after.GetEntry("replace.txt")!.Comment);
    }

    [Fact]
    public async Task Info_zip_times_owners_and_ntfs_times_survive_an_update()
    {
        byte[] local = [.. Raw.Times(3, 1700000000, 1700000100), .. Raw.Owner(501, 20), .. Raw.Ntfs(11, 22, 33)];
        byte[] central = [.. Raw.Times(3, 1700000000), .. Raw.Extra(0x7875, []), .. Raw.Ntfs(11, 22, 33)];
        byte[] original = Raw.Build([new("a.txt", Text, LocalExtra: local, CentralExtra: central, Comment: "kept", Descriptor: 1)], comment: "zip comment"u8.ToArray());
        var source = await Assess(original);
        byte[] added = "b"u8.ToArray();
        var plan = ArchiveZipTransformPlan.Create(Zip, source, [new(1, 1, Hash(added), new ArchiveTimestamp(1700000000, 0))], [], [new("b.txt", ArchiveMemberType.File, 1)], Ct);
        byte[] result = await Transform(plan, original, new Dictionary<int, byte[]> { [1] = added });
        var before = StrictZipCatalog.Read(new MemoryStream(original, false), Ct); var after = StrictZipCatalog.Read(new MemoryStream(result, false), Ct);
        Assert.Equal(before.Members[0].Metadata, after.Members[0].Metadata);
        var metadata = ArchiveZipMetadata.Decode(after.Members[0].Metadata);
        Assert.Equal((3, 1700000100, "kept"), (metadata.TimeFlags, metadata.Accessed, metadata.Comment));
        Assert.Equal(new ArchiveZipOwner(4, 501, 4, 20), metadata.Owner); Assert.Equal(new ArchiveZipNtfsTimes(11, 22, 33), metadata.Ntfs);
        Assert.Equal("zip comment"u8.ToArray(), after.Comment);
    }

    [Fact]
    public async Task The_store_level_writes_stored_members_behind_a_descriptor()
    {
        var store = new ArchiveWriteProfile(ArchiveWriteFormat.Zip, compressionLevel: 3);
        var plan = ArchiveZipTransformPlan.Create(store, null, [new(1, Text.Length, Hash(Text), new ArchiveTimestamp(1700000000, 0))], [],
            [new("a.txt", ArchiveMemberType.File, 1)], Ct);
        var invocation = ArchiveTransformWire.Prepare(plan, Ct);
        using var stage = new MemoryStream();
        await new ArchiveTransformClient((n, _) => Child.Start(n, "archive-transform"))
            .RebuildAsync(invocation, stage, () => throw new InvalidOperationException("No source."), _ => new MemoryStream(Text, false), Ct);
        byte[] zip = stage.ToArray();
        var verified = await new ArchiveVerificationClient((n, _) => Child.Start(n, "archive-verify"))
            .VerifyAsync(new("owned-parent-stage", zip.Length, Hash(zip)), plan.Expected, () => new MemoryStream(zip, false), Ct);
        Assert.Equal(plan.Expected.Digest, verified.ManifestDigest);
        var member = Assert.Single(StrictZipCatalog.Read(new MemoryStream(zip, false), Ct).Members);
        Assert.Equal((0, (long)Text.Length), (member.Method, member.CompressedLength));
        using var archive = new ZipArchive(new MemoryStream(zip, false), ZipArchiveMode.Read); Assert.Equal(Text, Content(archive, "a.txt"));
    }

    [Fact]
    public async Task A_refused_source_never_reaches_a_plan()
    {
        byte[] encrypted = Raw.Build([new("a.txt", Text, Flags: 1)]);
        var error = await Assert.ThrowsAsync<ArchiveWriteException>(() => Assess(encrypted));
        Assert.Equal(ArchiveWriteReason.UnsupportedFeature, error.Reason);
    }

    // AW2 task 5: the worker profile and the existing ZIP route produce the same names, order, contents, local DOS times and
    // comments for the same captured files. Recorded differences (design note): unchanged members keep their compressed bytes
    // and extra fields; new members add the exact UTC second and record Unix permission bits rather than DOS attribute bits.
    [Fact]
    public async Task The_worker_profile_matches_the_existing_route_for_names_contents_and_local_times()
    {
        using var temp = new TempDir();
        string folder = temp.Dir("tree"), sub = temp.Dir("tree/sub");
        string first = temp.File("tree/first.txt", string.Concat(Enumerable.Repeat("first ", 500))), second = temp.File("tree/sub/second.txt", "second");
        foreach (var (path, seconds) in new[] { (first, 1690000000L), (second, 1690000123L) }) File.SetLastWriteTimeUtc(path, DateTime.UnixEpoch.AddSeconds(seconds));
        Directory.SetLastWriteTimeUtc(sub, DateTime.UnixEpoch.AddSeconds(1690000200));
        string legacy = Path.Combine(temp.Path, "legacy.zip");
        var manager = new JobManager(new PortableFileOperations(), new ProviderRegistry(), temp.Dir("journals"));
        var job = manager.Submit(new JobRequest { Kind = JobKind.ArchiveUpdate, Archive = new ArchivePlan(legacy, null,
            [new(ArchiveChangeKind.AddFile, "first.txt", first), new(ArchiveChangeKind.AddFolder, "sub", sub)]) });
        using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(Ct)) { deadline.CancelAfter(TimeSpan.FromSeconds(60)); while (!job.State.IsFinished()) await Task.Delay(10, deadline.Token); }
        Assert.True(job.State == JobState.Completed, job.State + ": " + string.Join(" | ", job.Issues.Select(i => i.Message)));

        byte[] one = File.ReadAllBytes(first), two = File.ReadAllBytes(second);
        ArchiveTransformInput Input(int id, string path, byte[] data) => new(id, data.Length, Hash(data), (ArchiveTimestamp)new DateTimeOffset(File.GetLastWriteTimeUtc(path)));
        var plan = ArchiveZipTransformPlan.Create(Zip, null, [Input(1, first, one), Input(2, second, two)], [],
            [new("first.txt", ArchiveMemberType.File, 1), new("sub", new ArchiveTransformDirectoryMetadata((ArchiveTimestamp)new DateTimeOffset(Directory.GetLastWriteTimeUtc(sub)))),
             new("sub/second.txt", ArchiveMemberType.File, 2)], Ct);
        byte[] worker = await Transform(plan, null, new Dictionary<int, byte[]> { [1] = one, [2] = two });

        using var existing = ZipFile.OpenRead(legacy); using var rebuilt = new ZipArchive(new MemoryStream(worker, false), ZipArchiveMode.Read);
        static IEnumerable<string> Files(ZipArchive archive) => archive.Entries.Where(e => !e.FullName.EndsWith('/')).Select(e => e.FullName);
        Assert.Equal(Files(existing), Files(rebuilt));
        foreach (string name in Files(existing))
        {
            var entry = existing.GetEntry(name)!; var other = rebuilt.GetEntry(name)!;
            Assert.Equal(Content(existing, name), Content(rebuilt, name));
            Assert.Equal(entry.LastWriteTime.DateTime, other.LastWriteTime.DateTime);
            Assert.Equal(entry.Comment, other.Comment);
        }
        // Recorded difference: like the TAR and 7z routes, the worker route keeps each captured folder as its own entry with
        // its time and permissions; the existing route writes folder entries only for empty folders.
        Assert.DoesNotContain(existing.Entries, e => e.FullName.EndsWith('/'));
        Assert.Equal(new[] { "sub/" }, rebuilt.Entries.Where(e => e.FullName.EndsWith('/')).Select(e => e.FullName));
    }

    // Independent review (v15c): a member flagged UTF-8 whose name is ASCII but whose comment holds a control character keeps
    // the flag when rewritten. Without it the strict catalog, and so fresh verification, refused the writer's own output.
    [Fact]
    public async Task A_utf8_flagged_member_with_a_control_character_in_its_comment_keeps_the_flag_when_rewritten()
    {
        byte[] source = Raw.Build([new Raw.RawEntry("a.txt", Text, Flags: 0x800, CommentBytes: [0x1b, (byte)'x'])]);
        var assessed = await Assess(source);
        byte[] added = "added"u8.ToArray();
        ArchiveTransformInput[] inputs = [new(1, added.Length, Hash(added), new ArchiveTimestamp(1700000000, 0))];
        var plan = ArchiveZipTransformPlan.Create(Zip, assessed, inputs, [], [new("b.txt", ArchiveMemberType.File, 1)], Ct);
        byte[] zip = await Transform(plan, source, new Dictionary<int, byte[]> { [1] = added });
        var catalog = StrictZipCatalog.Read(new MemoryStream(zip, false), Ct);
        Assert.Equal("\u001bx", ArchiveZipMetadata.Decode(catalog.Members[0].Metadata).Comment);
        Assert.Equal(new[] { "a.txt", "b.txt" }, catalog.Members.Select(m => m.Name));
    }

    // Independent review (v15c): ZIP stores a name's length in 16 bits. A longer name, which the shared rules allow for TAR,
    // is refused by the plan with a plain reason instead of failing in the worker that writes the header.
    [Fact]
    public void A_name_longer_than_a_zip_header_can_hold_is_refused_before_any_worker_starts()
    {
        string name = new string((char)0x20ac, 25_000) + ".txt"; // 75,004 UTF-8 bytes, within the shared 128,000-byte rule
        ArchiveTransformInput[] inputs = [new(1, Text.Length, Hash(Text), new ArchiveTimestamp(1700000000, 0))];
        var error = Assert.Throws<ArchiveWriteException>(() =>
            ArchiveZipTransformPlan.Create(Zip, null, inputs, [], [new(name, ArchiveMemberType.File, 1)], Ct));
        Assert.Equal(ArchiveWriteReason.InvalidRequest, error.Reason);
        Assert.Contains("65,535 bytes", error.Message, StringComparison.Ordinal);
    }
}
