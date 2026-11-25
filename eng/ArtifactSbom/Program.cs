using System.IO.Compression;
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
        || !Regex.IsMatch(sourceCommit, @"^[0-9a-f]{40}(\+dirty\.[0-9a-f]{64})?$", RegexOptions.CultureInvariant))
        throw new InvalidDataException("Version, RID, kind or source commit is invalid.");
    // "<commit>+dirty.<sha256>": built from uncommitted changes to the build inputs; the hash names that state.
    string commitOnly = sourceCommit[..40];
    string sourceStatement = sourceCommit.Length == 40
        ? $"Source commit {sourceCommit}"
        : $"Source commit {commitOnly} plus uncommitted changes (state SHA-256 {sourceCommit[47..]}); not a reproducible source identity";
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
        // The assemblies a package's build targets reference directly, which the dependency manifest lists as references
        // rather than under the package: the reviewed index names each one and the package file it comes from.
        ReferenceComponent[] components = item.TryGetProperty("ReferenceComponents", out JsonElement referenceComponents)
            ? referenceComponents.EnumerateArray().Select(value => new ReferenceComponent(Required(value, "ID"), PackageRelative(Required(value, "PackagePath")))).ToArray()
            : [];
        // Files publishing takes from the package that no dependency manifest names (its build targets' content items, or
        // the documentation beside a reference they add): the reviewed index names each, by its path in the app folder,
        // with the package file it is, that file's size and SHA-256, and what copies it.
        UnlistedFile[] unlisted = item.TryGetProperty("UnlistedPayloadFiles", out JsonElement unlistedFiles)
            ? unlistedFiles.EnumerateArray().Select(value => new UnlistedFile(NormalizeLogical(Required(value, "Path")),
                PackageRelative(Required(value, "PackagePath")), value.GetProperty("Bytes").GetInt64(), Required(value, "SHA256"),
                Required(value, "CopiedBy"))).ToArray()
            : [];
        if (!packages.TryAdd(id, new(id, packageVersion, contentHash, archiveHash, licenseText, repositoryUrl, repositoryCommit, notices, components, unlisted)))
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
    // The SDK's apphost packs: the reviewed launcher template of each RID (see AppHost), with the license texts the index
    // retains for it.
    var appHostPacks = new List<AppHostInfo>();
    if (snapshot.TryGetProperty("AppHostPacks", out JsonElement hostItems))
        foreach (JsonElement item in hostItems.EnumerateArray())
        {
            var host = new AppHostInfo(Required(item, "ID"), Required(item, "Version"), Required(item, "RID"), Required(item, "RawArchiveSHA512"),
                Required(item, "PackageArchiveSHA256"), PackageRelative(Required(item, "TemplatePath")), item.GetProperty("TemplateBytes").GetInt64(),
                Required(item, "TemplateSHA256"),
                item.GetProperty("RetainedNoticePaths").EnumerateArray().Select(value => value.GetString() ?? throw new InvalidDataException("Null apphost notice path.")).ToArray());
            if (host.ID != "Microsoft.NETCore.App.Host." + host.RID || appHostPacks.Any(other => other.ID == host.ID && other.Version == host.Version))
                throw new InvalidDataException("Malformed or duplicate apphost pack in notice snapshot.");
            appHostPacks.Add(host);
        }
    // The retained license and notice texts, by their path in the notice folder, with their reviewed SHA-256.
    var noticeHashes = new Dictionary<string, string>(StringComparer.Ordinal);
    if (snapshot.TryGetProperty("Files", out JsonElement noticeItems))
        foreach (JsonElement item in noticeItems.EnumerateArray())
            if (!noticeHashes.TryAdd(Required(item, "Path"), Required(item, "SHA256")))
                throw new InvalidDataException("Duplicate retained notice text in notice snapshot.");

    var seenPackages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var seenRuntimePacks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var references = new Dictionary<string, ReferenceInfo>(StringComparer.OrdinalIgnoreCase);
    var projects = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    var omittedProjects = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
    // The files publish copies from each restored package folder (paths inside the package) and from each runtime pack
    // (bare names by kind): they must be the reviewed archives' own bytes.
    var packageFiles = new Dictionary<string, SortedSet<string>>(StringComparer.OrdinalIgnoreCase);
    var runtimePackFiles = new Dictionary<string, SortedSet<string>>(StringComparer.OrdinalIgnoreCase);
    // Where publishing puts the files the manifests name, relative to the payload: an app folder (one holding a manifest)
    // plus the file's name, under its culture for resources; a runtime target keeps its path.
    var manifestNamed = new HashSet<string>(StringComparer.Ordinal);
    var appFolders = new SortedSet<string>(StringComparer.Ordinal);
    string[] assetKinds = ["runtime", "native", "resources", "runtimeTargets"];
    string[] depsFiles = FindFiles(new DirectoryInfo(payload), ".deps.json").Order(StringComparer.Ordinal).ToArray();
    if (depsFiles.Length == 0) throw new InvalidDataException("Payload has no dependency manifest.");
    foreach (string depsPath in depsFiles)
    {
        using var depsDocument = JsonDocument.Parse(File.ReadAllBytes(depsPath));
        string depsFolder = Path.GetDirectoryName(depsPath) ?? throw new InvalidDataException("Dependency manifest has no folder.");
        string appFolder = LogicalFolder(payload, depsFolder);
        appFolders.Add(appFolder);
        // The files each library contributes, by kind, as the manifest's targets name them.
        var libraryAssets = new Dictionary<string, List<(string Kind, string Name)>>(StringComparer.OrdinalIgnoreCase);
        if (depsDocument.RootElement.TryGetProperty("targets", out JsonElement targets) && targets.ValueKind == JsonValueKind.Object)
            foreach (JsonProperty target in targets.EnumerateObject().Where(target => target.Value.ValueKind == JsonValueKind.Object))
                foreach (JsonProperty entry in target.Value.EnumerateObject().Where(entry => entry.Value.ValueKind == JsonValueKind.Object))
                    foreach (string assetKind in assetKinds)
                        if (entry.Value.TryGetProperty(assetKind, out JsonElement assets) && assets.ValueKind == JsonValueKind.Object)
                        {
                            if (!libraryAssets.TryGetValue(entry.Name, out var list)) libraryAssets[entry.Name] = list = [];
                            foreach (JsonProperty asset in assets.EnumerateObject())
                            {
                                list.Add((assetKind, asset.Name));
                                string fileName = asset.Name[(asset.Name.LastIndexOf('/') + 1)..];
                                string? culture = assetKind == "resources" && asset.Value.ValueKind == JsonValueKind.Object
                                    && asset.Value.TryGetProperty("locale", out JsonElement locale) ? locale.GetString() : null;
                                manifestNamed.Add(JoinLogical(appFolder, assetKind == "runtimeTargets" ? asset.Name : culture is null ? fileName : culture + "/" + fileName));
                            }
                        }
        List<(string Kind, string Name)> AssetsOf(string library) => libraryAssets.TryGetValue(library, out var list) ? list : [];
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
                seenRuntimePacks.Add(runtimeIdentity);
                if (!runtimePackFiles.TryGetValue(runtimeIdentity, out var named)) runtimePackFiles[runtimeIdentity] = named = new(StringComparer.Ordinal);
                named.UnionWith(AssetsOf(library.Name).Select(asset => asset.Kind + "/" + asset.Name));
            }
            else if (type == "package" || packages.ContainsKey(id))
            {
                if (type != "package" || !packages.TryGetValue(id, out PackageInfo? package)
                    || package.Version != libraryVersion || Required(library.Value, "sha512") != "sha512-" + package.ContentHash)
                    throw new InvalidDataException("Published package differs from the reviewed snapshot.");
                seenPackages.Add(package.ID);
                if (!packageFiles.TryGetValue(package.ID, out var named)) packageFiles[package.ID] = named = new(StringComparer.Ordinal);
                named.UnionWith(AssetsOf(library.Name).Select(asset => PackageRelative(asset.Name)));
            }
            else if (type == "project")
            {
                // Named runtime files that are all missing mean the project is not shipped in this payload: the Linux and
                // macOS packages drop the Windows-only helpers after publishing, while FileCat.deps.json still names them.
                string[] named = AssetsOf(library.Name).Where(asset => asset.Kind == "runtime").Select(asset => asset.Name).ToArray();
                if (named.Length > 0
                    && !named.Any(asset => File.Exists(Path.Combine(depsFolder, asset.Replace('/', Path.DirectorySeparatorChar)))))
                {
                    omittedProjects.Add(id);
                    continue;
                }
                if (projects.TryGetValue(id, out string? existing) && existing != libraryVersion)
                    throw new InvalidDataException("Conflicting project version in payload.");
                projects[id] = libraryVersion;
            }
            else if (type == "reference")
            {
                // Only a reference the reviewed index names as a package's own is attributed to it; a name that merely
                // starts with a package's ID is not.
                (PackageInfo Package, ReferenceComponent Component)[] origins = packages.Values
                    .Where(package => libraryVersion == package.Version)
                    .SelectMany(package => package.ReferenceComponents
                        .Where(candidate => candidate.ID.Equals(id, StringComparison.OrdinalIgnoreCase))
                        .Select(candidate => (package, candidate)))
                    .ToArray();
                if (Required(library.Value, "sha512") != "")
                    throw new InvalidDataException("Published reference component carries a package hash.");
                if (origins.Length != 1)
                    throw new InvalidDataException("Published reference component is not one the reviewed snapshot names as a package's own.");
                (PackageInfo parent, ReferenceComponent component) = origins[0];
                string fileName = component.PackagePath[(component.PackagePath.LastIndexOf('/') + 1)..];
                if (AssetsOf(library.Name).Any(asset => asset.Kind != "runtime" || asset.Name != fileName))
                    throw new InvalidDataException("Published reference component's files differ from the one its reviewed package names.");
                var reference = new ReferenceInfo(id, libraryVersion, parent.ID, component.PackagePath);
                if (references.TryGetValue(id, out ReferenceInfo? existingReference) && existingReference != reference)
                    throw new InvalidDataException("Conflicting reference component in payload.");
                references[id] = reference;
                seenPackages.Add(parent.ID);
                if (!packageFiles.TryGetValue(parent.ID, out var named)) packageFiles[parent.ID] = named = new(StringComparer.Ordinal);
                named.Add(component.PackagePath);
            }
            else throw new InvalidDataException("Unsupported dependency kind in payload.");
        }
    }
    omittedProjects.ExceptWith(projects.Keys);
    if (references.Values.Any(reference => !seenPackages.Contains(reference.ParentPackage)))
        throw new InvalidDataException("Published reference component omits its reviewed parent package.");
    var files = new List<FileRecord>();
    ReadTree(new DirectoryInfo(payload), "", files);
    var payloadFiles = files.ToDictionary(file => file.Path, StringComparer.Ordinal);
    // A file a package's build targets copy without any manifest naming it ships only as the reviewed index declares it,
    // which makes the package one this payload takes; its bytes must be that package file's.
    var unlistedShipped = new SortedDictionary<string, (PackageInfo Package, UnlistedFile Declared, FileRecord File)>(StringComparer.Ordinal);
    foreach (PackageInfo package in packages.Values.OrderBy(package => package.ID, StringComparer.OrdinalIgnoreCase))
        foreach (UnlistedFile declared in package.UnlistedFiles)
            foreach (string folder in appFolders)
                if (payloadFiles.TryGetValue(JoinLogical(folder, declared.Path), out FileRecord? file) && !file.IsLink)
                {
                    if (!unlistedShipped.TryAdd(file.Path, (package, declared, file)))
                        throw new InvalidDataException($"The reviewed index declares {file.Path} for more than one package.");
                    seenPackages.Add(package.ID);
                    if (!packageFiles.TryGetValue(package.ID, out var named)) packageFiles[package.ID] = named = new(StringComparer.Ordinal);
                    named.Add(declared.PackagePath);
                }
    int restoredFilesMatched = 0;
    var memberHashes = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
    foreach (string packageId in seenPackages.Order(StringComparer.OrdinalIgnoreCase))
    {
        PackageInfo package = packages[packageId];
        IReadOnlyDictionary<string, string> compared = ValidateNuGetArchive(package.ID, package.Version, package.ContentHash, package.ArchiveSHA256, false,
            _ => packageFiles.TryGetValue(package.ID, out var named) ? named : []);
        memberHashes[package.ID] = compared;
        restoredFilesMatched += compared.Count;
    }
    foreach ((string path, (PackageInfo package, UnlistedFile declared, FileRecord file)) in unlistedShipped)
        if (memberHashes[package.ID][declared.PackagePath] != declared.SHA256 || file.SHA256 != declared.SHA256 || file.Bytes != declared.Bytes)
            throw new InvalidDataException($"{path} is not {package.ID} {package.Version}'s {declared.PackagePath} byte for byte, as the reviewed index declares.");
    foreach (string identity in seenRuntimePacks.Order(StringComparer.OrdinalIgnoreCase))
    {
        RuntimeInfo runtime = runtimePacks[identity];
        restoredFilesMatched += ValidateNuGetArchive(runtime.ID, runtime.Version, runtime.ContentHash, runtime.ArchiveSHA256, true,
            members => runtimePackFiles[identity].Select(named => RuntimePackMember(members, runtime.RID, named))).Count;
    }
    bool selfContained = kind != "fdd";
    if (selfContained)
    {
        if (seenRuntimePacks.Count != 1) throw new InvalidDataException("Self-contained payload must contain exactly one reviewed runtime pack.");
        RuntimeInfo selected = runtimePacks[seenRuntimePacks.Single()];
        if (selected.RID != rid) throw new InvalidDataException("Runtime pack RID differs from the artifact RID.");
    }
    else if (seenRuntimePacks.Count != 0) throw new InvalidDataException("Framework-dependent payload unexpectedly contains a runtime pack.");

    // A launcher made from the .NET apphost (it carries the apphost's binding check) must be a reviewed template of this
    // RID, bound to the DLL beside it and changed only as publishing, signing and packaging change it (AppHost.TryRelate).
    // The reviewed pack whose template it matches is the one the SDK used.
    var launchers = new List<(FileRecord File, AppHostInfo Host, string AppDll, string Relation)>();
    var templates = new Dictionary<AppHostInfo, byte[]>();
    foreach (FileRecord file in files.Where(file => !file.IsLink))
    {
        byte[] shipped = File.ReadAllBytes(Path.Combine(payload, file.Path.Replace('/', Path.DirectorySeparatorChar)));
        if (!AppHost.CarriesBindingCheck(shipped)) continue;
        int slash = file.Path.LastIndexOf('/');
        string folder = slash < 0 ? "" : file.Path[..slash], name = file.Path[(slash + 1)..];
        string appDll = (rid.StartsWith("win-", StringComparison.Ordinal) && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name) + ".dll";
        if (!payloadFiles.TryGetValue(JoinLogical(folder, appDll), out FileRecord? dll) || dll.IsLink)
            throw new InvalidDataException($"{file.Path} carries the .NET apphost's binding check, but its app {appDll} is not beside it.");
        byte[] dllBytes = File.ReadAllBytes(Path.Combine(payload, dll.Path.Replace('/', Path.DirectorySeparatorChar)));
        var mismatches = new List<string>();
        (AppHostInfo Host, string Relation)? match = null;
        foreach (AppHostInfo host in appHostPacks.Where(host => host.RID == rid).OrderBy(host => host.Version, StringComparer.Ordinal))
        {
            if (!templates.TryGetValue(host, out byte[]? template)) templates[host] = template = AppHostTemplate(host, noticeHashes);
            if (AppHost.TryRelate(AppHost.Bind(template, appDll), shipped, appDll, dllBytes, out string relation, out string mismatch))
            {
                match = (host, relation);
                break;
            }
            mismatches.Add($"{host.ID} {host.Version}: {mismatch}");
        }
        if (match is not { } found)
            throw new InvalidDataException($"{file.Path} carries the .NET apphost's binding check, but it is no reviewed {rid} apphost bound to {appDll}"
                + (mismatches.Count == 0 ? ": the reviewed index has no apphost pack for this RID." : ": " + string.Join("; ", mismatches) + "."));
        launchers.Add((file, found.Host, appDll, found.Relation));
    }
    if (launchers.Select(launcher => launcher.Host).Distinct().Count() > 1)
        throw new InvalidDataException("The payload's launchers come from more than one apphost pack.");
    if (selfContained && launchers.Count != 0 && launchers[0].Host.Version != runtimePacks[seenRuntimePacks.Single()].Version)
        throw new InvalidDataException("The launchers' apphost and the payload's runtime pack are different .NET releases.");

    // Every payload file that is a reviewed archive's member byte for byte must be declared: named by a dependency manifest
    // or by the reviewed index (as an unlisted file), or made from the apphost. A file a package's build targets copy
    // without either would otherwise ship with no package in its SBOM. The archives are those this payload takes and the
    // App graph's other reviewed packages restored here (build-only ones too, whose targets can copy files). A license or
    // notice text the reviewed index retains is no component, wherever it sits (the notice folder, or another component's
    // texts that quote the same license), and an empty file says nothing of where it comes from.
    var declaredFiles = new HashSet<string>(manifestNamed, StringComparer.Ordinal);
    declaredFiles.UnionWith(unlistedShipped.Keys);
    declaredFiles.UnionWith(launchers.Select(launcher => launcher.File.Path));
    var retainedTexts = new HashSet<string>(noticeHashes.Values, StringComparer.Ordinal);
    var undeclaredBySize = files.Where(file => !file.IsLink && file.Bytes > 0 && !declaredFiles.Contains(file.Path) && !retainedTexts.Contains(file.SHA256))
        .GroupBy(file => file.Bytes).ToDictionary(group => group.Key, group => group.ToList());
    var scannedArchives = seenPackages.Select(id => packages[id]).Select(package => (package.ID, package.Version, package.ArchiveSHA256))
        .Concat(seenRuntimePacks.Select(identity => runtimePacks[identity]).Select(runtime => (runtime.ID, runtime.Version, runtime.ArchiveSHA256)))
        .Concat(launchers.Select(launcher => (launcher.Host.ID, launcher.Host.Version, launcher.Host.ArchiveSHA256)))
        .Concat(packages.Values.Where(package => !seenPackages.Contains(package.ID)).Select(package => (package.ID, package.Version, package.ArchiveSHA256)))
        .Distinct().Where(archive => File.Exists(ArchivePath(archive.ID, archive.Version)))
        .OrderBy(archive => archive.ID + "/" + archive.Version, StringComparer.Ordinal).ToList();
    foreach ((string id, string archiveVersion, string archiveSHA256) in scannedArchives)
        foreach ((string member, long length, string hash) in ArchiveMembers(ArchivePath(id, archiveVersion), archiveSHA256, undeclaredBySize))
            foreach (FileRecord file in undeclaredBySize[length].Where(file => file.SHA256 == hash))
                throw new InvalidDataException($"{file.Path} is {id} {archiveVersion}'s {member} byte for byte, but neither a dependency manifest nor the reviewed index declares it.");

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
    // The runtime an AppImage starts with is type2-runtime, under its own licenses: its own package holds it, not FileCat's.
    FileRecord? appImageRuntime = kind == "appimage" ? files.FirstOrDefault(file => file.Path == ".appimage/runtime") : null;
    // The installer adds Inno Setup's license text to the installed tree; with it come Inno Setup's loader (the setup
    // executable) and the uninstaller it installs, which are Inno Setup's work and under its license, not FileCat's.
    FileRecord? innoLicense = kind == "setup-payload"
        ? files.FirstOrDefault(file => file.Path.StartsWith("licenses/InnoSetup-", StringComparison.Ordinal) && file.Path.EndsWith(".txt", StringComparison.Ordinal))
        : null;
    List<FileRecord> ownFiles = files.Where(file => !ReferenceEquals(file, appImageRuntime) && !ReferenceEquals(file, innoLicense)).ToList();
    string verification = VerificationCode(ownFiles);
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
            ["copyrightText"] = "Copyright (c) 2026 Marek Střihavka <marek.strihavka@gmail.com> and FileCat contributors",
            ["sourceInfo"] = sourceStatement,
            ["comment"] = $"Exact {kind} payload tree for {rid}; tree SHA-256 {treeIdentity}. Preliminary composition evidence, not signing or candidate qualification."
                + (omittedProjects.Count == 0 ? "" : $" Its dependency manifest also names {string.Join(", ", omittedProjects)}, whose files this payload does not carry: not shipped here."),
            ["hasFiles"] = new JsonArray(ownFiles.Select(file => (JsonNode?)file.SpdxId).ToArray()),
            ["externalRefs"] = new JsonArray(new JsonObject
            {
                ["referenceCategory"] = "OTHER",
                ["referenceType"] = "vcs",
                ["referenceLocator"] = $"https://github.com/benny-cz/FileCat@{commitOnly}"
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
            .OrderBy(reference => reference.ID, StringComparer.OrdinalIgnoreCase)
            .Select(reference => $"{reference.ID}/{reference.Version} (from {reference.PackagePath})").ToArray();
        if (aliases.Length != 0)
            node["comment"] = "Assemblies this reviewed package's build targets reference directly, which the dependency manifest lists as "
                + "references; the reviewed notice index names each as this package's own: " + string.Join(", ", aliases)
                + ". Their restored files match this archive's members; their payload bytes, which publishing may compile ahead of time, "
                + "are recorded as files.";
        string[] unlistedHere = unlistedShipped.Values.Where(shipped => shipped.Package.ID.Equals(package.ID, StringComparison.OrdinalIgnoreCase))
            .Select(shipped => $"./{shipped.File.Path} ({shipped.Declared.PackagePath})").ToArray();
        if (unlistedHere.Length != 0)
        {
            string unlistedText = "Files publishing takes from this package through its build targets without any dependency manifest naming them; "
                + "the reviewed notice index declares each, and each is this archive's member byte for byte, EXPANDED_FROM_ARCHIVE this package: "
                + string.Join(", ", unlistedHere) + ".";
            node["comment"] = node["comment"] is JsonNode existing ? existing.GetValue<string>() + " " + unlistedText : unlistedText;
        }
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
    // The .NET apphost every launcher here is made from, framework-dependent payloads included: the reviewed pack whose
    // template the launchers match, with how each launcher's bytes relate to it in its file comment.
    string? appHostId = null;
    if (launchers.Count != 0)
    {
        AppHostInfo host = launchers[0].Host;
        appHostId = SpdxId("AppHost", host.ID + "/" + host.Version);
        dependencyIds.Add(appHostId);
        spdxPackages.Add(new JsonObject
        {
            ["name"] = host.ID,
            ["SPDXID"] = appHostId,
            ["versionInfo"] = host.Version,
            ["downloadLocation"] = $"https://api.nuget.org/v3-flatcontainer/{host.ID.ToLowerInvariant()}/{host.Version}/{host.ID.ToLowerInvariant()}.{host.Version}.nupkg",
            ["filesAnalyzed"] = false,
            ["checksums"] = new JsonArray(new JsonObject { ["algorithm"] = "SHA256", ["checksumValue"] = host.ArchiveSHA256 }),
            ["licenseConcluded"] = "NOASSERTION",
            ["licenseDeclared"] = "MIT",
            ["licenseComments"] = "Retained texts: " + string.Join(", ", host.NoticePaths) + "; the reviewed notice index pins them as this archive's members of the same names.",
            ["copyrightText"] = "NOASSERTION",
            ["comment"] = $"The .NET application host. Publishing makes each launcher from this archive's {host.TemplatePath} ({host.TemplateBytes} bytes, "
                + $"SHA-256 {host.TemplateSHA256}) by writing its app's DLL name over a placeholder: "
                + string.Join(", ", launchers.Select(launcher => $"./{launcher.File.Path} ({launcher.AppDll})"))
                + ". Each is GENERATED_FROM this package; its file comment says how its bytes relate to the template."
                + (selfContained ? " The same .NET release as the runtime pack." : ""),
            ["externalRefs"] = new JsonArray(new JsonObject
            {
                ["referenceCategory"] = "PACKAGE-MANAGER", ["referenceType"] = "purl",
                ["referenceLocator"] = $"pkg:nuget/{Uri.EscapeDataString(host.ID)}@{host.Version}"
            })
        });
    }
    // What the file comments add to a file's size: how a launcher or an unlisted package file relates to its source.
    var fileNotes = new Dictionary<string, string>(StringComparer.Ordinal);
    foreach (var launcher in launchers)
        fileNotes[launcher.File.Path] = $"made from the .NET apphost {launcher.Host.ID} {launcher.Host.Version} ({launcher.Host.TemplatePath}) "
            + $"bound to {launcher.AppDll}: {launcher.Relation}";
    foreach ((string path, (PackageInfo package, UnlistedFile declared, FileRecord _)) in unlistedShipped)
        fileNotes[path] = $"{package.ID} {package.Version}'s {declared.PackagePath} byte for byte, which no dependency manifest names: {declared.CopiedBy}";
    foreach ((string name, string projectVersion) in projects.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
    {
        if (name.Equals("FileCat", StringComparison.OrdinalIgnoreCase)) continue;
        string spdxId = SpdxId("Project", name + "/" + projectVersion);
        dependencyIds.Add(spdxId);
        // FileCat's archive projects retain BSD rardecode parts and selected public-domain SDK source. The separate
        // RAR 1.5 decoder has XADMaster terms; SharpCompress's parts retain their component licenses.
        (string license, string copyright, string? comment) = name.ToLowerInvariant() switch
        {
            "filecat.archives" or "filecat.archives.writing" => ("MIT AND BSD-2-Clause",
                "Copyright (c) 2026 Marek Střihavka <marek.strihavka@gmail.com> and FileCat contributors; "
                + "rardecode ports Copyright (c) 2015 Nicholas Waples; selected LZMA SDK source Igor Pavlov: Public domain",
                "FileCat code with bounded BSD-2-Clause rardecode decoding/password primitives as applicable; "
                + "the development writing engine also adapts individually public-domain BCJ2 files from LZMA SDK 26.05 "
                + "(b88c6bc09339de32f7d18bbab900686b244f42ba). Exact source and notice inventories are in the repository; "
                + "retained terms/attribution: THIRD-PARTY-NOTICES.md and licenses/rardecode/LICENSE. "
                + "This does not make the separate LGPL RAR 1.5 assembly a writing-worker dependency or qualify a writer profile."),
            "filecat.archives.rar15" => ("LGPL-2.1-or-later",
                "Copyright (c) 2017-present MacPaw Way Ltd. (XADMaster); C# port Copyright (c) 2026 Marek Střihavka <marek.strihavka@gmail.com> and FileCat contributors", null),
            "filecat.sshnet" => ("MIT AND ISC", "Copyright (c) Renci, Oleg Kapeljushnik, Gert Driesen and contributors; BCrypt code copyright (c) 2006 Damien Miller and copyright (c) 2010 Ryan D. Emerle",
                "SSH.NET 2026.0.0 (github.com/sshnet/SSH.NET commit 7b2fd3dbf2c86a80a7b06cea020aa5f821c9902e) with FileCat's typed SFTP permissions requests. License texts: licenses/sshnet/LICENSE and licenses/sshnet/LICENSE-bcrypt."),
            "filecat.sharpcompress" => ("MIT AND Apache-2.0 AND MS-PL AND BSD-2-Clause AND BSD-3-Clause AND Zlib",
                "Copyright (c) 2014 Adam Hathcock and SharpCompress contributors; the copyrights of its parts from other projects are in "
                + "licenses/sharpcompress/COMPONENTS.md; FileCat's changes Copyright (c) 2026 Marek Střihavka <marek.strihavka@gmail.com> and FileCat contributors",
                "SharpCompress 0.50.5 (github.com/adamhathcock/sharpcompress commit e4193af83b6ec8bdd5fb4f6ebffc2315dd3fc626) built from "
                + "src/ThirdParty/SharpCompress, cut down to its 7z reader and bzip2, xz, zstd and raw DEFLATE stream codecs; "
                + "the archive-writing development worker also uses its LZMA/LZMA2, PPMd7, bzip2/Zstandard encoders, branch/delta/swap transforms and raw DEFLATE decoder. No UnRAR-derived RAR or PPMd variant H "
                + "code and none of its other formats. Every part's license is OSI-approved. Texts and notices: licenses/sharpcompress."),
            _ => ("MIT", "Copyright (c) 2026 Marek Střihavka <marek.strihavka@gmail.com> and FileCat contributors", null),
        };
        var project = new JsonObject
        {
            ["name"] = name, ["SPDXID"] = spdxId, ["versionInfo"] = projectVersion,
            ["downloadLocation"] = "NOASSERTION", ["filesAnalyzed"] = false,
            ["licenseConcluded"] = "NOASSERTION", ["licenseDeclared"] = license,
            ["copyrightText"] = copyright,
            ["sourceInfo"] = "Built from FileCat " + sourceStatement[0..1].ToLowerInvariant() + sourceStatement[1..]
        };
        if (comment is not null) project["licenseComments"] = comment;
        spdxPackages.Add(project);
    }
    var relationships = new JsonArray(new JsonObject
    {
        ["spdxElementId"] = "SPDXRef-DOCUMENT", ["relationshipType"] = "DESCRIBES", ["relatedSpdxElement"] = rootId
    });
    if (appImageRuntime is not null)
    {
        // Its version comes from the reviewed runtime snapshot beside the dependency index, when there is one.
        string runtimeIndex = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(noticeIndexPath)) ?? "", "appimage-runtime", "index.json");
        string runtimeVersion = "NOASSERTION";
        if (File.Exists(runtimeIndex))
        {
            using var runtimeDocument = JsonDocument.Parse(File.ReadAllBytes(runtimeIndex));
            runtimeVersion = runtimeDocument.RootElement.GetProperty("RuntimeInput").GetProperty("DeclaredSourceCommit").GetString() ?? "NOASSERTION";
        }
        spdxPackages.Add(new JsonObject
        {
            ["name"] = "type2-runtime",
            ["SPDXID"] = "SPDXRef-AppImage-Runtime",
            ["versionInfo"] = runtimeVersion,
            ["downloadLocation"] = "https://github.com/AppImage/type2-runtime",
            ["filesAnalyzed"] = true,
            ["packageVerificationCode"] = new JsonObject { ["packageVerificationCodeValue"] = VerificationCode([appImageRuntime]) },
            ["hasFiles"] = new JsonArray(appImageRuntime.SpdxId),
            ["licenseConcluded"] = "NOASSERTION",
            ["licenseDeclared"] = "MIT AND LGPL-2.1-only AND BSD-2-Clause AND BSD-3-Clause AND Zlib",
            ["licenseComments"] = "type2-runtime (MIT) statically linking libfuse 3.15.0 (LGPL-2.1-only), squashfuse 0.5.2 (BSD-2-Clause), "
                + "zstd (BSD-3-Clause), zlib (Zlib), mimalloc (MIT) and musl (MIT), as its producer declares them; texts in "
                + "licenses/appimage-runtime. Its corresponding source ships beside the AppImage as "
                + $"FileCat-{version}-{rid}-appimage-runtime-source.tar.gz.",
            ["copyrightText"] = "NOASSERTION",
            ["comment"] = "The bytes this AppImage starts with: the reviewed runtime of eng/linux/appimage-runtime, with the "
                + "AppImage's MD5 digest that appimagetool writes into its .digest_md5 section."
        });
        relationships.Add(new JsonObject { ["spdxElementId"] = rootId, ["relationshipType"] = "CONTAINS", ["relatedSpdxElement"] = "SPDXRef-AppImage-Runtime" });
    }
    if (innoLicense is not null)
    {
        string innoVersion = innoLicense.Path["licenses/InnoSetup-".Length..^".txt".Length];
        spdxPackages.Add(new JsonObject
        {
            ["name"] = "Inno Setup",
            ["SPDXID"] = "SPDXRef-Inno-Setup",
            ["versionInfo"] = innoVersion,
            ["downloadLocation"] = "https://jrsoftware.org/isinfo.php",
            ["filesAnalyzed"] = true,
            ["packageVerificationCode"] = new JsonObject { ["packageVerificationCodeValue"] = VerificationCode([innoLicense]) },
            ["hasFiles"] = new JsonArray(innoLicense.SpdxId),
            ["licenseConcluded"] = "NOASSERTION",
            ["licenseDeclared"] = "NOASSERTION",
            ["licenseComments"] = $"Inno Setup License (Jordan Russell and Martijn Laan), a permissive license that is not SPDX-listed or "
                + $"OSI-approved; its text is installed as {innoLicense.Path}.",
            ["copyrightText"] = "Copyright (C) 1997-2026 Jordan Russell. Portions Copyright (C) 2000-2026 Martijn Laan.",
            ["comment"] = $"The installer's loader (the setup executable's own code) and the uninstaller it installs (unins000.exe, with "
                + $"unins000.dat) are Inno Setup {innoVersion}'s, from the pinned compiler (eng/toolchains/inno-setup.json). Their bytes "
                + "come from that compiler when the installer is built and are not in this payload tree; the installer build's receipt "
                + "pins the setup executable."
        });
        relationships.Add(new JsonObject { ["spdxElementId"] = rootId, ["relationshipType"] = "CONTAINS", ["relatedSpdxElement"] = "SPDXRef-Inno-Setup" });
    }
    foreach (string dependencyId in dependencyIds.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        relationships.Add(new JsonObject { ["spdxElementId"] = rootId, ["relationshipType"] = "DEPENDS_ON", ["relatedSpdxElement"] = dependencyId });
    // Each launcher is generated from the apphost package; each unlisted package file is expanded from its package's archive.
    foreach (var launcher in launchers.OrderBy(launcher => launcher.File.Path, StringComparer.Ordinal))
        relationships.Add(new JsonObject { ["spdxElementId"] = launcher.File.SpdxId, ["relationshipType"] = "GENERATED_FROM", ["relatedSpdxElement"] = appHostId });
    foreach ((PackageInfo package, UnlistedFile _, FileRecord file) in unlistedShipped.Values)
        relationships.Add(new JsonObject
        {
            ["spdxElementId"] = file.SpdxId, ["relationshipType"] = "EXPANDED_FROM_ARCHIVE",
            ["relatedSpdxElement"] = SpdxId("NuGet", package.ID + "/" + package.Version)
        });

    string generatorPath = Assembly.GetExecutingAssembly().Location;
    if (string.IsNullOrWhiteSpace(generatorPath)) throw new InvalidOperationException("Missing generator assembly path.");
    string generatorHash = Hex(SHA256.HashData(File.ReadAllBytes(generatorPath)));
    var document = new JsonObject
    {
        ["spdxVersion"] = "SPDX-2.3",
        ["dataLicense"] = "CC0-1.0",
        ["SPDXID"] = "SPDXRef-DOCUMENT",
        ["name"] = $"FileCat-{version}-{rid}-{kind}",
        ["documentNamespace"] = $"https://github.com/benny-cz/FileCat/spdx/{Uri.EscapeDataString(sourceCommit)}/{Uri.EscapeDataString(version)}/{rid}/{kind}/{treeIdentity}",
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
            ["comment"] = fileNotes.TryGetValue(file.Path, out string? note) ? file.Description + "; " + note : file.Description
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
    Console.WriteLine($"Wrote SPDX 2.3 SBOM: {files.Count} exact files, {seenPackages.Count} reviewed NuGet packages, {seenRuntimePacks.Count} runtime packs ({restoredFilesMatched} restored files match their archives), tree {treeIdentity}.");
    Console.WriteLine($"Launchers made from the reviewed .NET apphost: {launchers.Count}"
        + (launchers.Count == 0 ? "" : $" ({launchers[0].Host.ID} {launchers[0].Host.Version}: {string.Join(", ", launchers.Select(launcher => launcher.File.Path))})")
        + $"; files the reviewed index declares outside the dependency manifests: {unlistedShipped.Count}; {scannedArchives.Count} reviewed archives hold no other payload file.");
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
static string VerificationCode(IEnumerable<FileRecord> files) =>
    Hex(SHA1.HashData(Encoding.ASCII.GetBytes(string.Concat(files.Select(file => file.SHA1).Order(StringComparer.Ordinal)))));
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
        SpdxId("File", logical), $"symbolic link target: {target}", IsLink: true);
}
// A folder inside the payload as a logical path ("" for the payload itself), and a path inside such a folder.
static string LogicalFolder(string payload, string folder)
{
    string relative = Path.GetRelativePath(payload, folder);
    return relative == "." ? "" : NormalizeLogical(relative);
}
static string JoinLogical(string folder, string relative) => folder.Length == 0 ? relative : folder + "/" + relative;
// The restored NuGet package folder and a package's archive in it.
static string NuGetRoot()
{
    string? packageRoot = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
    return string.IsNullOrWhiteSpace(packageRoot)
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages")
        : packageRoot;
}
static string ArchivePath(string id, string version) =>
    Path.Combine(NuGetRoot(), id.ToLowerInvariant(), version.ToLowerInvariant(), $"{id.ToLowerInvariant()}.{version.ToLowerInvariant()}.nupkg");
// The members of a reviewed archive whose sizes are among the given ones, with their SHA-256 values; the archive must have
// its reviewed SHA-256.
static List<(string Member, long Length, string SHA256)> ArchiveMembers(string archive, string archiveSHA256, IReadOnlyDictionary<long, List<FileRecord>> sizes)
{
    if ((File.GetAttributes(archive) & FileAttributes.ReparsePoint) != 0)
        throw new InvalidDataException($"Restored NuGet package archive {Path.GetFileName(archive)} is linked.");
    byte[] bytes = File.ReadAllBytes(archive);
    if (Hex(SHA256.HashData(bytes)) != archiveSHA256)
        throw new InvalidDataException($"Restored NuGet package archive {Path.GetFileName(archive)} differs from the reviewed snapshot.");
    var found = new List<(string, long, string)>();
    using var reader = new ZipArchive(new MemoryStream(bytes, false), ZipArchiveMode.Read);
    foreach (ZipArchiveEntry entry in reader.Entries)
        if (entry.Length > 0 && sizes.ContainsKey(entry.Length))
        {
            using Stream stream = entry.Open();
            found.Add((Uri.UnescapeDataString(entry.FullName), entry.Length, Hex(SHA256.HashData(stream))));
        }
    return found;
}
// The template of a reviewed apphost pack. Restored, the pack's archive must be the reviewed one, and the template and the
// texts the index retains for the pack its members (the texts by their names). The SDK prefers a pack in its own packs
// folder, and restore then leaves none: the template there is taken, as the SDK takes it. Either way its bytes must be the
// reviewed template's.
static byte[] AppHostTemplate(AppHostInfo host, IReadOnlyDictionary<string, string> noticeHashes)
{
    byte[] template;
    string archive = ArchivePath(host.ID, host.Version);
    if (File.Exists(archive))
    {
        static string Name(string path) => path[(path.LastIndexOf('/') + 1)..];
        IReadOnlyDictionary<string, string> compared = ValidateNuGetArchive(host.ID, host.Version, host.ArchiveSHA512, host.ArchiveSHA256, true,
            _ => host.NoticePaths.Select(Name).Append(host.TemplatePath));
        foreach (string path in host.NoticePaths)
            if (!noticeHashes.TryGetValue(path, out string? hash) || compared[Name(path)] != hash)
                throw new InvalidDataException($"The retained text {path} is not {host.ID} {host.Version}'s own {Name(path)}.");
        template = File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(archive)!, host.TemplatePath.Replace('/', Path.DirectorySeparatorChar)));
    }
    else
    {
        string? packed = DotnetRoots().Select(root => Path.Combine(root, "packs", host.ID, host.Version, host.TemplatePath.Replace('/', Path.DirectorySeparatorChar)))
            .FirstOrDefault(File.Exists);
        if (packed is null || (File.GetAttributes(packed) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException($"The reviewed apphost template of {host.ID} {host.Version} is neither restored nor in the SDK's packs folder.");
        template = File.ReadAllBytes(packed);
    }
    if (template.LongLength != host.TemplateBytes || Hex(SHA256.HashData(template)) != host.TemplateSHA256)
        throw new InvalidDataException($"The apphost template of {host.ID} {host.Version} differs from the reviewed one.");
    return template;
}
// The .NET installations whose packs folder the SDK may have used: DOTNET_ROOT, then the one running this tool
// (<root>/shared/Microsoft.NETCore.App/<version>/).
static IEnumerable<string> DotnetRoots()
{
    string? configured = Environment.GetEnvironmentVariable("DOTNET_ROOT");
    if (!string.IsNullOrWhiteSpace(configured)) yield return configured;
    string? runtime = Path.GetDirectoryName(typeof(object).Assembly.Location);
    if (!string.IsNullOrEmpty(runtime) && new DirectoryInfo(runtime).Parent?.Parent?.Parent is { } root) yield return root.FullName;
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
// Publish copies a package's files from its restored folder, not from the archive whose hash is reviewed: each file the
// payload takes (restoredFiles, given the archive's members) must exist there with the archive member's own bytes.
// Returns the files it compared, with their SHA-256 values.
static IReadOnlyDictionary<string, string> ValidateNuGetArchive(string id, string version, string contentHash, string archiveSHA256, bool contentHashMustMatchArchive,
    Func<IReadOnlyDictionary<string, ZipArchiveEntry>, IEnumerable<string>> restoredFiles)
{
    string archive = ArchivePath(id, version);
    string folder = Path.GetDirectoryName(archive)!;
    string sha512File = archive + ".sha512";
    if (!File.Exists(archive) || !File.Exists(sha512File)
        || (File.GetAttributes(archive) & FileAttributes.ReparsePoint) != 0
        || (File.GetAttributes(sha512File) & FileAttributes.ReparsePoint) != 0)
        throw new InvalidDataException("Restored NuGet package archive is unavailable or linked.");
    byte[] archiveBytes = File.ReadAllBytes(archive);
    string restoredArchiveHash = File.ReadAllText(sha512File).Trim();
    string actualArchiveHash = Convert.ToBase64String(SHA512.HashData(archiveBytes));
    if (restoredArchiveHash != actualArchiveHash
        || (contentHashMustMatchArchive && contentHash != actualArchiveHash)
        || Hex(SHA256.HashData(archiveBytes)) != archiveSHA256)
        throw new InvalidDataException("Restored NuGet package archive differs from the reviewed snapshot.");
    using var archiveReader = new ZipArchive(new MemoryStream(archiveBytes, false), ZipArchiveMode.Read);
    // NuGet escapes member names in the archive and restores them unescaped.
    var members = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);
    foreach (ZipArchiveEntry entry in archiveReader.Entries)
        if (!members.TryAdd(Uri.UnescapeDataString(entry.FullName), entry))
            throw new InvalidDataException($"NuGet package archive {id} {version} has duplicate member names.");
    var compared = new Dictionary<string, string>(StringComparer.Ordinal);
    foreach (string relative in restoredFiles(members).Select(PackageRelative))
    {
        if (!members.TryGetValue(relative, out ZipArchiveEntry? member))
            throw new InvalidDataException($"NuGet package archive {id} {version} lacks {relative}, which the payload takes from it.");
        var restored = new FileInfo(Path.Combine(folder, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!restored.Exists || (restored.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException($"Restored file {relative} of {id} {version} is missing or linked.");
        byte[] expected;
        using (Stream stream = member.Open()) expected = SHA256.HashData(stream);
        byte[] actual;
        using (FileStream stream = restored.Open(FileMode.Open, FileAccess.Read, FileShare.Read)) actual = SHA256.HashData(stream);
        if (!expected.AsSpan().SequenceEqual(actual))
            throw new InvalidDataException($"Restored file {relative} of {id} {version} differs from its reviewed archive.");
        compared[relative] = Hex(expected);
    }
    return compared;
}
// A path inside a package, as the dependency manifest and the notice index name them.
static string PackageRelative(string value)
{
    if (string.IsNullOrWhiteSpace(value) || value.Contains('\\') || value.Contains(':') || value.StartsWith('/')
        || value.Split('/').Any(part => part is "" or "." or ".."))
        throw new InvalidDataException("Invalid package file path.");
    return value;
}
// A runtime pack's manifest names its files bare, by kind ("runtime/<name>" or "native/<name>"): the managed ones are under
// runtimes/<rid>/lib/<framework>/ in the pack, the native ones under runtimes/<rid>/native/.
static string RuntimePackMember(IReadOnlyDictionary<string, ZipArchiveEntry> members, string rid, string named)
{
    int slash = named.IndexOf('/');
    string kind = named[..slash], name = named[(slash + 1)..];
    if (name.Length == 0 || name.Contains('/') || name.Contains('\\'))
        throw new InvalidDataException("Invalid runtime pack file name.");
    string[] matches = kind switch
    {
        "runtime" => members.Keys.Where(path => path.StartsWith($"runtimes/{rid}/lib/", StringComparison.Ordinal)
            && path.EndsWith("/" + name, StringComparison.Ordinal) && path.Count(character => character == '/') == 4).ToArray(),
        "native" => members.ContainsKey($"runtimes/{rid}/native/{name}") ? [$"runtimes/{rid}/native/{name}"] : [],
        _ => throw new InvalidDataException($"Unsupported runtime pack file kind: {kind}."),
    };
    if (matches.Length != 1) throw new InvalidDataException($"Runtime pack has no single {kind} file named {name}.");
    return matches[0];
}

sealed record PackageInfo(string ID, string Version, string ContentHash, string ArchiveSHA256, string License, string? RepositoryUrl, string? RepositoryCommit, string[] NoticePaths, ReferenceComponent[] ReferenceComponents, UnlistedFile[] UnlistedFiles);
sealed record ReferenceComponent(string ID, string PackagePath);
sealed record UnlistedFile(string Path, string PackagePath, long Bytes, string SHA256, string CopiedBy);
sealed record RuntimeInfo(string ID, string Version, string RID, string ContentHash, string ArchiveSHA256, string[] NoticePaths);
sealed record AppHostInfo(string ID, string Version, string RID, string ArchiveSHA512, string ArchiveSHA256, string TemplatePath, long TemplateBytes, string TemplateSHA256, string[] NoticePaths);
sealed record ReferenceInfo(string ID, string Version, string ParentPackage, string PackagePath);
sealed record FileRecord(string Path, long Bytes, string SHA1, string SHA256, string SpdxId, string Description, bool IsLink = false);
