using System.Buffers.Binary;
using System.Formats.Tar;
using System.Security.Cryptography;
using FileCat.Archives.Writing;
using FileCat.Core.Archives;
using Child = FileCat.Core.Tests.ArchiveAssessmentClientTests.Child;

namespace FileCat.Core.Tests;

public sealed class ArchiveTarTransformPlanTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static ArchiveTransformInput Input(int id, byte[] bytes, DateTimeOffset? modified = null) =>
        new(id, bytes.Length, Hash(bytes), modified ?? DateTimeOffset.UnixEpoch.AddTicks(1234567));
    private static Task<ArchiveAssessment> Assess(byte[] bytes) => new ArchiveAssessmentClient((nonce, _) => Child.Start(nonce))
        .AssessAsync(new(new(ArchiveWriteFormat.Tar), bytes.Length, Hash(bytes)), () => new MemoryStream(bytes, writable: false), Ct);
    private static ArchiveMemberKey Key(ArchiveAssessment source, int i)
    { var member = source.Manifest.Members[i]; return new(source.CatalogRevision, member.Name, member.Type, member.Occurrence); }

    private static async Task RebuildAndVerify(ArchiveTarTransformPlan plan, byte[]? original, IReadOnlyDictionary<int, byte[]> inputs)
    {
        using var before = original is null ? null : new MemoryStream(original, writable: false);
        using var stage = new MemoryStream();
        // Deliberately ignore the transform's receipt/expected manifest. The expectation already exists in the parent.
        _ = TarRewriteEngine.Rebuild(before, plan.Source?.CatalogRevision, stage,
            plan.Edits.Select(e => new TarWriteEdit(e.Kind, e.Key, e.NewName, e.InputId)).ToArray(),
            plan.Additions.Select(a => new TarWriteAddition(a.Name, a.Type, a.InputId)).ToArray(),
            plan.Inputs.ToDictionary(p => p.Key, p => new TarInputDescriptor(p.Key, p.Value.Length, p.Value.Sha256, p.Value.ModifiedTimestamp, p.Value.Mode)),
            id => new MemoryStream(inputs[id], writable: false), Ct);
        byte[] bytes = stage.ToArray(); Child? verifier = null;
        var result = await new ArchiveVerificationClient((nonce, _) => verifier = Child.Start(nonce, "archive-verify"))
            .VerifyAsync(new("parent-held-stage", bytes.Length, Hash(bytes)), plan.Expected, () => new MemoryStream(bytes, writable: false), Ct);
        Assert.Equal(plan.Expected.Digest, result.ManifestDigest); Assert.True(verifier!.Reaped);
        if (original is not null) Assert.Equal(original, before!.ToArray());
    }

    [Fact]
    public async Task Creation_expectation_precedes_encoding_and_includes_directories_empty_files_and_exact_input_metadata()
    {
        byte[] payload = "parent captured payload"u8.ToArray();
        var input = Input(1, payload, new DateTimeOffset(2024, 1, 1, 1, 0, 0, TimeSpan.FromHours(1)).AddTicks(7));
        var plan = ArchiveTarTransformPlan.Create(null, [input, Input(2, [])], [],
            [new("dir/", ArchiveMemberType.Directory), new("dir/file", ArchiveMemberType.File, 1), new("zero", ArchiveMemberType.File, 2)], Ct);
        Assert.Equal(new[] { "dir/", "dir/file", "zero" }, plan.Expected.Members.Select(m => m.Name));
        Assert.Equal(new byte[] { 1, 2 }, plan.Expected.Features.ToArray());
        Assert.Equal(TimeSpan.Zero, plan.Inputs[1].Modified.Offset);
        await RebuildAndVerify(plan, null, new Dictionary<int, byte[]> { [1] = payload, [2] = [] });
    }

    [Theory]
    [InlineData(TarEntryFormat.Pax)]
    [InlineData(TarEntryFormat.Ustar)]
    public async Task Reviewed_occurrence_edits_preserve_order_and_all_untouched_metadata(TarEntryFormat format)
    {
        byte[] bytes = ArchiveStrictTarTests.Make(format, ("same", "first"), ("same", "second"), ("remove", "removed"), ("dir/", ""));
        var source = await Assess(bytes); byte[] replacement = "new contents"u8.ToArray(), added = "added"u8.ToArray();
        var plan = ArchiveTarTransformPlan.Create(source, [Input(1, replacement), Input(2, added)],
            [new(ArchiveChangeKind.Replace, Key(source, 1), inputId: 1), new(ArchiveChangeKind.Delete, Key(source, 2)),
             new(ArchiveChangeKind.Rename, Key(source, 3), "renamed/")], [new("added", ArchiveMemberType.File, 2)], Ct);
        Assert.Equal(source.Manifest.Members[0].Metadata.ToArray(), plan.Expected.Members[0].Metadata.ToArray());
        Assert.Equal(source.Manifest.Members[1].Metadata.ToArray(), plan.Expected.Members[1].Metadata.ToArray());
        Assert.Equal(source.Manifest.Members[3].Metadata.ToArray(), plan.Expected.Members[2].Metadata.ToArray());
        Assert.Equal(Hash(replacement), plan.Expected.Members[1].Sha256); Assert.Equal(1, plan.Expected.Members[1].Occurrence);
        await RebuildAndVerify(plan, bytes, new Dictionary<int, byte[]> { [1] = replacement, [2] = added });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Deleting_an_earlier_duplicate_renumbers_remaining_occurrences_and_remove_all_has_USTAR_empty_features(bool all)
    {
        byte[] bytes = ArchiveStrictTarTests.Make(TarEntryFormat.Pax, ("same", "first"), ("same", "second"));
        var source = await Assess(bytes);
        var edits = new List<ArchiveTransformEdit> { new(ArchiveChangeKind.Delete, Key(source, 0)) };
        if (all) edits.Add(new(ArchiveChangeKind.Delete, Key(source, 1)));
        var plan = ArchiveTarTransformPlan.Create(source, [], edits, [], Ct);
        if (all) { Assert.Empty(plan.Expected.Members); Assert.Equal(new byte[] { 1, 1 }, plan.Expected.Features.ToArray()); }
        else { Assert.Equal(0, Assert.Single(plan.Expected.Members).Occurrence); Assert.Equal(Hash("second"u8.ToArray()), plan.Expected.Members[0].Sha256); }
        await RebuildAndVerify(plan, bytes, new Dictionary<int, byte[]>());
    }

    [Fact]
    public async Task Empty_creation_has_a_complete_verifiable_expectation()
    {
        var plan = ArchiveTarTransformPlan.Create(null, [], [], [], Ct);
        Assert.Equal(new byte[] { 1, 1 }, plan.Expected.Features.ToArray());
        await RebuildAndVerify(plan, null, new Dictionary<int, byte[]>());
    }

    [Fact]
    public async Task PAX_optional_times_full_permission_bits_large_ids_and_unicode_owner_survive_a_replacement()
    {
        using var stream = new MemoryStream();
        using (var writer = new TarWriter(stream, TarEntryFormat.Pax, leaveOpen: true))
        {
            using var data = new MemoryStream("before"u8.ToArray());
            writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "member", new Dictionary<string, string> { ["atime"] = "-0.1234567", ["ctime"] = "0.7654321" })
            {
                DataStream = data, ModificationTime = DateTimeOffset.UnixEpoch.AddTicks(-1), Mode = (UnixFileMode)0x9ed,
                Uid = int.MaxValue, Gid = int.MaxValue - 1, UserName = "žluťoučký", GroupName = "世界"
            });
        }
        byte[] bytes = stream.ToArray(), payload = "after"u8.ToArray(); var source = await Assess(bytes);
        var plan = ArchiveTarTransformPlan.Create(source, [Input(1, payload)], [new(ArchiveChangeKind.Replace, Key(source, 0), inputId: 1)], [], Ct);
        Assert.Equal(source.Manifest.Members[0].Metadata.ToArray(), plan.Expected.Members[0].Metadata.ToArray());
        await RebuildAndVerify(plan, bytes, new Dictionary<int, byte[]> { [1] = payload });
    }

    [Fact]
    public async Task Distinct_case_and_unicode_names_remain_distinct_and_in_order()
    {
        string[] names = ["Name", "name", "é", "e\u0301"];
        var plan = ArchiveTarTransformPlan.Create(null, [Input(1, [])], [], names.Select(n => new ArchiveTransformAddition(n, ArchiveMemberType.File, 1)).ToArray(), Ct);
        Assert.Equal(names, plan.Expected.Members.Select(m => m.Name));
        await RebuildAndVerify(plan, null, new Dictionary<int, byte[]> { [1] = [] });
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("type")]
    [InlineData("ancestor")]
    [InlineData("lexical-neighbor")]
    public void Changed_names_and_file_folder_collisions_refuse_without_launching_a_writer(string kind)
    {
        var adds = new List<ArchiveTransformAddition> { new("a", ArchiveMemberType.File, 1) };
        if (kind == "duplicate") adds.Add(new("a", ArchiveMemberType.File, 1));
        else if (kind == "type") adds.Add(new("a/", ArchiveMemberType.Directory));
        else { if (kind == "lexical-neighbor") adds.Add(new("a-other", ArchiveMemberType.File, 1)); adds.Add(new("a/child", ArchiveMemberType.File, 1)); }
        Assert.Equal(ArchiveWriteReason.InvalidRequest, Assert.Throws<ArchiveWriteException>(() =>
            ArchiveTarTransformPlan.Create(null, [Input(1, [])], [], adds, Ct)).Reason);
    }

    [Fact]
    public async Task Stale_unknown_and_repeated_exact_keys_cannot_widen_a_change()
    {
        var source = await Assess(ArchiveStrictTarTests.Make(TarEntryFormat.Pax, ("same", "first"), ("same", "second")));
        foreach (var edits in new ArchiveTransformEdit[][] {
            [new(ArchiveChangeKind.Delete, new(new string('A', 64), "same", ArchiveMemberType.File, 0))],
            [new(ArchiveChangeKind.Delete, new(source.CatalogRevision, "same", ArchiveMemberType.File, 2))],
            [new(ArchiveChangeKind.Delete, Key(source, 0)), new(ArchiveChangeKind.Delete, Key(source, 0))] })
            Assert.Equal(ArchiveWriteReason.SourceChanged, Assert.Throws<ArchiveWriteException>(() => ArchiveTarTransformPlan.Create(source, [], edits, [], Ct)).Reason);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Deletion_cannot_bypass_ambiguous_source_namespace_admission(bool typeConflict)
    {
        byte[] bytes = ArchiveStrictTarTests.Make(TarEntryFormat.Pax, ("a", "file"), (typeConflict ? "a/" : "a/child", ""));
        var source = await Assess(bytes);
        Assert.Equal(ArchiveWriteReason.UnsupportedFeature, Assert.Throws<ArchiveWriteException>(() =>
            ArchiveTarTransformPlan.Create(source, [], [new(ArchiveChangeKind.Delete, Key(source, 1))], [], Ct)).Reason);
    }

    [Theory]
    [InlineData("schema")]
    [InlineData("mode")]
    [InlineData("uid")]
    [InlineData("text-length")]
    [InlineData("text-utf8")]
    [InlineData("time")]
    [InlineData("presence")]
    [InlineData("trailing")]
    public async Task Unsupported_metadata_is_checked_even_on_deleted_members(string field)
    {
        var source = await Assess(ArchiveStrictTarTests.Make(TarEntryFormat.Pax, ("file", "data"))); var old = source.Manifest.Members[0];
        byte[] metadata = old.Metadata.ToArray();
        switch (field)
        {
            case "schema": metadata[0] = 2; break;
            case "mode": BinaryPrimitives.WriteInt32LittleEndian(metadata.AsSpan(2), -1); break;
            case "uid": BinaryPrimitives.WriteInt32LittleEndian(metadata.AsSpan(6), -1); break;
            case "text-length": BinaryPrimitives.WriteInt32LittleEndian(metadata.AsSpan(14), int.MaxValue); break;
            case "text-utf8": metadata[18] = 0xff; break;
            case "time": BinaryPrimitives.WriteInt64LittleEndian(metadata.AsSpan(metadata.Length - 10), long.MaxValue); break;
            case "presence": metadata[^1] = 2; break;
            case "trailing": metadata = [.. metadata, 0]; break;
        }
        var altered = new ArchiveManifestMember(old.Name, old.Type, old.Occurrence, old.Length, old.Sha256, metadata);
        var invalid = source with { Manifest = new(source.Manifest.Profile, [altered], source.Manifest.Features) };
        Assert.Equal(ArchiveWriteReason.UnsupportedFeature, Assert.Throws<ArchiveWriteException>(() =>
            ArchiveTarTransformPlan.Create(invalid, [], [new(ArchiveChangeKind.Delete, Key(source, 0))], [], Ct)).Reason);
    }

    [Fact]
    public async Task Unknown_features_profiles_and_unrepresentable_USTAR_changes_are_refused()
    {
        var source = await Assess(ArchiveStrictTarTests.Make(TarEntryFormat.Ustar, ("old", "data")));
        foreach (var invalid in new[] {
            source with { Manifest = new(source.Manifest.Profile, source.Manifest.Members, [1, 3]) },
            source with { Manifest = new(new(ArchiveWriteFormat.TarGzip), source.Manifest.Members, [1, 1]) } })
            Assert.Equal(ArchiveWriteReason.UnsupportedFeature, Assert.Throws<ArchiveWriteException>(() => ArchiveTarTransformPlan.Create(invalid, [], [], [], Ct)).Reason);
        Assert.Equal(ArchiveWriteReason.UnsupportedFeature, Assert.Throws<ArchiveWriteException>(() =>
            ArchiveTarTransformPlan.Create(source, [], [new(ArchiveChangeKind.Rename, Key(source, 0), new string('x', 101))], [], Ct)).Reason);
        Assert.Equal(ArchiveWriteReason.UnsupportedFeature, Assert.Throws<ArchiveWriteException>(() =>
            ArchiveTarTransformPlan.Create(source, [Input(1, [], DateTimeOffset.UnixEpoch.AddSeconds(-1))], [], [new("new", ArchiveMemberType.File, 1)], Ct)).Reason);
    }

    [Fact]
    public void Frozen_requests_have_only_referenced_positive_distinct_inputs_and_reserve_bounds_before_enumeration()
    {
        var inputs = new List<ArchiveTransformInput> { Input(1, []) }; var adds = new List<ArchiveTransformAddition> { new("file", ArchiveMemberType.File, 1) };
        var plan = ArchiveTarTransformPlan.Create(null, inputs, [], adds, Ct); inputs.Clear(); adds.Clear();
        Assert.Single(plan.Inputs); Assert.Single(plan.Additions); Assert.Equal("file", Assert.Single(plan.Expected.Members).Name);
        foreach (var invalid in new[] { new[] { Input(1, []), Input(1, []) }, new[] { Input(2, []) }, new[] { Input(1, []), Input(2, []) } })
            Assert.Throws<ArchiveWriteException>(() => ArchiveTarTransformPlan.Create(null, invalid, [], [new("file", ArchiveMemberType.File, 1)], Ct));
        Assert.Throws<ArchiveWriteException>(() => new ArchiveTransformInput(0, 0, Hash([]), DateTimeOffset.UnixEpoch));
        Assert.Equal(ArchiveWriteReason.BudgetExceeded, Assert.Throws<ArchiveWriteException>(() =>
            ArchiveTarTransformPlan.Create(null, new OversizedInputs(), [], [], Ct)).Reason);
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        Assert.Throws<OperationCanceledException>(() => ArchiveTarTransformPlan.Create(null, [], [], [], canceled.Token));
    }

    [Fact]
    public async Task Logical_input_budget_includes_source_members_that_will_be_deleted_and_new_captured_inputs()
    {
        var source = await Assess(ArchiveStrictTarTests.Make(TarEntryFormat.Pax, ("file", "data")));
        var members = Enumerable.Range(0, 8).Select(i => new ArchiveManifestMember("file" + i, ArchiveMemberType.File, 0,
            ArchiveWritePolicy.MaxPayloadBytes, source.SourceSha256, source.Manifest.Members[0].Metadata)).ToArray();
        var full = source with { SourceLength = ArchiveWritePolicy.MaxLogicalBytes + (1 << 20), Manifest = new(source.Manifest.Profile, members, source.Manifest.Features) };
        var edits = Enumerable.Range(0, 8).Select(i => new ArchiveTransformEdit(ArchiveChangeKind.Delete, Key(full, i))).ToArray();
        Assert.Equal(ArchiveWriteReason.BudgetExceeded, Assert.Throws<ArchiveWriteException>(() =>
            ArchiveTarTransformPlan.Create(full, [Input(1, [1])], edits, [new("added", ArchiveMemberType.File, 1)], Ct)).Reason);
    }

    [Fact]
    public async Task Reusing_a_captured_input_is_charged_for_every_reference_in_addition_to_deleted_source_payloads()
    {
        var source = await Assess(ArchiveStrictTarTests.Make(TarEntryFormat.Pax, ("file", "data")));
        var members = Enumerable.Range(0, 4).Select(i => new ArchiveManifestMember("file" + i, ArchiveMemberType.File, 0,
            ArchiveWritePolicy.MaxPayloadBytes, source.SourceSha256, source.Manifest.Members[0].Metadata)).ToArray();
        var full = source with { SourceLength = 4 * ArchiveWritePolicy.MaxPayloadBytes + (1 << 20), Manifest = new(source.Manifest.Profile, members, source.Manifest.Features) };
        var edits = Enumerable.Range(0, 4).Select(i => new ArchiveTransformEdit(ArchiveChangeKind.Delete, Key(full, i))).ToArray();
        var input = new ArchiveTransformInput(1, ArchiveWritePolicy.MaxPayloadBytes, source.SourceSha256, DateTimeOffset.UnixEpoch);
        var additions = Enumerable.Range(0, 5).Select(i => new ArchiveTransformAddition("added" + i, ArchiveMemberType.File, 1)).ToArray();
        var boundary = ArchiveTarTransformPlan.Create(full, [input], edits, additions[..4], Ct);
        Assert.Equal(4 * ArchiveWritePolicy.MaxPayloadBytes, boundary.Expected.LogicalBytes); // Exactly 64 GiB across source plus requested input reads.
        Assert.Equal(ArchiveWriteReason.BudgetExceeded, Assert.Throws<ArchiveWriteException>(() =>
            ArchiveTarTransformPlan.Create(full, [input], edits, additions, Ct)).Reason);
    }

    private sealed class OversizedInputs : IReadOnlyList<ArchiveTransformInput>
    {
        public int Count => ArchiveWritePolicy.MaxMembers + 1;
        public ArchiveTransformInput this[int index] => throw new InvalidOperationException("Not authorized to enumerate.");
        public IEnumerator<ArchiveTransformInput> GetEnumerator() => throw new InvalidOperationException("Not authorized to enumerate.");
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
