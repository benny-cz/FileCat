using System.Security.Cryptography;
using FileCat.Archives.Writing;
using FileCat.Core.Archives;

namespace FileCat.Core.Tests;

public sealed class ArchiveZipRewriteStackTests
{
    // Each new payload's data descriptor came from a stackalloc inside the member loop, which is released only when the rebuild
    // returns: every added or replaced file grew the frame, so tens of thousands of them (requests admit 100,000 members)
    // exhausted a 1-1.5 MiB worker stack and killed the process. 20,000 files took about 640 KiB of descriptors; the rebuild now
    // finishes on a 256 KiB thread. Without the fix this test ends the test process, as a stack overflow cannot be caught.
    [Fact]
    public void Many_new_payloads_rebuild_in_constant_stack()
    {
        const int count = 20_000;
        var ct = TestContext.Current.CancellationToken;
        byte[] data = [0x5a]; string sha = Convert.ToHexString(SHA256.HashData(data));
        var inputs = Enumerable.Range(1, count).Select(id => new ArchiveTransformInput(id, 1, sha, new ArchiveTimestamp(1700000000, 0))).ToArray();
        var additions = Enumerable.Range(1, count).Select(id => new ArchiveTransformAddition($"f{id}.txt", ArchiveMemberType.File, id)).ToArray();
        var store = new ArchiveWriteProfile(ArchiveWriteFormat.Zip, compressionLevel: 3);
        using var stage = new MemoryStream(); ArchiveWriteManifest? manifest = null; Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { manifest = ZipRewriteEngine.Rebuild(null, null, store, stage, [], additions, inputs, _ => new MemoryStream(data, false), ct); }
            catch (Exception error) { failure = error; }
        }, 256 * 1024);
        thread.Start(); thread.Join();
        Assert.Null(failure); Assert.Equal(count, manifest!.Members.Count);
        Assert.Equal(count, StrictZipCatalog.Read(new MemoryStream(stage.ToArray(), false), ct).Members.Count);
    }
}
