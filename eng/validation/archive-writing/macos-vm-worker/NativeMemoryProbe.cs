using System.Buffers.Binary;
using System.Runtime.InteropServices;

internal static unsafe class NativeMemoryProbe
{
    internal static int Run(Stream output, int chunks, CancellationToken ct)
    {
        if (chunks is not (32 or 160)) return 64;
        output.Write("FCVMMEM1"u8); output.Flush();
        nint[] allocated = new nint[chunks];
        ulong state = 0x428292971357;
        byte[] progress = new byte[16]; "FCVMPAGE"u8.CopyTo(progress);
        try
        {
            for (int i = 0; i < chunks; i++)
            {
                ct.ThrowIfCancellationRequested();
                ulong* memory = (ulong*)NativeMemory.Alloc(8 << 20);
                allocated[i] = (nint)memory;
                // Dirty every word with changing nonzero entropy; do not count untouched virtual reservations.
                for (int word = 0; word < (8 << 20) / sizeof(ulong); word++)
                { state ^= state << 13; state ^= state >> 7; state ^= state << 17; memory[word] = state; }
                BinaryPrimitives.WriteUInt64LittleEndian(progress.AsSpan(8), (ulong)(i + 1) * (8 << 20));
                output.Write(progress); output.Flush();
            }
            output.Write("FCVMDONE"u8); output.Flush(); return 0;
        }
        finally { foreach (nint memory in allocated) if (memory != 0) NativeMemory.Free((void*)memory); }
    }
}
