using System.Security.Cryptography;
using FileCat.Core.Archives;

namespace FileCat.Core.Tests;

public sealed partial class ArchiveCapturedInputsTests
{
    public static IEnumerable<object[]> WorkflowProfiles()
    {
        foreach (var format in new[] { ArchiveWriteFormat.Tar, ArchiveWriteFormat.TarGzip, ArchiveWriteFormat.TarBzip2, ArchiveWriteFormat.TarZstd })
            yield return [format, ArchiveEncryptionMode.None];
        foreach (var format in new[] { ArchiveWriteFormat.Rar5, ArchiveWriteFormat.SevenZip })
            foreach (var mode in Enum.GetValues<ArchiveEncryptionMode>()) yield return [format, mode];
    }
    private static ArchiveWriteProfile WorkflowProfile(ArchiveWriteFormat format, ArchiveEncryptionMode mode)
        => format == ArchiveWriteFormat.SevenZip ? ArchiveWriteProfile.ForSevenZip(encryption: mode == ArchiveEncryptionMode.None ? null : new(mode)) :
            format == ArchiveWriteFormat.Rar5 ? ArchiveWriteProfile.ForRar5(encryption: mode == ArchiveEncryptionMode.None ? null : new(mode)) : new(format);
    private static ArchiveContainerTransformPlan WorkflowCreationPlan(ArchiveWriteProfile profile)
    {
        string digest = Convert.ToHexString(SHA256.HashData(new byte[] { 9 }));
        var inputs = new[] { new ArchiveTransformInput(7, 1, digest, new ArchiveTimestamp(10, 100)) };
        ArchiveTransformAddition[] additions = [new("tree/", new ArchiveTransformDirectoryMetadata(new(10, 100), 0x1ed)),
            new("tree/old", ArchiveMemberType.File, 7), new("treehouse/kept", ArchiveMemberType.File, 7),
            new("Case", ArchiveMemberType.File, 7), new("case", ArchiveMemberType.File, 7),
            new("é", ArchiveMemberType.File, 7), new("é", ArchiveMemberType.File, 7)];
        return profile.Format switch
        {
            ArchiveWriteFormat.Rar5 => ArchiveRar5TransformPlan.Create(profile, null, inputs, [], additions, Ct),
            ArchiveWriteFormat.SevenZip => ArchiveSevenZipTransformPlan.Create(profile, null, inputs, [], additions, Ct),
            _ => ArchiveTarTransformPlan.Create(profile, null, inputs, [], additions, Ct),
        };
    }
    private static ArchiveAssessment WorkflowSource(ArchiveWriteProfile profile, bool duplicates = false, bool implicitFolder = false)
    {
        var plan = WorkflowCreationPlan(profile);
        var members = plan.Expected.Members.Where(m => !implicitFolder || m.Type != ArchiveMemberType.Directory).ToList();
        if (duplicates)
        {
            var original = members.Single(m => m.Name == "tree/old");
            members.Insert(members.IndexOf(original) + 1, new(original.Name, original.Type, 1, original.Length, original.Sha256, original.Metadata));
        }
        string revision = new('A', 64); return new(new(profile, members, plan.Expected.Features), revision, 200, revision);
    }

