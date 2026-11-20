using System;
using System.Buffers.Binary;
using System.IO;

namespace SharpCompress.Compressors.Filters;

// ARM64 and RISC-V expose a four-byte branch offset in the 7z codec contract. The older branch codecs do not.
internal static class BranchFilterProperties
{
    internal static uint InitialPosition(byte[]? properties, uint alignmentMask)
    {
        if (properties is null || properties.Length == 0) return 0;
        if (properties.Length != 4) throw new InvalidDataException("The branch offset needs four bytes.");
        uint value = BinaryPrimitives.ReadUInt32LittleEndian(properties);
        if ((value & alignmentMask) != 0) throw new InvalidDataException("The branch offset is not instruction-aligned.");
        return value;
    }
}
