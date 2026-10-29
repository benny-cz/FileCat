using System;
using System.Buffers.Binary;

namespace SharpCompress.Compressors.PPMd;

/// <summary>
/// FileCat: the coder properties of 7z's PPMd (variant H): the model order, then the model memory as a little-endian
/// 32-bit number. Upstream also read ZIP's two-byte PPMd variant I properties and offered them for compression; FileCat's
/// build keeps 7z decoding only and refuses any other form.
/// </summary>
public class PpmdProperties
{
    public PpmdProperties(byte[] properties)
        : this(properties.AsSpan()) { }

    public PpmdProperties(ReadOnlySpan<byte> properties)
    {
        if (properties.Length != 5)
        {
            throw new NotSupportedException(
                $"Only 7z's PPMd (variant H, five property bytes) is supported; these properties have {properties.Length} bytes."
            );
        }
        ModelOrder = properties[0];
        AllocatorSize = BinaryPrimitives.ReadInt32LittleEndian(properties.Slice(1));
    }

    public int ModelOrder { get; }

    public int AllocatorSize { get; }

    public PpmdVersion Version => PpmdVersion.H7Z;
}
