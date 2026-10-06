using System.Security.Cryptography;
using System.Text.Json;

if (args.Length != 4)
{
    Console.Error.WriteLine("usage: DependencyNotices <snapshot> <App lock> <published payload> <new destination>");
    Console.Error.WriteLine("   or: DependencyNotices --appimage-runtime <snapshot> <runtime input> <new destination>");
    return 2;
}

try
{
    bool appImage = args[0] == "--appimage-runtime";
    string source = Path.GetFullPath(args[appImage ? 1 : 0]);
    string destination = Path.GetFullPath(args[3]);
    string sourcePrefix = source.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
    if (destination.StartsWith(sourcePrefix, StringComparison.OrdinalIgnoreCase) || destination.Equals(source, StringComparison.OrdinalIgnoreCase)
        || Directory.Exists(destination) || File.Exists(destination))
        throw new InvalidDataException("Notice destination must be new and outside the source snapshot.");

    var sourceFiles = new Dictionary<string, byte[]>(StringComparer.Ordinal);
    ReadTree(source, "", sourceFiles);
    using var indexDocument = JsonDocument.Parse(sourceFiles["index.json"]);
    var index = indexDocument.RootElement;
    if (index.GetProperty("SchemaVersion").GetInt32() != 1)
        throw new InvalidDataException("Unsupported notice snapshot schema.");

    var expectedFiles = new HashSet<string>(StringComparer.Ordinal) { "README.md", "index.json" };
    foreach (var file in index.GetProperty("Files").EnumerateArray())
    {
        string relative = RequiredString(file, "Path");
        if (Path.IsPathRooted(relative) || relative.Contains('\\') || relative.Contains(':')
            || relative.Split('/').Any(p => p is "" or "." or "..") || !expectedFiles.Add(relative))
            throw new InvalidDataException("Invalid or duplicate notice snapshot path.");
        if (!sourceFiles.TryGetValue(relative, out var bytes) || bytes.LongLength != file.GetProperty("Bytes").GetInt64()
            || Hash(bytes) != RequiredString(file, "SHA256"))
            throw new InvalidDataException($"Notice bytes differ from snapshot: {relative}");
    }
    if (!expectedFiles.SetEquals(sourceFiles.Keys))
        throw new InvalidDataException("Notice snapshot has missing or unindexed files.");

    if (appImage)
    {
        var runtime = index.GetProperty("RuntimeInput");
        byte[] bytes = File.ReadAllBytes(args[2]);
        if (bytes.LongLength != runtime.GetProperty("Bytes").GetInt64() || Hash(bytes) != RequiredString(runtime, "SHA256"))
            throw new InvalidDataException("AppImage runtime input differs from the reviewed notice snapshot.");
    }
    else ValidateAppIdentity(index, args[1], args[2]);

    // Validate the complete snapshot and payload identity before creating any output.
    Directory.CreateDirectory(destination);
    foreach (var (relative, bytes) in sourceFiles)
    {
        string target = Path.Combine(destination, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.WriteAllBytes(target, bytes);
        if (Hash(File.ReadAllBytes(target)) != Hash(bytes)) throw new IOException("Copied notice bytes changed.");
    }
    Console.WriteLine($"Copied {sourceFiles.Count} verified dependency notice files.");
    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine(error.Message);
    return 1;
}

static void ValidateAppIdentity(JsonElement index, string lockPath, string payloadPath)
{
    var packages = new Dictionary<string, (string Version, string Hash)>(StringComparer.OrdinalIgnoreCase);
    foreach (var package in index.GetProperty("Packages").EnumerateArray())
        if (!packages.TryAdd(RequiredString(package, "ID"), (RequiredString(package, "Version"), RequiredString(package, "NuGetContentHash"))))
            throw new InvalidDataException("Duplicate notice package identity.");
    using var lockDocument = JsonDocument.Parse(File.ReadAllBytes(lockPath));
    var lockedPackages = new Dictionary<string, (string Version, string Hash)>(StringComparer.OrdinalIgnoreCase);
    foreach (var framework in lockDocument.RootElement.GetProperty("dependencies").EnumerateObject())
        foreach (var package in framework.Value.EnumerateObject())
        {
            if (RequiredString(package.Value, "type").Equals("Project", StringComparison.OrdinalIgnoreCase)) continue;
            var identity = (RequiredString(package.Value, "resolved"), RequiredString(package.Value, "contentHash"));
            if (lockedPackages.TryGetValue(package.Name, out var existing) && existing != identity)
                throw new InvalidDataException("Conflicting package identity across lock frameworks.");
            lockedPackages[package.Name] = identity;
        }
    if (packages.Count != lockedPackages.Count || packages.Any(p => !lockedPackages.TryGetValue(p.Key, out var identity) || identity != p.Value))
        throw new InvalidDataException("Notice package identities differ from the App dependency lock; regenerate and review the snapshot.");

    var runtimes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var runtime in index.GetProperty("RuntimePacks").EnumerateArray())
        if (!runtimes.Add(RequiredString(runtime, "ID") + "/" + RequiredString(runtime, "Version")))
            throw new InvalidDataException("Duplicate runtime notice identity.");
    using var depsDocument = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(payloadPath, "FileCat.deps.json")));
    foreach (var library in depsDocument.RootElement.GetProperty("libraries").EnumerateObject())
        if (library.Name.StartsWith("runtimepack.", StringComparison.OrdinalIgnoreCase) && !runtimes.Contains(library.Name["runtimepack.".Length..]))
            throw new InvalidDataException("Published runtime pack has no matching pinned notice snapshot.");

}

static string RequiredString(JsonElement value, string key) => value.GetProperty(key).GetString()
    ?? throw new InvalidDataException($"Missing notice identity field: {key}");
static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
static void ReadTree(string root, string relative, Dictionary<string, byte[]> files)
{
    var directory = new DirectoryInfo(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
    if ((directory.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Linked notice directory.");
    foreach (var entry in directory.EnumerateFileSystemInfos())
    {
        if ((entry.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Linked notice input.");
        string name = relative.Length == 0 ? entry.Name : relative + "/" + entry.Name;
        if (entry is DirectoryInfo) ReadTree(root, name, files);
        else if (!files.TryAdd(name, File.ReadAllBytes(entry.FullName))) throw new InvalidDataException("Duplicate notice file.");
    }
}
