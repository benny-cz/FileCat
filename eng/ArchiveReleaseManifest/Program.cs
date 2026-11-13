// Plan §16.1: "The release manifest records application commit, profile schema, exact source/component hashes, worker
// protocol, supported RIDs, qualified filesystem routes and evidence IDs." This tool writes that manifest for one release
// candidate. Every product fact comes from the build itself (the route lists, protocol and record-schema versions are read
// from FileCat.Core, not retyped), and every file it names is hashed as it is now. Usage:
//
//   FileCat.ArchiveReleaseManifest --repo <checkout> --out <manifest.json>
//       [--payload <rid>=<published folder>]... [--package <rid>:<kind>=<file>]... [--evidence <id>=<file>]...
//       [--remote-package <rid>:<kind>=<file name>:<sha256>:<bytes>:<evidence id>]... [--allow-dirty]
//
// A remote package was built and hashed on its own machine; its entry names the evidence (a package log) that records it.
//
// A dirty checkout is refused unless --allow-dirty is given, and then the manifest says so. The tool lists and enables
// nothing: it records the build's own route lists, and refuses a listed route whose evidence it does not carry.
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using FileCat.Core.Archives;
using FileCat.Core.Edit;

static class Program
{
    private const string Schema = "filecat-archive-release-manifest/1";

    private static int Main(string[] args)
    {
        try { return Run(args); }
        catch (Exception ex) when (ex is ArgumentException or IOException or InvalidOperationException)
        { Console.Error.WriteLine("Release manifest refused: " + ex.Message); return 2; }
    }

