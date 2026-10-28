using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

if (args.Length < 8)
{
    Console.Error.WriteLine("usage: ArtifactSbom <payload-directory> <notice-index.json> <version> <rid> <kind> <source-commit> <created-utc> <new-output.spdx.json> [logical-path=extra-file ...]");
    return 2;
}

try
{
    string payload = Path.GetFullPath(args[0]);
    string noticeIndexPath = Path.GetFullPath(args[1]);
    string version = args[2];
    string rid = args[3];
    string kind = args[4];
    string sourceCommit = args[5];
    string output = Path.GetFullPath(args[7]);
    var supportedRids = new HashSet<string>(["win-x64", "win-arm64", "linux-x64", "linux-arm64", "osx-arm64"], StringComparer.Ordinal);
    var supportedKinds = new HashSet<string>(["setup-payload", "portable", "fdd", "tar", "deb", "appimage", "macos-app"], StringComparer.Ordinal);
    if (!Directory.Exists(payload) || (new DirectoryInfo(payload).Attributes & FileAttributes.ReparsePoint) != 0)
        throw new InvalidDataException("Payload must be an existing non-linked directory.");
    if (!File.Exists(noticeIndexPath) || (File.GetAttributes(noticeIndexPath) & FileAttributes.ReparsePoint) != 0)
        throw new InvalidDataException("Notice index must be an existing non-linked file.");
    if (!Regex.IsMatch(version, @"^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$", RegexOptions.CultureInvariant)
        || !supportedRids.Contains(rid) || !supportedKinds.Contains(kind)
        || !Regex.IsMatch(sourceCommit, "^[0-9a-f]{40}$", RegexOptions.CultureInvariant))
        throw new InvalidDataException("Version, RID, kind or source commit is invalid.");
    if (!DateTimeOffset.TryParse(args[6], out var created)) throw new InvalidDataException("Creation time is invalid.");
    created = created.ToUniversalTime();
    if (File.Exists(output) || Directory.Exists(output)) throw new InvalidDataException("SBOM output must be new.");
    string payloadPrefix = payload.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
    if (output.Equals(payload, StringComparison.OrdinalIgnoreCase) || output.StartsWith(payloadPrefix, StringComparison.OrdinalIgnoreCase))
        throw new InvalidDataException("SBOM output cannot be inside its payload.");

    using var snapshotDocument = JsonDocument.Parse(File.ReadAllBytes(noticeIndexPath));
    JsonElement snapshot = snapshotDocument.RootElement;
    if (snapshot.GetProperty("SchemaVersion").GetInt32() != 1) throw new InvalidDataException("Unsupported notice snapshot schema.");
    var packages = new Dictionary<string, PackageInfo>(StringComparer.OrdinalIgnoreCase);
    foreach (JsonElement item in snapshot.GetProperty("Packages").EnumerateArray())
    {
        string id = Required(item, "ID");
        string packageVersion = Required(item, "Version");
        string contentHash = Required(item, "NuGetContentHash");
        string archiveHash = Required(item, "PackageArchiveSHA256");
        JsonElement license = item.GetProperty("NuGetLicenseDeclaration");
        string licenseText = Required(license, "Text");
        JsonElement repository = item.GetProperty("NuGetRepositoryDeclaration");
        JsonElement repositoryAttributes = default;
        bool hasRepositoryAttributes = repository.ValueKind == JsonValueKind.Object
            && repository.TryGetProperty("Attributes", out repositoryAttributes)
            && repositoryAttributes.ValueKind == JsonValueKind.Object;
        string? repositoryUrl = hasRepositoryAttributes && repositoryAttributes.TryGetProperty("url", out var url) ? url.GetString() : null;
        string? repositoryCommit = hasRepositoryAttributes && repositoryAttributes.TryGetProperty("commit", out var commit) ? commit.GetString() : null;
        string[] notices = item.GetProperty("RetainedNoticePaths").EnumerateArray().Select(value => value.GetString() ?? throw new InvalidDataException("Null notice path.")).ToArray();
        if (!packages.TryAdd(id, new(id, packageVersion, contentHash, archiveHash, licenseText, repositoryUrl, repositoryCommit, notices)))
            throw new InvalidDataException("Duplicate package in notice snapshot.");
    }
    var runtimePacks = new Dictionary<string, RuntimeInfo>(StringComparer.OrdinalIgnoreCase);
    foreach (JsonElement item in snapshot.GetProperty("RuntimePacks").EnumerateArray())
    {
        var runtime = new RuntimeInfo(Required(item, "ID"), Required(item, "Version"), Required(item, "RID"),
            Required(item, "NuGetContentHashFromMetadata"), Required(item, "PackageArchiveSHA256"),
            item.GetProperty("RetainedNoticePaths").EnumerateArray().Select(value => value.GetString() ?? throw new InvalidDataException("Null runtime notice path.")).ToArray());
        if (!runtimePacks.TryAdd(runtime.ID + "/" + runtime.Version, runtime))
            throw new InvalidDataException("Duplicate runtime pack in notice snapshot.");
    }

    var seenPackages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var seenRuntimePacks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var references = new Dictionary<string, ReferenceInfo>(StringComparer.OrdinalIgnoreCase);
    var projects = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    string[] depsFiles = FindFiles(new DirectoryInfo(payload), ".deps.json").Order(StringComparer.Ordinal).ToArray();
    if (depsFiles.Length == 0) throw new InvalidDataException("Payload has no dependency manifest.");
    foreach (string depsPath in depsFiles)
    {
        using var depsDocument = JsonDocument.Parse(File.ReadAllBytes(depsPath));
        foreach (JsonProperty library in depsDocument.RootElement.GetProperty("libraries").EnumerateObject())
        {
            int separator = library.Name.LastIndexOf('/');
            if (separator <= 0) throw new InvalidDataException("Malformed dependency identity.");
            string id = library.Name[..separator];
            string libraryVersion = library.Name[(separator + 1)..];
            string type = Required(library.Value, "type");
            if (id.StartsWith("runtimepack.", StringComparison.OrdinalIgnoreCase))
            {
                string runtimeIdentity = id["runtimepack.".Length..] + "/" + libraryVersion;
                if (type != "runtimepack" || !runtimePacks.TryGetValue(runtimeIdentity, out RuntimeInfo? runtime)
                    || Required(library.Value, "sha512") != "")
                    throw new InvalidDataException("Published runtime pack differs from the reviewed snapshot.");
                if (seenRuntimePacks.Add(runtimeIdentity)) ValidateRuntimeArchive(runtime);
            }
            else if (type == "package" || packages.ContainsKey(id))
            {
                if (type != "package" || !packages.TryGetValue(id, out PackageInfo? package)
                    || package.Version != libraryVersion || Required(library.Value, "sha512") != "sha512-" + package.ContentHash)
                    throw new InvalidDataException("Published package differs from the reviewed snapshot.");
                seenPackages.Add(package.ID);
            }
            else if (type == "project")
            {
                if (projects.TryGetValue(id, out string? existing) && existing != libraryVersion)
                    throw new InvalidDataException("Conflicting project version in payload.");
                projects[id] = libraryVersion;
            }
            else if (type == "reference")
            {
                PackageInfo[] parents = packages.Values
                    .Where(package => libraryVersion == package.Version
                        && id.StartsWith(package.ID + ".", StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                if (Required(library.Value, "sha512") != "" || parents.Length != 1)
                    throw new InvalidDataException("Published reference component has no unique reviewed package origin.");
                var reference = new ReferenceInfo(id, libraryVersion, parents[0].ID);
                if (references.TryGetValue(id, out ReferenceInfo? existingReference) && existingReference != reference)
                    throw new InvalidDataException("Conflicting reference component in payload.");
                references[id] = reference;
            }
            else throw new InvalidDataException("Unsupported dependency kind in payload.");
        }
    }
    if (references.Values.Any(reference => !seenPackages.Contains(reference.ParentPackage)))
        throw new InvalidDataException("Published reference component omits its reviewed parent package.");
    bool selfContained = kind != "fdd";
    if (selfContained)
    {
        if (seenRuntimePacks.Count != 1) throw new InvalidDataException("Self-contained payload must contain exactly one reviewed runtime pack.");
        RuntimeInfo selected = runtimePacks[seenRuntimePacks.Single()];
        if (selected.RID != rid) throw new InvalidDataException("Runtime pack RID differs from the artifact RID.");
    }
    else if (seenRuntimePacks.Count != 0) throw new InvalidDataException("Framework-dependent payload unexpectedly contains a runtime pack.");

    var files = new List<FileRecord>();
    ReadTree(new DirectoryInfo(payload), "", files);
    var logicalPaths = new HashSet<string>(files.Select(file => file.Path), StringComparer.Ordinal);
    for (int index = 8; index < args.Length; index++)
    {
        int equals = args[index].IndexOf('=');
        if (equals <= 0) throw new InvalidDataException("Extra file must use logical-path=source-file.");
        string logical = NormalizeLogical(args[index][..equals]);
        string source = Path.GetFullPath(args[index][(equals + 1)..]);
        var item = new FileInfo(source);
        if (!item.Exists || (item.Attributes & FileAttributes.ReparsePoint) != 0 || !logicalPaths.Add(logical))
            throw new InvalidDataException("Extra file is missing, linked or duplicates a payload path.");
        files.Add(ReadFile(item, logical));
    }
    files.Sort((left, right) => string.CompareOrdinal(left.Path, right.Path));
    string verification = Hex(SHA1.HashData(Encoding.ASCII.GetBytes(string.Concat(files.Select(file => file.SHA1).Order(StringComparer.Ordinal)))));
    string treeIdentity = TreeIdentity(files);
    string rootId = "SPDXRef-Package-FileCat";

    var spdxPackages = new JsonArray
    {
        new JsonObject
        {
            ["name"] = "FileCat",
            ["SPDXID"] = rootId,
            ["versionInfo"] = version,
            ["supplier"] = "NOASSERTION",
            ["downloadLocation"] = "NOASSERTION",
            ["filesAnalyzed"] = true,
            ["packageVerificationCode"] = new JsonObject { ["packageVerificationCodeValue"] = verification },
            ["licenseConcluded"] = "NOASSERTION",
            ["licenseDeclared"] = "MIT",
            ["copyrightText"] = "Copyright (c) 2026 Benny and FileCat contributors",
            ["sourceInfo"] = $"Source commit {sourceCommit}",
            ["comment"] = $"Exact {kind} payload tree for {rid}; tree SHA-256 {treeIdentity}. Preliminary composition evidence, not signing or candidate qualification.",
            ["hasFiles"] = new JsonArray(files.Select(file => (JsonNode?)file.SpdxId).ToArray()),
            ["externalRefs"] = new JsonArray(new JsonObject
            {
                ["referenceCategory"] = "OTHER",
                ["referenceType"] = "vcs",
                ["referenceLocator"] = $"https://github.com/benny-cz/FileCat@{sourceCommit}"
            })
        }
    };
    var dependencyIds = new List<string>();
    foreach (string packageId in seenPackages.Order(StringComparer.OrdinalIgnoreCase))
    {
        PackageInfo package = packages[packageId];
        string spdxId = SpdxId("NuGet", package.ID + "/" + package.Version);
        dependencyIds.Add(spdxId);
        var node = new JsonObject
        {
            ["name"] = package.ID,
            ["SPDXID"] = spdxId,
            ["versionInfo"] = package.Version,
            ["downloadLocation"] = $"https://api.nuget.org/v3-flatcontainer/{package.ID.ToLowerInvariant()}/{package.Version.ToLowerInvariant()}/{package.ID.ToLowerInvariant()}.{package.Version.ToLowerInvariant()}.nupkg",
            ["filesAnalyzed"] = false,
            ["checksums"] = new JsonArray(new JsonObject { ["algorithm"] = "SHA256", ["checksumValue"] = package.ArchiveSHA256 }),
            ["licenseConcluded"] = "NOASSERTION",
            ["licenseDeclared"] = IsSpdxLicense(package.License) ? package.License : "NOASSERTION",
            ["licenseComments"] = $"NuGet declaration: {package.License}; retained texts: {string.Join(", ", package.NoticePaths)}",
            ["copyrightText"] = "NOASSERTION",
            ["externalRefs"] = new JsonArray(new JsonObject
            {
                ["referenceCategory"] = "PACKAGE-MANAGER",
                ["referenceType"] = "purl",
                ["referenceLocator"] = $"pkg:nuget/{Uri.EscapeDataString(package.ID)}@{Uri.EscapeDataString(package.Version)}"
            })
        };
        string[] aliases = references.Values.Where(reference => reference.ParentPackage.Equals(package.ID, StringComparison.OrdinalIgnoreCase))
            .Select(reference => reference.ID + "/" + reference.Version).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        if (aliases.Length != 0)
            node["comment"] = "Published reference components from this reviewed package: " + string.Join(", ", aliases);
        if (!string.IsNullOrWhiteSpace(package.RepositoryUrl))
        {
            ((JsonArray)node["externalRefs"]!).Add(new JsonObject
            {
                ["referenceCategory"] = "OTHER", ["referenceType"] = "vcs",
                ["referenceLocator"] = package.RepositoryUrl + (string.IsNullOrWhiteSpace(package.RepositoryCommit) ? "" : "@" + package.RepositoryCommit)
            });
        }
        spdxPackages.Add(node);
    }
    foreach (string identity in seenRuntimePacks.Order(StringComparer.OrdinalIgnoreCase))
    {
        RuntimeInfo runtime = runtimePacks[identity];
        string spdxId = SpdxId("Runtime", identity);
        dependencyIds.Add(spdxId);
        spdxPackages.Add(new JsonObject
        {
            ["name"] = runtime.ID,
            ["SPDXID"] = spdxId,
            ["versionInfo"] = runtime.Version,
            ["downloadLocation"] = $"https://api.nuget.org/v3-flatcontainer/{runtime.ID.ToLowerInvariant()}/{runtime.Version}/{runtime.ID.ToLowerInvariant()}.{runtime.Version}.nupkg",
            ["filesAnalyzed"] = false,
            ["checksums"] = new JsonArray(new JsonObject { ["algorithm"] = "SHA256", ["checksumValue"] = runtime.ArchiveSHA256 }),
            ["licenseConcluded"] = "NOASSERTION",
            ["licenseDeclared"] = "MIT",
            ["licenseComments"] = "Retained texts: " + string.Join(", ", runtime.NoticePaths),
            ["copyrightText"] = "NOASSERTION",
            ["externalRefs"] = new JsonArray(new JsonObject
            {
                ["referenceCategory"] = "PACKAGE-MANAGER", ["referenceType"] = "purl",
                ["referenceLocator"] = $"pkg:nuget/{Uri.EscapeDataString(runtime.ID)}@{runtime.Version}"
            })
        });
    }
    foreach ((string name, string projectVersion) in projects.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
    {
        if (name.Equals("FileCat", StringComparison.OrdinalIgnoreCase)) continue;
        string spdxId = SpdxId("Project", name + "/" + projectVersion);
        dependencyIds.Add(spdxId);
        spdxPackages.Add(new JsonObject
        {
            ["name"] = name, ["SPDXID"] = spdxId, ["versionInfo"] = projectVersion,
            ["downloadLocation"] = "NOASSERTION", ["filesAnalyzed"] = false,
            ["licenseConcluded"] = "NOASSERTION", ["licenseDeclared"] = "MIT",
            ["copyrightText"] = "Copyright (c) 2026 Benny and FileCat contributors",
            ["sourceInfo"] = $"Built from FileCat source commit {sourceCommit}"
        });
    }
    var relationships = new JsonArray(new JsonObject
    {
        ["spdxElementId"] = "SPDXRef-DOCUMENT", ["relationshipType"] = "DESCRIBES", ["relatedSpdxElement"] = rootId
    });
    foreach (string dependencyId in dependencyIds.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        relationships.Add(new JsonObject { ["spdxElementId"] = rootId, ["relationshipType"] = "DEPENDS_ON", ["relatedSpdxElement"] = dependencyId });

    string generatorPath = Assembly.GetExecutingAssembly().Location;
    if (string.IsNullOrWhiteSpace(generatorPath)) throw new InvalidOperationException("Missing generator assembly path.");
    string generatorHash = Hex(SHA256.HashData(File.ReadAllBytes(generatorPath)));
    var document = new JsonObject
    {
        ["spdxVersion"] = "SPDX-2.3",
        ["dataLicense"] = "CC0-1.0",
        ["SPDXID"] = "SPDXRef-DOCUMENT",
        ["name"] = $"FileCat-{version}-{rid}-{kind}",
        ["documentNamespace"] = $"https://github.com/benny-cz/FileCat/spdx/{sourceCommit}/{Uri.EscapeDataString(version)}/{rid}/{kind}/{treeIdentity}",
        ["creationInfo"] = new JsonObject
        {
            ["created"] = created.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"),
            ["creators"] = new JsonArray("Tool: FileCat.ArtifactSbom-1.0")
        },
        ["documentDescribes"] = new JsonArray(rootId),
        ["packages"] = spdxPackages,
        ["files"] = new JsonArray(files.Select(file => (JsonNode?)new JsonObject
        {
            ["fileName"] = "./" + file.Path,
            ["SPDXID"] = file.SpdxId,
            ["checksums"] = new JsonArray(
                new JsonObject { ["algorithm"] = "SHA1", ["checksumValue"] = file.SHA1 },
                new JsonObject { ["algorithm"] = "SHA256", ["checksumValue"] = file.SHA256 }),
            ["licenseConcluded"] = "NOASSERTION",
            ["licenseInfoInFiles"] = new JsonArray("NOASSERTION"),
            ["copyrightText"] = "NOASSERTION",
            ["comment"] = file.Description
        }).ToArray()),
        ["relationships"] = relationships,
        ["annotations"] = new JsonArray(new JsonObject
        {
            ["annotator"] = "Tool: FileCat.ArtifactSbom-1.0",
            ["annotationDate"] = created.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"),
            ["annotationType"] = "OTHER",
            ["comment"] = $"Generator executable SHA-256 {generatorHash}; notice index SHA-256 {Hex(SHA256.HashData(File.ReadAllBytes(noticeIndexPath)))}; exact files and reviewed dependency identities included. Native origin/legal completeness and candidate qualification remain false until their separate gates close."
        })
    };
    string? outputDirectory = Path.GetDirectoryName(output);
    if (string.IsNullOrWhiteSpace(outputDirectory)) throw new InvalidDataException("SBOM output has no parent directory.");
    Directory.CreateDirectory(outputDirectory);
    string temporary = Path.Combine(outputDirectory, ".artifact-sbom-" + Guid.NewGuid().ToString("N") + ".tmp");
    try
    {
        File.WriteAllText(temporary, document.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine, new UTF8Encoding(false));
        File.Move(temporary, output, false);
    }
    finally { if (File.Exists(temporary)) File.Delete(temporary); }
    Console.WriteLine($"Wrote SPDX 2.3 SBOM: {files.Count} exact files, {seenPackages.Count} reviewed NuGet packages, {seenRuntimePacks.Count} runtime packs, tree {treeIdentity}.");
    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine(error.Message);
    return 1;
}

static string Required(JsonElement value, string name) => value.GetProperty(name).GetString()
    ?? throw new InvalidDataException($"Missing string field: {name}");
static string Hex(byte[] value) => Convert.ToHexString(value).ToLowerInvariant();
static string SpdxId(string kind, string identity) => "SPDXRef-" + kind + "-" + Hex(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..20];
static bool IsSpdxLicense(string value) => value is "MIT" or "Apache-2.0" or "BSD-2-Clause" or "BSD-3-Clause" or "LGPL-2.1-only" or "LGPL-2.1-or-later";
static string NormalizeLogical(string value)
{
    string logical = value.Replace('\\', '/').TrimStart('/');
    if (string.IsNullOrWhiteSpace(logical) || logical.Split('/').Any(part => part is "" or "." or "..") || logical.Contains(':'))
        throw new InvalidDataException("Invalid logical file path.");
    return logical;
}
static FileRecord ReadFile(FileInfo item, string logical)
{
    using FileStream stream = item.Open(FileMode.Open, FileAccess.Read, FileShare.Read);
    byte[] sha1 = SHA1.HashData(stream);
    stream.Position = 0;
    byte[] sha256 = SHA256.HashData(stream);
    return new(logical, item.Length, Hex(sha1), Hex(sha256), SpdxId("File", logical), $"{item.Length} bytes");
}
static FileRecord ReadLink(string logical, string target)
{
    byte[] bytes = Encoding.UTF8.GetBytes(target);
    return new(logical, bytes.LongLength, Hex(SHA1.HashData(bytes)), Hex(SHA256.HashData(bytes)),
        SpdxId("File", logical), $"symbolic link target: {target}");
}
static void ReadTree(DirectoryInfo directory, string relative, List<FileRecord> files)
{
    if ((directory.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Linked payload directory.");
    foreach (FileSystemInfo entry in directory.EnumerateFileSystemInfos().OrderBy(item => item.Name, StringComparer.Ordinal))
    {
        string logical = NormalizeLogical(string.IsNullOrEmpty(relative) ? entry.Name : relative + "/" + entry.Name);
        if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            files.Add(ReadLink(logical, entry.LinkTarget ?? throw new InvalidDataException("Payload reparse point is not a symbolic link.")));
            continue;
        }
        if (entry is DirectoryInfo child) ReadTree(child, logical, files);
        else files.Add(ReadFile((FileInfo)entry, logical));
    }
}
static IEnumerable<string> FindFiles(DirectoryInfo directory, string suffix)
{
    if ((directory.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Linked payload directory.");
    foreach (FileSystemInfo entry in directory.EnumerateFileSystemInfos().OrderBy(item => item.Name, StringComparer.Ordinal))
    {
        if ((entry.Attributes & FileAttributes.ReparsePoint) != 0) continue;
        if (entry is DirectoryInfo child)
        {
            foreach (string nested in FindFiles(child, suffix)) yield return nested;
        }
        else if (entry.Name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) yield return entry.FullName;
    }
}
static string TreeIdentity(IEnumerable<FileRecord> files)
{
    using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    foreach (FileRecord file in files)
    {
        byte[] line = Encoding.UTF8.GetBytes(file.Path + "\0" + file.Bytes + "\0" + file.SHA256 + "\n");
        hash.AppendData(line);
    }
    return Hex(hash.GetHashAndReset());
}
static void ValidateRuntimeArchive(RuntimeInfo runtime)
{
    string? packageRoot = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
    if (string.IsNullOrWhiteSpace(packageRoot))
        packageRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
    string folder = Path.Combine(packageRoot, runtime.ID.ToLowerInvariant(), runtime.Version.ToLowerInvariant());
    string archive = Path.Combine(folder, $"{runtime.ID.ToLowerInvariant()}.{runtime.Version.ToLowerInvariant()}.nupkg");
    string sha512File = archive + ".sha512";
    if (!File.Exists(archive) || !File.Exists(sha512File)
        || (File.GetAttributes(archive) & FileAttributes.ReparsePoint) != 0
        || (File.GetAttributes(sha512File) & FileAttributes.ReparsePoint) != 0)
        throw new InvalidDataException("Restored runtime package archive is unavailable or linked.");
    string restoredContentHash = File.ReadAllText(sha512File).Trim();
    if (restoredContentHash != runtime.ContentHash
        || Hex(SHA256.HashData(File.ReadAllBytes(archive))) != runtime.ArchiveSHA256)
        throw new InvalidDataException("Restored runtime package archive differs from the reviewed snapshot.");
}

sealed record PackageInfo(string ID, string Version, string ContentHash, string ArchiveSHA256, string License, string? RepositoryUrl, string? RepositoryCommit, string[] NoticePaths);
sealed record RuntimeInfo(string ID, string Version, string RID, string ContentHash, string ArchiveSHA256, string[] NoticePaths);
sealed record ReferenceInfo(string ID, string Version, string ParentPackage);
sealed record FileRecord(string Path, long Bytes, string SHA1, string SHA256, string SpdxId, string Description);