    [Theory] [MemberData(nameof(WorkflowProfiles))]
    public void Shared_workflow_merges_directory_provenance_preserves_existing_metadata_and_replaces_all_reviewed_duplicates(
        ArchiveWriteFormat format, ArchiveEncryptionMode mode)
    {
        var profile = WorkflowProfile(format, mode); var source = WorkflowSource(profile, duplicates: true);
        using var captured = new ArchiveCapturedInputs(); var input = new Source(1) { Modified = new(1700000000, 1) };
        var folder = Folder(2, modified: new(1700000001, 2), mode: 0x1c0);
        captured.Capture(1, input.Open, Ct); captured.CaptureDirectory(2, _ => folder, Ct); captured.Seal(Ct);
        var review = ArchiveWorkflowReview.Prepare(profile, source, captured, [], [],
            [captured.DirectoryAddition("tree", 2), new("tree/old", ArchiveMemberType.File, 1)], 0, Ct);
        Assert.Equal(1, review.ReplacementNames); Assert.Equal(2, review.ReplacedOccurrences); Assert.Equal(1, review.MergedDirectories);
        Assert.Equal(0, review.InputTimePrecisionChanges); Assert.Equal(new[] { "tree/old" }, review.ReplacementSamples);
        Assert.Throws<ArchiveWriteException>(() => review.CreatePlan(false, true, Ct));
        var plan = review.CreatePlan(true, false, Ct); ArchiveCapturedInputs.RequirePlanCost(plan, captured.Bindings);
        Assert.Empty(plan.Additions); Assert.Equal(2, plan.Edits.Count); Assert.Single(plan.Directories);
        Assert.Equal(source.Manifest.Members[0].Metadata.ToArray(), plan.Expected.Members[0].Metadata.ToArray());
        var replacement = Assert.Single(plan.Expected.Members, m => m.Name == "tree/old");
        if (format == ArchiveWriteFormat.Rar5)
        {
            var before = ArchiveRar5Metadata.Decode(source.Manifest.Members[1].Metadata); var after = ArchiveRar5Metadata.Decode(replacement.Metadata);
            Assert.Equal(before.Attributes, after.Attributes); Assert.Equal(before.Times, after.Times);
        }
        else Assert.Equal(source.Manifest.Members[1].Metadata.ToArray(), replacement.Metadata.ToArray());
        Assert.Equal(0, replacement.Occurrence); Assert.Equal(captured.Inputs[0].Sha256, replacement.Sha256);
        var wire = ArchiveTransformWire.Prepare(plan, Ct); Assert.Single(wire.Request.Inputs); Assert.Empty(wire.Request.Additions);
        Assert.All(wire.Request.Edits, edit => Assert.Equal(ArchiveMemberType.File, edit.Key.Type));
        folder = Folder(2, modified: new(1700000001, 3), mode: 0x1c0); Assert.Throws<ArchiveWriteException>(() => plan.Directories[2].Review(Ct));
        Assert.Equal(1, input.Opened); // Directory provenance and semantic planning grant no file read.
    }

    [Theory] [MemberData(nameof(WorkflowProfiles))]
    public void Recursive_workflow_targets_only_the_exact_reviewed_folder_prefix_including_implicit_folders(
        ArchiveWriteFormat format, ArchiveEncryptionMode mode)
    {
        var profile = WorkflowProfile(format, mode); var source = WorkflowSource(profile, implicitFolder: true);
        using var captured = new ArchiveCapturedInputs(); captured.Seal(Ct);
        var folder = new ArchiveFolderChange(ArchiveChangeKind.Rename, source.CatalogRevision, "tree", "renamed");
        var review = ArchiveWorkflowReview.Prepare(profile, source, captured, [], [folder], [], 0, Ct);
        var plan = review.CreatePlan(false, false, Ct); Assert.Single(plan.Edits);
        Assert.Contains(plan.Expected.Members, m => m.Name == "renamed/old");
        Assert.Contains(plan.Expected.Members, m => m.Name == "treehouse/kept");
        Assert.DoesNotContain(plan.Expected.Members, m => m.Name.StartsWith("tree/", StringComparison.Ordinal));
        Assert.All(plan.Expected.Members, m => Assert.Equal(source.Manifest.Members.Single(old => old.Name == (m.Name == "renamed/old" ? "tree/old" : m.Name)).Metadata.ToArray(), m.Metadata.ToArray()));
    }

    [Theory] [InlineData("owner")] [InlineData("id")] [InlineData("revision")] [InlineData("file")] [InlineData("edited")] [InlineData("duplicate")]
    public void Directory_merge_associations_cannot_drop_capture_ownership_or_target_an_edited_or_unrelated_member(string damage)
    {
        var profile = ArchiveWriteProfile.ForSevenZip(); var source = WorkflowSource(profile);
        using var first = new ArchiveCapturedInputs(); using var second = new ArchiveCapturedInputs();
        first.CaptureDirectory(2, _ => Folder(2), Ct); first.Seal(Ct); second.Seal(Ct);
        var member = source.Manifest.Members[damage == "file" ? 1 : 0];
        var key = new ArchiveMemberKey(damage == "revision" ? new('B', 64) : source.CatalogRevision, member.Name, member.Type, member.Occurrence);
        ArchiveTransformEdit[] edits = damage == "edited" ? [new(ArchiveChangeKind.Delete, key)] : [];
        var merge = new ArchiveCapturedDirectoryMerge(damage == "id" ? 3 : 2, key);
        Assert.Throws<ArchiveWriteException>(() => (damage == "owner" ? second : first).CreatePlan(profile, source, edits, [], Ct,
            damage == "duplicate" ? [merge, merge] : [merge]));
    }

