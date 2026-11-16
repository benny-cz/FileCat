using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;

if (args.Length is not (3 or 4 or 5)) return 64;
bool artifactSbom = args.Length == 5 && args[4] == "--artifact-sbom";
if (args.Length == 5 && !artifactSbom) return 64;
string repository = Path.TrimEndingDirectorySeparator(Path.GetFullPath(args[0]));
string publish = Path.GetFullPath(args[1]), output = Path.GetFullPath(args[2]);
StringComparison pathComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
// Test assemblies can compile pinned NuGet content sources (e.g. xUnit's generated runner). This explicit extra
// root is opt-in; default production audits still require every nonembedded document inside the private checkout.
string? extraSourceRoot = args.Length >= 4 ? Path.TrimEndingDirectorySeparator(Path.GetFullPath(args[3])) : null;
if (!Directory.Exists(repository) || !Directory.Exists(publish) || Directory.Exists(output)) return 65;
if (extraSourceRoot != null && (!Directory.Exists(extraSourceRoot) || Path.GetPathRoot(extraSourceRoot) == extraSourceRoot)) return 65;
Directory.CreateDirectory(output);
Guid sha256 = new("8829d00f-11b8-4213-878b-770e8597ac16"), sha1 = new("ff1816ec-aa5e-4d10-87f7-6f4963833460");
Guid embeddedKind = new("0e8a571b-6926-466e-b4ad-8ab04611f5fe");
var documents = new List<object>(); var binaries = new List<object>();
long bytes = 0; int count = 0;
var symbols = Directory.EnumerateFiles(publish, "*.pdb").Where(path => artifactSbom
    ? Path.GetFileName(path) == "ArtifactSbom.pdb"
    : Path.GetFileName(path) == "FileCat.pdb" || Path.GetFileName(path).StartsWith("FileCat.", StringComparison.Ordinal)).Order(StringComparer.Ordinal).ToArray();
if (!artifactSbom && File.Exists(Path.Combine(publish, "FileCat.dll")) && !symbols.Any(path => Path.GetFileName(path) == "FileCat.pdb"))
{ Console.Error.WriteLine("The application binary requires its exact compiler symbols."); return 65; }
foreach (string pdb in symbols)
{
    string name = Path.GetFileNameWithoutExtension(pdb), dll = Path.ChangeExtension(pdb, ".dll");
    using var stream = File.OpenRead(pdb);
    using var provider = MetadataReaderProvider.FromPortablePdbStream(stream);
    MetadataReader reader = provider.GetMetadataReader();
    var id = new BlobContentId((reader.DebugMetadataHeader ?? throw new InvalidDataException("Portable compiler symbols have no identity.")).Id);
    using var assemblyStream = File.OpenRead(dll);
    using var assembly = new PEReader(assemblyStream);
    var codeViews = assembly.ReadDebugDirectory().Where(entry => entry.Type == DebugDirectoryEntryType.CodeView).ToArray();
    if (codeViews.Length != 1) { Console.Error.WriteLine("The assembly has no unique compiler-symbol identity: " + name); return 65; }
    var codeView = assembly.ReadCodeViewDebugDirectoryData(codeViews[0]);
    if (codeView.Guid != id.Guid || codeViews[0].Stamp != id.Stamp || codeView.Age != 1)
    { Console.Error.WriteLine("Compiler symbols do not belong to the assembly: " + name); return 65; }
    binaries.Add(new { assembly = name, pdbBoundToAssembly = true, pdbSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(pdb))),
        dllSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(dll))) });
    foreach (DocumentHandle handle in reader.Documents)
    {
        if (++count > 8192) throw new InvalidDataException("Compiler document count exceeds the audit bound.");
        Document document = reader.GetDocument(handle);
        string path = reader.GetString(document.Name); byte[] expected = reader.GetBlobBytes(document.Hash);
        Guid algorithm = reader.GetGuid(document.HashAlgorithm); byte[]? content = null;
        foreach (CustomDebugInformationHandle infoHandle in reader.GetCustomDebugInformation(handle))
        {
            CustomDebugInformation info = reader.GetCustomDebugInformation(infoHandle);
            if (reader.GetGuid(info.Kind) != embeddedKind) continue;
            byte[] blob = reader.GetBlobBytes(info.Value);
            if (blob.Length < 4 || blob.Length > 8 << 20) throw new InvalidDataException("Embedded source exceeds the audit bound.");
            int length = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(blob);
            if (length < 0 || length > 8 << 20) throw new InvalidDataException("Embedded source expansion exceeds the audit bound.");
            if (length == 0) content = blob[4..];
            else
            {
                using var encoded = new MemoryStream(blob, 4, blob.Length - 4);
                using var decoder = new DeflateStream(encoded, CompressionMode.Decompress);
                content = new byte[length]; decoder.ReadExactly(content);
                if (decoder.ReadByte() != -1) throw new InvalidDataException("Embedded source length differs.");
            }
        }
        bool embedded = content != null;
        if (content == null)
        {
            string full = Path.GetFullPath(path);
            bool permitted = full.StartsWith(repository + Path.DirectorySeparatorChar, pathComparison) ||
                extraSourceRoot != null && full.StartsWith(extraSourceRoot + Path.DirectorySeparatorChar, pathComparison);
            if (!permitted ||
                !File.Exists(full) || new FileInfo(full).Length > 8 << 20)
                throw new InvalidDataException("Compiler source cannot be verified inside the private checkout: " + path);
            content = File.ReadAllBytes(full);
        }
        bytes = checked(bytes + content.LongLength);
        if (bytes > 256L << 20) throw new InvalidDataException("Source snapshot exceeds the audit bound.");
        byte[] digest = algorithm == sha256 ? SHA256.HashData(content) : algorithm == sha1 ? SHA1.HashData(content) :
            throw new InvalidDataException("Unknown compiler document checksum algorithm.");
        if (!digest.AsSpan().SequenceEqual(expected)) throw new InvalidDataException("Compiler source checksum differs: " + path);
        string snapshot = $"{count:D5}.source";
        File.WriteAllBytes(Path.Combine(output, snapshot), content);
        documents.Add(new { assembly = name, document = path, embedded, snapshot, algorithm, checksum = Convert.ToHexString(expected),
            bytes = content.Length, sha256 = Convert.ToHexString(SHA256.HashData(content)) });
    }
}
if (binaries.Count < (artifactSbom ? 1 : 4) || count == 0) throw new InvalidDataException("Requested compiler symbols are incomplete.");
var result = new { compilerSourceChecksumsMatched = true, productionBackendQualified = false, documents = count, bytes, extraSourceRoot,
    scope = artifactSbom ? "ArtifactSbom" : "FileCat", binaries, sources = documents };
File.WriteAllText(Path.Combine(output, "facts.json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }) + "\n");
Console.WriteLine(JsonSerializer.Serialize(new { compilerSourceChecksumsMatched = true, productionBackendQualified = false, documents = count, bytes }));
return 0;
