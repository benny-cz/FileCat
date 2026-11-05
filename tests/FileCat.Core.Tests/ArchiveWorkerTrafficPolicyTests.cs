using System.Reflection;
using FileCat.Core.Archives;

namespace FileCat.Core.Tests;

public sealed class ArchiveWorkerTrafficPolicyTests
{
    [Fact]
    public void Fixed_IO_requests_do_not_spend_the_complete_semantic_metadata_budget()
    {
        var wire = new ArchiveWorkerProtocol(Guid.NewGuid(), ArchiveWritePolicy.MaxMetadataBytes + 1024);
        byte[] metadata = new byte[ArchiveWritePolicy.MaxControlFrameBytes];
        for (long offset = 0; offset < ArchiveWritePolicy.MaxMetadataBytes; offset += metadata.Length)
            wire.Write(Stream.Null, ArchiveWorkerMessage.Control, metadata);
        wire.Write(Stream.Null, ArchiveWorkerMessage.Read, new byte[16]);
        Assert.Equal(ArchiveWriteReason.ProtocolViolation, Assert.Throws<ArchiveWriteException>(() => wire.Write(Stream.Null, ArchiveWorkerMessage.Control, [0])).Reason);
        Assert.Throws<ArchiveWriteException>(() => wire.Write(Stream.Null, ArchiveWorkerMessage.Data, []));
    }

    [Fact]
    public void Zero_payload_frames_still_exhaust_the_finite_sequence_allowance_and_poison_the_channel()
    {
        var wire = new ArchiveWorkerProtocol(Guid.NewGuid(), 0);
        typeof(ArchiveWorkerProtocol).GetField("_next", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(wire, ArchiveWorkerProtocol.MaximumFrames - 1);
        wire.Write(Stream.Null, ArchiveWorkerMessage.Progress, []);
        Assert.Equal(ArchiveWriteReason.ProtocolViolation, Assert.Throws<ArchiveWriteException>(() => wire.Write(Stream.Null, ArchiveWorkerMessage.Progress, [])).Reason);
        Assert.Throws<ArchiveWriteException>(() => wire.Write(Stream.Null, ArchiveWorkerMessage.Completed, []));
    }
}