    [Theory] [MemberData(nameof(WorkflowProfiles))]
    public void Exact_workflow_edits_do_not_case_fold_or_normalize_unicode_and_requests_are_frozen(
        ArchiveWriteFormat format, ArchiveEncryptionMode mode)
    {
        var profile = WorkflowProfile(format, mode); var source = WorkflowSource(profile); using var captured = new ArchiveCapturedInputs(); captured.Seal(Ct);
        var selected = source.Manifest.Members.Single(m => m.Name == "é");
        var edits = new List<ArchiveTransformEdit> { new(ArchiveChangeKind.Delete, new(source.CatalogRevision, selected.Name, selected.Type, 0)) };
        var review = ArchiveWorkflowReview.Prepare(profile, source, captured, edits, [], [], 0, Ct); edits.Clear();
        var plan = review.CreatePlan(false, false, Ct); Assert.DoesNotContain(plan.Expected.Members, m => m.Name == "é");
        Assert.Contains(plan.Expected.Members, m => m.Name == "é"); Assert.Contains(plan.Expected.Members, m => m.Name == "Case"); Assert.Contains(plan.Expected.Members, m => m.Name == "case");
    }

    [Fact]
    public void Sevenzip_new_input_precision_is_reviewed_while_native_rereviews_retain_every_source_nanosecond()
    {
        var profile = ArchiveWriteProfile.ForSevenZip(); var source = new Source(1) { Modified = new(-1, 999999999) };
        using var captured = new ArchiveCapturedInputs(); captured.Capture(1, source.Open, Ct);
        captured.CaptureDirectory(2, _ => Folder(2, modified: new(0, 99)), Ct); captured.Seal(Ct);
        var review = ArchiveWorkflowReview.Prepare(profile, null, captured, [], [],
            [new("file", ArchiveMemberType.File, 1), captured.DirectoryAddition("folder", 2)], 1, Ct);
        Assert.Equal(2, review.InputTimePrecisionChanges);
        Assert.Throws<ArchiveWriteException>(() => review.CreatePlan(false, false, Ct, acceptTimestampPrecision: true));
        Assert.Throws<ArchiveWriteException>(() => review.CreatePlan(false, true, Ct));
        var plan = review.CreatePlan(false, true, Ct, acceptTimestampPrecision: true);
        Assert.Equal(ArchiveSevenZipEncoding.FileTime(new(-1, 999999900)), ArchiveSevenZipMetadata.Decode(plan.Expected.Members[0].Metadata).ModifiedFileTime);
        Assert.Equal(ArchiveSevenZipEncoding.FileTime(new(0, 0)), ArchiveSevenZipMetadata.Decode(plan.Expected.Members[1].Metadata).ModifiedFileTime);
        Assert.Equal(999999999, plan.Inputs[1].ModifiedTimestamp.Nanoseconds);
        source.Modified = new(-1, 999999998); var binding = captured.Bindings[1];
        Assert.ThrowsAny<IOException>(() => binding.File.OpenReadView(binding.Reviewed, Ct));
    }

    [Theory] [InlineData("stale")] [InlineData("missing-folder")] [InlineData("file-as-folder")] [InlineData("overlap")]
    [InlineData("wrong-input")] [InlineData("duplicate-incoming")] [InlineData("case-collision")] [InlineData("file-directory")]
    public void Shared_workflow_refuses_stale_ambiguous_or_unbound_scope_before_granting_IO(string damage)
    {
        var profile = ArchiveWriteProfile.ForSevenZip(); var source = WorkflowSource(profile); using var captured = new ArchiveCapturedInputs(); captured.Seal(Ct);
        var first = source.Manifest.Members.Single(m => m.Name == "tree/old");
        var key = new ArchiveMemberKey(source.CatalogRevision, first.Name, first.Type, first.Occurrence);
        ArchiveTransformEdit[] edits = damage == "overlap" ? [new(ArchiveChangeKind.Delete, key)] : damage == "case-collision" ?
            [new(ArchiveChangeKind.Rename, new(source.CatalogRevision, "Case", ArchiveMemberType.File, 0), "case")] : [];
        ArchiveFolderChange[] folders = damage switch
        {
            "stale" => [new(ArchiveChangeKind.Delete, new('B', 64), "tree")], "missing-folder" => [new(ArchiveChangeKind.Delete, source.CatalogRevision, "absent")],
            "file-as-folder" => [new(ArchiveChangeKind.Delete, source.CatalogRevision, "Case")], "overlap" => [new(ArchiveChangeKind.Delete, source.CatalogRevision, "tree")], _ => [],
        };
        ArchiveTransformAddition[] additions = damage switch
        {
            "wrong-input" => [new("new", ArchiveMemberType.File, 37)],
            "duplicate-incoming" => [new("new", ArchiveMemberType.Directory), new("new/", ArchiveMemberType.Directory)],
            "file-directory" => [new("Case", ArchiveMemberType.Directory)], _ => [],
        };
        Assert.Throws<ArchiveWriteException>(() => ArchiveWorkflowReview.Prepare(profile, source, captured, edits, folders, additions, 0, Ct)
            .CreatePlan(true, true, Ct));
    }

