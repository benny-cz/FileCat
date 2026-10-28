using System.Security.Cryptography;
using FileCat.Platform.Windows.Mtp;
using FileCat.Platform.Windows;

namespace FileCat.Platform.Windows.Tests;

public sealed class MtpUploadEvidenceTests
{
    private static PortableObject Object(string id, string name, long size) =>
        new(id, name, false, false, size, DateTime.UtcNow, true, false);

    [Fact]
    public void Created_object_requires_exact_readback_bytes()
    {
        byte[] source = "correct bytes"u8.ToArray();
        byte[] digest = SHA256.HashData(source);
        var objects = new Dictionary<string, (PortableObject Item, byte[] Bytes)>
        {
            ["created-A"] = (Object("created-A", "copy.bin", source.Length), source),
        };
        PortableObject? Get(string id) => objects.TryGetValue(id, out var value) ? value.Item : null;
        Stream Read(string id) => new MemoryStream(objects[id].Bytes);

        Assert.Equal("created-A", MtpUploadEvidence.Verify("created-A", "copy.bin", source.Length, digest, Get, Read, () => { }).Id);
        objects["created-A"] = (objects["created-A"].Item, "incorrect byt"u8.ToArray());
        Assert.Equal(source.Length, objects["created-A"].Bytes.Length);
        Assert.Throws<IOException>(() => MtpUploadEvidence.Verify("created-A", "copy.bin", source.Length, digest, Get, Read, () => { }));
        Assert.Throws<IOException>(() => MtpUploadEvidence.Verify(null, "copy.bin", source.Length, digest, Get, Read, () => { }));
    }

    [Fact]
    public void Failed_creation_never_deletes_a_peer_object_by_name()
    {
        var objects = new Dictionary<string, PortableObject>
        {
            ["peer-B"] = Object("peer-B", "copy.bin", 7),
            ["created-A"] = Object("created-A", "copy.bin", 7),
        };
        MtpUploadEvidence.CleanupCreated(null, id => objects.GetValueOrDefault(id), id => objects.Remove(id));
        Assert.True(objects.ContainsKey("peer-B"));
        MtpUploadEvidence.CleanupCreated("created-A", id => objects.GetValueOrDefault(id), id => objects.Remove(id));
        Assert.True(objects.ContainsKey("peer-B"));
        Assert.False(objects.ContainsKey("created-A"));
    }

    [Fact]
    public void Move_retires_only_verified_held_bytes()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Held file disposition is Windows-specific.");
        string root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "filecat-mtp-hold", Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            string path = Path.Combine(root, "source.bin");
            byte[] a = "original A"u8.ToArray();
            byte[] b = "different B"u8.ToArray();
            File.WriteAllBytes(path, a);
            using var original = File.OpenHandle(path);
            var originalId = ProtectedHexFile.Identity(original);
            original.Dispose();
            MtpMoveSafety.RetireVerifiedSource(path, SHA256.HashData(a), originalId);
            Assert.False(File.Exists(path));

            File.WriteAllBytes(path, a);
            using var originalAgain = File.OpenHandle(path);
            var originalAgainId = ProtectedHexFile.Identity(originalAgain);
            originalAgain.Dispose();
            string displaced = Path.Combine(root, "original.bin");
            File.Move(path, displaced);
            File.WriteAllBytes(path, b);
            Assert.Throws<IOException>(() => MtpMoveSafety.RetireVerifiedSource(path, SHA256.HashData(a), originalAgainId));
            Assert.Equal(b, File.ReadAllBytes(path));
            Assert.Equal(a, File.ReadAllBytes(displaced));

            // Equal replacement bytes still belong to a different file object and must survive.
            File.Delete(path);
            File.WriteAllBytes(path, a);
            Assert.Throws<IOException>(() => MtpMoveSafety.RetireVerifiedSource(path, SHA256.HashData(a), originalAgainId));
            Assert.Equal(a, File.ReadAllBytes(path));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
