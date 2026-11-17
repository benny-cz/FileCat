using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Text;

namespace FileCat.Core.FileSystem;

public enum ReparsePayloadState { Known, Unsupported, Malformed }
public sealed record WofBacking(uint Provider, uint? Algorithm);
public sealed record AppExecutionAlias(string Package, string AppId, string Executable, string AppType);
public sealed record ReparsePayload(uint Tag, Guid? Vendor, ReparsePayloadState State, ImmutableArray<byte> RawData)
{
    public StoredLinkTarget? Link { get; init; }
    public WofBacking? Wof { get; init; }
    public AppExecutionAlias? Alias { get; init; }
    public string? WslTarget { get; init; }
}

/// <summary>Bounded binary evidence, shared by records and details; descriptive payloads are never navigation paths.</summary>
public static class ReparsePayloadReader
{
    public static ReparsePayload Parse(ReadOnlySpan<byte> buffer)
    {
        uint tag = buffer.Length >= 4 ? BinaryPrimitives.ReadUInt32LittleEndian(buffer) : 0;
        int header = (tag & 0x80000000) != 0 ? 8 : 24;
        if (buffer.Length < header || buffer.Length > 16 * 1024) return new(tag, null, ReparsePayloadState.Malformed, []);
        Guid? vendor = header == 24 ? new Guid(buffer.Slice(8, 16)) : null;
        int length = BinaryPrimitives.ReadUInt16LittleEndian(buffer[4..]);
        if (length > buffer.Length - header) return new(tag, vendor, ReparsePayloadState.Malformed, []);
        return ParseData(tag, buffer.Slice(header, length)) with { Vendor = vendor };
    }

    public static ReparsePayload ParseData(uint tag, ReadOnlySpan<byte> data)
    {
        if (data.Length > 16 * 1024 - 8) return new(tag, null, ReparsePayloadState.Malformed, []);
        var result = new ReparsePayload(tag, null, ReparsePayloadState.Unsupported, data.ToArray().ToImmutableArray());
        try
        {
            if (tag is ReparseLinks.SymbolicLinkTag or ReparseLinks.MountPointTag)
            {
                var link = ReparsePresentation.ParseLink(tag, data);
                return result with { State = link is null ? ReparsePayloadState.Malformed : ReparsePayloadState.Known, Link = link };
            }
            if (tag == 0x80000017)
            {
                if (data.Length < 8) return result with { State = ReparsePayloadState.Malformed };
                if (BinaryPrimitives.ReadUInt32LittleEndian(data) != 1) return result;
                uint provider = BinaryPrimitives.ReadUInt32LittleEndian(data[4..]);
                if (provider == 1) return result with { State = ReparsePayloadState.Known, Wof = new(provider, null) };
                if (provider != 2) return result;
                if (data.Length < 16) return result with { State = ReparsePayloadState.Malformed };
                if (BinaryPrimitives.ReadUInt32LittleEndian(data[8..]) != 1) return result;
                return result with { State = ReparsePayloadState.Known, Wof = new(provider, BinaryPrimitives.ReadUInt32LittleEndian(data[12..])) };
            }
            if (tag == ReparseLinks.AppExecutionAliasTag)
            {
                if (data.Length < 4 || (data.Length & 1) != 0) return result with { State = ReparsePayloadState.Malformed };
                if (BinaryPrimitives.ReadUInt32LittleEndian(data) != 3) return result;
                string[] fields = new UnicodeEncoding(false, false, true).GetString(data[4..]).Split('\0');
                if (fields.Length != 5 || fields[^1].Length != 0) return result with { State = ReparsePayloadState.Malformed };
                return result with { State = ReparsePayloadState.Known, Alias = new(fields[0], fields[1], fields[2], fields[3]) };
            }
            if (tag == ReparseLinks.WslSymbolicLinkTag)
            {
                if (data.Length <= 4) return result with { State = ReparsePayloadState.Malformed };
                if (BinaryPrimitives.ReadUInt32LittleEndian(data) != 2) return result;
                string target = new UTF8Encoding(false, true).GetString(data[4..]);
                return target.Contains('\0') ? result with { State = ReparsePayloadState.Malformed }
                    : result with { State = ReparsePayloadState.Known, WslTarget = target };
            }
        }
        catch (DecoderFallbackException) { return result with { State = ReparsePayloadState.Malformed }; }
        return result;
    }
}