    [Theory] [InlineData(-1L, 999999999, -1L, 999999900)] [InlineData(0L, 99, 0L, 0)]
    [InlineData(1700000000L, 123456789, 1700000000L, 123456700)] [InlineData(-11644473600L, 99, -11644473600L, 0)]
    public void Sevenzip_capture_precision_conversion_always_uses_the_earlier_representable_instant(long seconds, int nanos, long expectedSeconds, int expectedNanos)
        => Assert.Equal(ArchiveSevenZipEncoding.FileTime(new(expectedSeconds, expectedNanos)), ArchiveSevenZipEncoding.CapturedFileTime(new(seconds, nanos)));

    [Fact]
    public void Sevenzip_capture_precision_does_not_weaken_FILETIME_range_checks_or_existing_unsigned_values()
    {
        Assert.Equal(ArchiveSevenZipEncoding.FileTime(new(253402300799L, 999999900)),
            ArchiveSevenZipEncoding.CapturedFileTime(new(253402300799L, 999999999)));
        Assert.Throws<ArchiveWriteException>(() => ArchiveSevenZipEncoding.CapturedFileTime(new(-11644473601L, 999999999)));
        Assert.Throws<ArchiveWriteException>(() => ArchiveSevenZipEncoding.CapturedFileTime(new(long.MaxValue, 0)));
        Assert.Equal(ulong.MaxValue, ArchiveSevenZipMetadata.Decode(new ArchiveSevenZipMetadata(ArchiveMemberType.Directory,
            modifiedFileTime: ulong.MaxValue).Encode()).ModifiedFileTime);
    }

    [Fact]
    public void Workflow_source_manifest_and_capture_heap_share_one_review_budget_before_maps_are_built()
    {
        var profile = new ArchiveWriteProfile(ArchiveWriteFormat.Tar); var prototype = WorkflowSource(profile).Manifest.Members[1];
        var members = Enumerable.Range(0, 1000).Select(i => new ArchiveManifestMember("file-" + i, ArchiveMemberType.File, 0, prototype.Length, prototype.Sha256, prototype.Metadata)).ToArray();
        string revision = new('A', 64); var source = new ArchiveAssessment(new(profile, members, [1, 2]), revision, 1024, revision);
        using var captured = new ArchiveCapturedInputs(); captured.RetainSelection(0, ArchiveWritePolicy.MaxRetainedHeapBytes - 900000); captured.Seal(Ct);
        Assert.Equal(ArchiveWriteReason.BudgetExceeded, Assert.Throws<ArchiveWriteException>(() =>
            ArchiveWorkflowReview.Prepare(profile, source, captured, [], [], [], 0, Ct)).Reason);
    }

    [Fact]
    public void Workflow_recursive_rename_reserves_generated_names_before_constructing_them()
    {
        var profile = ArchiveWriteProfile.ForSevenZip(); var source = WorkflowSource(profile); using var captured = new ArchiveCapturedInputs(); captured.Seal(Ct);
        Assert.Equal(ArchiveWriteReason.BudgetExceeded, Assert.Throws<ArchiveWriteException>(() => ArchiveWorkflowReview.Prepare(profile, source,
            captured, [], [new(ArchiveChangeKind.Rename, source.CatalogRevision, "tree", new string('x', ArchiveWritePolicy.MaxNameChars))], [], 0, Ct)).Reason);
    }