    private static int Run(string[] args)
    {
        string? repo = null, output = null; bool allowDirty = false;
        var payloads = new List<(string Rid, string Folder)>(); var packages = new List<(string Rid, string Kind, string File)>();
        var evidence = new List<(string Id, string File)>(); var remote = new List<JsonObject>();
        for (int i = 0; i < args.Length; i++)
        {
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException(args[i] + " needs a value.");
            switch (args[i])
            {
                case "--repo": repo = Path.GetFullPath(Next()); break;
                case "--out": output = Path.GetFullPath(Next()); break;
                case "--allow-dirty": allowDirty = true; break;
                case "--payload": { var (key, value) = Pair(Next()); payloads.Add((Rid(key), Path.GetFullPath(value))); break; }
                case "--package":
                    {
                        var (key, value) = Pair(Next()); int colon = key.IndexOf(':');
                        if (colon <= 0 || colon == key.Length - 1) throw new ArgumentException("--package takes <rid>:<kind>=<file>.");
                        packages.Add((Rid(key[..colon]), key[(colon + 1)..], Path.GetFullPath(value))); break;
                    }
                case "--evidence": { var (key, value) = Pair(Next()); evidence.Add((key, Path.GetFullPath(value))); break; }
                case "--remote-package":
                    {
                        var (key, value) = Pair(Next()); int colon = key.IndexOf(':'); string[] parts = value.Split(':');
                        if (colon <= 0 || colon == key.Length - 1 || parts.Length != 4 || parts[1].Length != 64 || !parts[1].All(Uri.IsHexDigit) ||
                            !long.TryParse(parts[2], out long bytes) || bytes <= 0 || parts[0].Length == 0 || parts[3].Length == 0)
                            throw new ArgumentException("--remote-package takes <rid>:<kind>=<file name>:<sha256>:<bytes>:<evidence id>.");
                        remote.Add(new JsonObject
                        {
                            ["rid"] = Rid(key[..colon]), ["kind"] = key[(colon + 1)..], ["file"] = parts[0], ["bytes"] = bytes,
                            ["sha256"] = parts[1].ToLowerInvariant(), ["hashedOn"] = "its build machine", ["evidence"] = parts[3],
                        });
                        break;
                    }
                default: throw new ArgumentException("Unknown argument: " + args[i]);
            }
        }
        if (repo is null || output is null) throw new ArgumentException("--repo and --out are required.");
        if (File.Exists(output) || Directory.Exists(output)) throw new IOException("The manifest output must be new.");

        string commit = Git(repo, "rev-parse HEAD").Trim();
        string status = Git(repo, "status --porcelain=v1 --untracked-files=all");
        bool clean = status.Length == 0;
        if (!clean && !allowDirty) throw new InvalidOperationException("The checkout has uncommitted changes; a release manifest names a clean commit.");
        // The routes, protocol, profiles and schemas below are read from the FileCat.Core this tool was built with. They
        // speak for the release only when that build, and every payload, comes from the commit named here.
        string? toolSource = SourceCommit(typeof(ArchiveWriteProfile).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);
        if (!string.Equals(toolSource, commit, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"This tool's FileCat.Core was built from {toolSource ?? "an unnamed source"}, not {commit}. Build the tool from the checkout it describes.");
        foreach (var payload in payloads)
        {
            string core = Path.Combine(payload.Folder, "FileCat.Core.dll");
            string? built = File.Exists(core) ? SourceCommit(FileVersionInfo.GetVersionInfo(core).ProductVersion) : null;
            if (!string.Equals(built, commit, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"The {payload.Rid} payload was built from {built ?? "an unnamed source"}, not {commit}.");
        }

        var manifest = new JsonObject
        {
            ["schema"] = Schema,
            ["application"] = new JsonObject
            {
                ["commit"] = commit,
                ["committed"] = Git(repo, "show -s --format=%cI HEAD").Trim(),
                ["cleanCheckout"] = clean,
                ["coreAssembly"] = typeof(ArchiveWriteProfile).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
            },
            ["workerProtocol"] = new JsonObject
            {
                ["versions"] = new JsonArray(ArchiveWorkerProtocol.Version, ArchiveWorkerProtocol.SecretVersion),
                ["headerBytes"] = ArchiveWorkerProtocol.HeaderBytes,
                ["roles"] = new JsonArray("archive-assess-v2", "archive-transform-v2", "archive-verify-v2"),
            },
            ["profiles"] = Profiles(),
            ["recordSchemas"] = RecordSchemas(),
            ["routes"] = new JsonObject
            {
                ["archiveWrite"] = new JsonArray(ArchiveWriteRoutes.Qualified.Select(route => (JsonNode)new JsonObject
                {
                    ["format"] = route.Format.ToString(), ["rid"] = route.RuntimeIdentifier, ["fileSystem"] = route.FileSystem, ["evidence"] = route.EvidenceId,
                }).ToArray()),
                ["nativeEditStorage"] = new JsonArray(NativeEditStorage.Qualified.Select(route => (JsonNode)new JsonObject
                {
                    ["rid"] = route.RuntimeIdentifier, ["fileSystem"] = route.FileSystem, ["evidence"] = route.EvidenceId,
                }).ToArray()),
            },
            ["payloads"] = new JsonArray(payloads.Select(payload => (JsonNode)Payload(payload.Rid, payload.Folder)).ToArray()),
            ["packages"] = new JsonArray(packages.Select(package => (JsonNode)Described(package.File, new JsonObject { ["rid"] = package.Rid, ["kind"] = package.Kind }))
                .Concat(remote).ToArray()),
            ["evidence"] = new JsonArray(evidence.Select(item => (JsonNode)Described(item.File, new JsonObject { ["id"] = item.Id })).ToArray()),
        };
        if (!clean) manifest["application"]!["uncommittedStatus"] = status.Trim();
        foreach (var package in remote)
            if (!evidence.Any(item => item.Id == (string?)package["evidence"]))
                throw new ArgumentException("A remote package names evidence this manifest does not carry: " + package["evidence"]);
        // A listed route stands on its evidence: the manifest carries it under the route's ID, or under IDs below it
        // ("AWQ-win-x64-NTFS/full" for "AWQ-win-x64-NTFS"). An entry counts for the longest listed ID it is under, so a
        // capability listed below a route ("AWQ-win-x64-NTFS/edit") never satisfies the route itself, nor the route's
        // evidence that capability: each listed thing stands on evidence of its own.
        string[] listedIds = ArchiveWriteRoutes.Qualified.Select(route => route.EvidenceId)
            .Concat(NativeEditStorage.Qualified.Select(route => route.EvidenceId)).Distinct(StringComparer.Ordinal).ToArray();
        string? Credited(string entry) => listedIds
            .Where(id => entry == id || entry.StartsWith(id + "/", StringComparison.Ordinal))
            .OrderByDescending(id => id.Length, Comparer<int>.Default).FirstOrDefault();
        foreach (string id in listedIds)
            if (!evidence.Any(item => Credited(item.Id) == id))
                throw new ArgumentException("A listed route names evidence this manifest does not carry: " + id);
        File.WriteAllText(output, manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
        Console.WriteLine($"Wrote {output}: commit {commit}, {payloads.Count} payloads, {packages.Count} packages, {evidence.Count} evidence files, " +
            $"{ArchiveWriteRoutes.Qualified.Count} archive routes and {NativeEditStorage.Qualified.Count} edit-storage routes listed.");
        return 0;
    }

    // Each format's profile as the build constructs it, with its encoded length; the dialog's options vary only the values.
    private static JsonArray Profiles()
    {
        var result = new JsonArray();
        foreach (var format in Enum.GetValues<ArchiveWriteFormat>())
        {
            ArchiveWriteProfile profile = format switch
            {
                ArchiveWriteFormat.SevenZip => ArchiveWriteProfile.ForSevenZip(),
                ArchiveWriteFormat.Rar5 => ArchiveWriteProfile.ForRar5(),
                _ => new(format),
            };
            result.Add(new JsonObject
            {
                ["format"] = format.ToString(), ["profileVersion"] = profile.Version,
                ["encodedBytes"] = ArchiveWriteProfileEncoding.Length(profile),
            });
        }
        return result;
    }

    // Every persisted record type's schema version: journal facts, recovery and cleanup records, edit sessions.
    private static JsonArray RecordSchemas()
    {
        var result = new JsonArray();
        foreach (var type in typeof(ArchiveWriteProfile).Assembly.GetTypes().OrderBy(type => type.FullName, StringComparer.Ordinal))
        {
            var field = type.GetField("SchemaVersion", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (field is { IsLiteral: true } && field.FieldType == typeof(int))
                result.Add(new JsonObject { ["type"] = type.FullName, ["version"] = (int)field.GetRawConstantValue()! });
        }
        result.Add(new JsonObject { ["type"] = typeof(EditSessionRecord).FullName + " (current)", ["version"] = EditSessionRecord.CurrentVersion });
        result.Add(new JsonObject { ["type"] = typeof(EditSessionRecord).FullName + " (native storage)", ["version"] = EditSessionRecord.NativeStorageVersion });
        return result;
    }

    // Every file of a published payload, by relative path, so a package can be checked against the folder it came from.
    private static JsonObject Payload(string rid, string folder)
    {
        if (!Directory.Exists(folder)) throw new IOException("A payload folder is missing: " + folder);
        var files = new JsonArray(); long total = 0;
        foreach (string path in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).OrderBy(path => path, StringComparer.Ordinal))
        {
            var info = new FileInfo(path);
            if (info.Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new IOException("A payload holds a link: " + path);
            total += info.Length;
            files.Add(new JsonObject
            {
                ["path"] = Path.GetRelativePath(folder, path).Replace('\\', '/'), ["bytes"] = info.Length, ["sha256"] = Hash(path),
            });
        }
        return new JsonObject { ["rid"] = rid, ["fileCount"] = files.Count, ["bytes"] = total, ["files"] = files };
    }

    private static JsonObject Described(string file, JsonObject facts)
    {
        var info = new FileInfo(file);
        if (!info.Exists || info.Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new IOException("A named file is missing or linked: " + file);
        facts["file"] = info.Name; facts["bytes"] = info.Length; facts["sha256"] = Hash(file);
        return facts;
    }

    private static string Hash(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, FileOptions.SequentialScan);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static (string Key, string Value) Pair(string text)
    {
        int equals = text.IndexOf('=');
        if (equals <= 0 || equals == text.Length - 1) throw new ArgumentException("Expected key=value: " + text);
        return (text[..equals], text[(equals + 1)..]);
    }

    private static string Rid(string rid) => rid is "win-x64" or "win-arm64" or "linux-x64" or "linux-arm64" or "osx-arm64"
        ? rid : throw new ArgumentException("Unknown runtime identifier: " + rid);

    // The commit an assembly's informational version names ("1.0.0+<40 hex>"), or null.
    private static string? SourceCommit(string? informational)
    {
        int plus = informational?.LastIndexOf('+') ?? -1;
        string? commit = plus < 0 ? null : informational![(plus + 1)..];
        return commit is { Length: 40 } && commit.All(Uri.IsHexDigit) ? commit.ToLowerInvariant() : null;
    }

    private static string Git(string repo, string arguments)
    {
        var start = new ProcessStartInfo("git") { WorkingDirectory = repo, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (string argument in arguments.Split(' ')) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("git did not start.");
        // Both streams drain at once: git blocked on a full error pipe would otherwise never finish its output.
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(60_000))
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            throw new InvalidOperationException("git did not finish.");
        }
        string text = output.GetAwaiter().GetResult(); _ = error.GetAwaiter().GetResult();
        if (process.ExitCode != 0) throw new InvalidOperationException("git " + arguments + " failed.");
        return text;
    }
}
