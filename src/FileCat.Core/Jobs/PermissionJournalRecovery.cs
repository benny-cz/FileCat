using System.Text.Json.Nodes;
using FileCat.Core.Analysis;
using FileCat.Core.Permissions;

namespace FileCat.Core.Jobs;

public sealed record InterruptedPermissionCall(int Step, int Target, string IdentityDigest, PermissionSections Section, PermissionVerdict? Outcome,
    string Before, string Expected, string? Observed, int? NativeError);
public sealed record InterruptedPermissionReview(IReadOnlyList<InterruptedPermissionCall> Calls,
    IReadOnlyDictionary<int, PermissionVerdict> ObjectVerdicts, bool Damaged)
{
    public PermissionScope? Scope { get; init; } = PermissionScope.Objects;
    public bool ServerPath { get; init; }
    internal PermissionTreeSummary? Tree { get; init; }
    internal string? TreeTemplate { get; init; }
}

internal static class PermissionJournalRecovery
{
    internal static IReadOnlyList<InterruptedJob> Scan(AnalysisPrivateDirectory directory, string path, CancellationToken cancellation,
        int keepFinished = 200, TimeSpan? maximumAge = null)
    {
        var pending = new List<InterruptedJob>();
        DateTime cutoff = DateTime.UtcNow - (maximumAge ?? TimeSpan.FromDays(30));
        long budget = 64 * 1024 * 1024;
        int inspected = 0, finished = 0;
        foreach (string name in directory.Entries(cancellation).Where(n => PermissionJournalNames.TryParse(n, out _, out _))
            .OrderDescending(StringComparer.Ordinal))
        {
            cancellation.ThrowIfCancellationRequested();
            if (pending.Count == 200 || ++inspected > 512 || budget <= 0) break;
            try
            {
                string child = Path.Combine(path, name);
                InterruptedJob? receipt;
                using (var file = directory.OpenPermissionJournal(name))
                {
                    if (file.Stream.Length > budget) break;
                    budget -= file.Stream.Length;
                    receipt = Read(file.Stream, child);
                    file.Verify();
                    directory.Verify();
                }
                if (receipt is not null) pending.Add(receipt);
                else if (++finished > keepFinished || PermissionJournalNames.TryParse(name, out DateTime created, out _) && created < cutoff)
                {
                    // Reopen with exclusive native maintenance ownership. Only a still-complete journal can be pruned.
                    using var file = directory.OpenPermissionJournalMaintenance(name);
                    if (file.Stream.Length > budget) break;
                    budget -= file.Stream.Length;
                    if (Read(file.Stream, child) is null) file.RemoveOwned();
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            { /* A retained writer, changed name or non-private file cannot supply recovery evidence. */ }
        }
        return pending;
    }

    internal static InterruptedJob? Read(Stream stream, string path)
    {
        if (!PermissionJournalNames.TryParse(Path.GetFileName(path), out DateTime created, out var request)) return null;
        var sources = new List<string>();
        var calls = new List<InterruptedPermissionCall>();
        var objects = new Dictionary<int, PermissionVerdict>();
        var expectedId = request.ToString("N");
        int count = 0;
        PermissionScope? scope = null;
        PermissionTreeSummary? tree = null;
        string? treeTemplate = null;
        bool damaged = false, header = false, ended = false, serverPath = false;
        try
        {
            foreach (var record in JobJournal.ReadPermissionRecords(stream, () => damaged = true))
            {
                if (ended) { damaged = true; break; }
                if (!header)
                {
                    if (record.Type != "begin" || record.Get("kind") != nameof(JobKind.Permissions) || record.Get("id") != expectedId ||
                        Number(record.Node, "version") != JobJournal.PermissionJournalVersion ||
                        Number(record.Node, "sourceCount") is not (>= 1 and <= ReviewedPermissionOperation.TargetLimit) ||
                        record.Node["sources"] is not JsonArray { Count: <= 64 } listed)
                    { damaged = true; break; }
                    count = Number(record.Node, "sourceCount")!.Value;
                    scope = record.Node.TryGetPropertyValue("scope", out var scopeValue)
                        ? ReadScope(scopeValue) : PermissionScope.Objects;
                    if (scope is null || scope == PermissionScope.NativePropagation && count != 1)
                    { damaged = true; break; }
                    if (record.Node.TryGetPropertyValue("serverPath", out var pathValue))
                    {
                        serverPath = true;
                        if (pathValue is not JsonValue pathFlag || !pathFlag.TryGetValue<bool>(out bool isServer) || !isServer ||
                            scope != PermissionScope.Objects || listed.Count != 0)
                        { damaged = true; break; }
                    }
                    if (scope is PermissionScope.ReviewedTree or PermissionScope.ResetTree)
                    {
                        tree = ReadTree(record.Node["tree"], count);
                        if (tree is null) { damaged = true; break; }
                    }
                    if (scope == PermissionScope.ResetTree || record.Node.ContainsKey("template") || record.Node.ContainsKey("templateRevision"))
                    {
                        treeTemplate = record.Get("template");
                        if (scope is not (PermissionScope.ReviewedTree or PermissionScope.ResetTree) || tree is not { Roots: 1 } ||
                            treeTemplate is not { Length: > 0 and <= ReviewedPermissionOperation.LocatorLimit } ||
                            treeTemplate.Contains('\0') || !Digest(record.Get("templateRevision")))
                        { damaged = true; break; }
                    }
                    header = true;
                    foreach (var entry in listed)
                    {
                        if (entry is not JsonValue value || !value.TryGetValue<string>(out var locator) ||
                            locator.Length is 0 or > ReviewedPermissionOperation.LocatorLimit || locator.Contains('\0') || sources.Count == count)
                        { damaged = true; break; }
                        sources.Add(locator);
                    }
                    continue;
                }
                if (record.Type == "end") { ended = true; continue; }
                if (record.Get("id") != expectedId || Number(record.Node, "v") != JobJournal.PermissionJournalVersion)
                { damaged = true; break; }
                switch (record.Type)
                {
                    case "permission-intent":
                        if (calls.Count >= count * ReviewedPermissionOperation.ComponentLimit || record.Step != calls.Count + 1 ||
                            calls.Count > 0 && calls[^1].Outcome is null || Number(record.Node, "target") is not int target ||
                            target < 0 || target >= count || objects.ContainsKey(target) || Number(record.Node, "section") is not int section ||
                            section <= 0 || (section & (section - 1)) != 0 || section > (int)PermissionSections.Times ||
                            scope == PermissionScope.NativePropagation && (calls.Count != 0 || section != (int)PermissionSections.Access) ||
                            serverPath && section is not ((int)PermissionSections.Mode or (int)PermissionSections.Group) ||
                            !Digest(record.Get("before")) || !Digest(record.Get("expected")) || !Digest(record.Get("identity")))
                        { damaged = true; break; }
                        calls.Add(new(record.Step, target, record.Get("identity")!, (PermissionSections)section, null,
                            record.Get("before")!, record.Get("expected")!, null, null));
                        break;
                    case "permission-outcome":
                        string? observed = record.Get("observed");
                        if (calls.Count == 0 || record.Step != calls.Count || calls[^1].Outcome is not null ||
                            ReadVerdict(record.Get("verdict")) is not { } outcome || observed is not null && !Digest(observed))
                        { damaged = true; break; }
                        calls[^1] = calls[^1] with { Outcome = outcome, Observed = observed, NativeError = Number(record.Node, "error") };
                        break;
                    case "permission-object":
                        if (Number(record.Node, "target") is not int index || index < 0 || index >= count ||
                            calls.Count > 0 && calls[^1].Outcome is null || ReadVerdict(record.Get("verdict")) is not { } final ||
                            !Digest(record.Get("expected")) || !objects.TryAdd(index, final)) damaged = true;
                        break;
                    default: damaged = true; break;
                }
                if (damaged) break;
            }
        }
        catch (IOException) { damaged = true; }
        damaged |= !header;
        if (ended && !damaged && !calls.Any(call => call.Outcome is null or PermissionVerdict.Uncertain) &&
            !objects.Values.Any(verdict => verdict is PermissionVerdict.Partial or PermissionVerdict.Uncertain)) return null;
        return new InterruptedJob(path, nameof(JobKind.Permissions), "Permissions and ownership", created,
            sources.AsReadOnly(), null, [], [], objects.Count, count)
        {
            PermissionReview = new(calls.AsReadOnly(), new System.Collections.ObjectModel.ReadOnlyDictionary<int, PermissionVerdict>(objects), damaged)
                { Scope = scope, Tree = tree, TreeTemplate = treeTemplate, ServerPath = serverPath }
        };
    }

    private static PermissionTreeSummary? ReadTree(JsonNode? value, int count)
    {
        if (value is not JsonObject node || Number(node, "roots") is not int roots || roots < 1 || roots > count) return null;
        int? inaccessible = Number(node, "inaccessible"), excluded = Number(node, "excluded"), aliases = Number(node, "aliases");
        int? protectedEntries = node.ContainsKey("protected") ? Number(node, "protected") : 0;
        if (inaccessible is not (>= 0 and <= PermissionTreeManifest.EntryLimit) ||
            excluded is not (>= 0 and <= PermissionTreeManifest.EntryLimit) || aliases is not (>= 0 and <= PermissionTreeManifest.EntryLimit) ||
            protectedEntries is not (>= 0 and <= PermissionTreeManifest.EntryLimit)) return null;
        return new(roots, inaccessible.Value, excluded.Value, aliases.Value, protectedEntries.Value);
    }

    private static int? Number(JsonObject node, string key) => node[key] is JsonValue value && value.TryGetValue<int>(out int number) ? number : null;
    private static PermissionScope? ReadScope(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out string? scope)
        ? scope switch
        {
            nameof(PermissionScope.Objects) => PermissionScope.Objects,
            nameof(PermissionScope.DefaultsForNewChildren) => PermissionScope.DefaultsForNewChildren,
            nameof(PermissionScope.NativePropagation) => PermissionScope.NativePropagation,
            nameof(PermissionScope.ReviewedTree) => PermissionScope.ReviewedTree,
            nameof(PermissionScope.ResetTree) => PermissionScope.ResetTree,
            _ => null
        }
        : null;
    private static bool Digest(string? value) => value is { Length: 64 } && value.All(char.IsAsciiHexDigitUpper);
    private static PermissionVerdict? ReadVerdict(string? value) => Enum.TryParse<PermissionVerdict>(value, out var verdict) && value == verdict.ToString() ? verdict : null;
}