    [Theory] [MemberData(nameof(WorkflowProfiles))]
    public async Task Shared_workflow_canonical_requests_merge_replace_rename_and_add_through_real_workers_and_fresh_verification(
        ArchiveWriteFormat format, ArchiveEncryptionMode mode)
    {
        const string passwordText = "Public-7z-worker-暗号-42829324";
        var profile = WorkflowProfile(format, mode); var children = new List<ArchiveAssessmentClientTests.Child>();
        using var password = mode == ArchiveEncryptionMode.None ? null : new ArchiveJobPassword(passwordText);
        IArchiveWorkerProcess Start(Guid nonce, string role) { var child = ArchiveAssessmentClientTests.Child.Start(nonce, role); children.Add(child); return child; }
        ArchiveTransformClient Transformer() => new((nonce, _) => Start(nonce, "archive-transform-v2"), protocolVersion: 2);
        var initial = WorkflowCreationPlan(profile); using var first = new MemoryStream();
        await Transformer().RebuildAsync(ArchiveTransformWire.Prepare(initial, Ct), first, () => throw new InvalidOperationException(),
            _ => new MemoryStream(new byte[] { 9 }, false), Ct, password?.CreateGrant());
        byte[] original = first.ToArray(); string digest = Convert.ToHexString(SHA256.HashData(original));
        var assessment = await new ArchiveAssessmentClient((nonce, _) => Start(nonce, "archive-assess-v2"), protocolVersion: 2)
            .AssessAsync(new(profile, original.Length, digest), () => new MemoryStream(original, false), Ct, password?.CreateGrant());
        Assert.Equal(initial.Expected.Digest, assessment.Manifest.Digest);
        using var captured = new ArchiveCapturedInputs(); var input = new Source(1) { Modified = new(1700000000, 1) };
        captured.Capture(1, input.Open, Ct); captured.CaptureDirectory(2, _ => Folder(2, mode: 0x1c0), Ct); captured.Seal(Ct);
        var review = ArchiveWorkflowReview.Prepare(profile, assessment, captured, [],
            [new(ArchiveChangeKind.Rename, assessment.CatalogRevision, "treehouse", "neighbor")],
            [captured.DirectoryAddition("tree", 2), new("tree/old", ArchiveMemberType.File, 1), new("tree/new", ArchiveMemberType.File, 1)], 0, Ct);
        var plan = review.CreatePlan(true, false, Ct, acceptTimestampPrecision: true);
        using var output = new MemoryStream();
        await Transformer().RebuildAsync(ArchiveTransformWire.Prepare(plan, Ct), output, () => new MemoryStream(original, false),
            id => captured.Bindings[id].File.OpenReadView(captured.Bindings[id].Reviewed, Ct), Ct, password?.CreateGrant());
        byte[] rebuilt = output.ToArray();
        var verified = await new ArchiveVerificationClient((nonce, _) => Start(nonce, "archive-verify-v2"), protocolVersion: 2)
            .VerifyAsync(new("sealed-parent-stage", rebuilt.Length, Convert.ToHexString(SHA256.HashData(rebuilt))), plan.Expected,
                () => new MemoryStream(rebuilt, false), Ct, password?.CreateGrant());
        Assert.Equal(plan.Expected.Digest, verified.ManifestDigest); Assert.Equal(digest, Convert.ToHexString(SHA256.HashData(original)));
        Assert.Equal(initial.Expected.Members[0].Metadata.ToArray(), plan.Expected.Members[0].Metadata.ToArray());
        Assert.Equal(4, children.Count); Assert.All(children, child => { Assert.True(child.Reaped); Assert.True(child.Disposed); });
        if (Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_WORKFLOW_EXPORT_ROOT") is { } export)
        {
            string folder = Path.Combine(export, $"format-{format}-mode-{mode}"); Directory.CreateDirectory(folder);
            string extension = format switch { ArchiveWriteFormat.Tar => ".tar", ArchiveWriteFormat.TarGzip => ".tar.gz", ArchiveWriteFormat.TarBzip2 => ".tar.bz2",
                ArchiveWriteFormat.TarZstd => ".tar.zst", ArchiveWriteFormat.Rar5 => ".rar", _ => ".7z" };
            Write("source", original, initial.Expected); Write("rebuilt", rebuilt, plan.Expected);
            void Write(string leaf, byte[] bytes, ArchiveWriteManifest manifest)
            {
                System.IO.File.WriteAllBytes(Path.Combine(folder, leaf + extension), bytes);
                System.IO.File.WriteAllText(Path.Combine(folder, leaf + ".json"), System.Text.Json.JsonSerializer.Serialize(new
                {
                    synthetic_public_vector = true, password = passwordText, format = (int)format, mode = (int)mode,
                    revision = Convert.ToHexString(SHA256.HashData(bytes)), features = Convert.ToHexString(manifest.Features),
                    runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                    os = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
                    architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
                    members = manifest.Members.Select(m => new { name = m.Name, type = (int)m.Type, length = m.Length, sha256 = m.Sha256, metadata = Convert.ToHexString(m.Metadata) }),
                }));
            }
        }
    }
}
