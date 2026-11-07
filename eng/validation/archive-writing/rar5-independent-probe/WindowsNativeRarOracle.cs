// Validation only. Calls the disposable Windows guest's system libarchive directly, using its UTF-8 metadata API.
// No extraction or production dependency; bypasses bsdtar's narrow-locale filename presentation limitation.
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

public sealed class NativeRarMember
{
    public string Name;
    public bool Directory;
    public long Length;
    public uint Mode;
    public long Seconds;
    public int Nanoseconds;
    public string Sha256;
}

public static class WindowsNativeRarOracle
{
    private const string Library = @"C:\Windows\System32\archiveint.dll";
    [DllImport(Library, CallingConvention=CallingConvention.Cdecl)] private static extern IntPtr archive_read_new();
    [DllImport(Library, CallingConvention=CallingConvention.Cdecl)] private static extern int archive_read_support_format_rar5(IntPtr archive);
    [DllImport(Library, CallingConvention=CallingConvention.Cdecl)] private static extern int archive_read_open_filename(IntPtr archive, byte[] path, UIntPtr block);
    [DllImport(Library, CallingConvention=CallingConvention.Cdecl)] private static extern int archive_read_next_header(IntPtr archive, out IntPtr entry);
    [DllImport(Library, CallingConvention=CallingConvention.Cdecl)] private static extern IntPtr archive_read_data(IntPtr archive, [Out] byte[] buffer, UIntPtr length);
    [DllImport(Library, CallingConvention=CallingConvention.Cdecl)] private static extern int archive_read_free(IntPtr archive);
    [DllImport(Library, CallingConvention=CallingConvention.Cdecl)] private static extern IntPtr archive_error_string(IntPtr archive);
    [DllImport(Library, CallingConvention=CallingConvention.Cdecl)] private static extern IntPtr archive_entry_pathname_utf8(IntPtr entry);
    [DllImport(Library, CallingConvention=CallingConvention.Cdecl)] private static extern long archive_entry_size(IntPtr entry);
    [DllImport(Library, CallingConvention=CallingConvention.Cdecl)] private static extern uint archive_entry_filetype(IntPtr entry);
    [DllImport(Library, CallingConvention=CallingConvention.Cdecl)] private static extern uint archive_entry_perm(IntPtr entry);
    [DllImport(Library, CallingConvention=CallingConvention.Cdecl)] private static extern long archive_entry_mtime(IntPtr entry);
    [DllImport(Library, CallingConvention=CallingConvention.Cdecl)] private static extern int archive_entry_mtime_nsec(IntPtr entry);
    [DllImport(Library, CallingConvention=CallingConvention.Cdecl)] private static extern IntPtr archive_version_string();

    public static string Version() { return Utf8(archive_version_string()); }

    public static NativeRarMember[] Read(string path)
    {
        if (!Path.IsPathRooted(path) || new FileInfo(path).Length > 16 << 20) throw new InvalidDataException("Fixture path/size exceeds scope.");
        IntPtr archive = archive_read_new();
        if (archive == IntPtr.Zero) throw new InvalidDataException("Native reader did not allocate.");
        var result = new List<NativeRarMember>(); var buffer = new byte[65536];
        try
        {
            Check(archive, archive_read_support_format_rar5(archive));
            Check(archive, archive_read_open_filename(archive, Encoding.UTF8.GetBytes(path + "\0"), (UIntPtr)65536));
            while (true)
            {
                IntPtr entry; int status = archive_read_next_header(archive, out entry);
                if (status == 1) break;
                Check(archive, status);
                if (result.Count >= 14) throw new InvalidDataException("Unexpected native member count.");
                uint type = archive_entry_filetype(entry) & 0xf000;
                if (type != 0x4000 && type != 0x8000) throw new InvalidDataException("Unexpected native entry type.");
                long length = archive_entry_size(entry), count = 0;
                if (length < 0 || length > 1 << 20) throw new InvalidDataException("Unexpected native payload size.");
                var member = new NativeRarMember { Name = Utf8(archive_entry_pathname_utf8(entry)), Directory = type == 0x4000,
                    Length = length, Mode = archive_entry_perm(entry), Seconds = archive_entry_mtime(entry), Nanoseconds = archive_entry_mtime_nsec(entry) };
                using (var hash = SHA256.Create())
                {
                    while (!member.Directory)
                    {
                        long got = archive_read_data(archive, buffer, (UIntPtr)buffer.Length).ToInt64();
                        if (got < 0) throw new InvalidDataException("Native content error: " + Utf8(archive_error_string(archive)));
                        if (got == 0) break;
                        if (got > buffer.Length || got > length - count) throw new InvalidDataException("Native content exceeds declared size.");
                        hash.TransformBlock(buffer, 0, (int)got, null, 0); count += got;
                    }
                    hash.TransformFinalBlock(new byte[0], 0, 0);
                    member.Sha256 = BitConverter.ToString(hash.Hash).Replace("-", "");
                }
                if (count != length) throw new InvalidDataException("Native content length differs.");
                result.Add(member);
            }
        }
        finally { if (archive_read_free(archive) != 0) throw new InvalidDataException("Native reader cleanup failed."); }
        return result.ToArray();
    }

    private static void Check(IntPtr archive, int status)
    {
        if (status != 0) throw new InvalidDataException("Native header error: " + Utf8(archive_error_string(archive)));
    }

    private static string Utf8(IntPtr pointer)
    {
        if (pointer == IntPtr.Zero) throw new InvalidDataException("Missing native UTF-8 text.");
        int size = 0;
        while (size < 128000 && Marshal.ReadByte(pointer, size) != 0) size++;
        if (size == 128000) throw new InvalidDataException("Native text exceeds bound.");
        byte[] data = new byte[size]; Marshal.Copy(pointer, data, 0, size);
        return new UTF8Encoding(false, true).GetString(data);
    }
}
