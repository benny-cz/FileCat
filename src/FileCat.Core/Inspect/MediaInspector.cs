using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using FileCat.Core.Resources;

namespace FileCat.Core.Inspect;

/// <summary>
/// Audio and video containers (plan §16.1, P8): MP4/MOV (ISO base media), Matroska/WebM, MP3 (ID3 and frame header),
/// FLAC, WAV, AVI, and Ogg. Duration, tracks, codecs, dimensions, sample rates, and tags, from bounded header reads.
/// Nothing is decoded or played.
/// </summary>
public static class MediaInspector
{
    private const int MaxBoxes = 10_000;
    private const int MaxMovie = 16 * 1024 * 1024;

    public static InspectionReport? Inspect(IContentSource source, CancellationToken ct)
    {
        var r = new ContentReader(source);
        var h = r.Read(0, 64);
        if (h.Length < 12) return null;
        var s = h.AsSpan();
        if (s.Slice(4, 4).SequenceEqual("ftyp"u8)) return IsoMedia(r, ct);
        if (s[..4].SequenceEqual((ReadOnlySpan<byte>)[0x1A, 0x45, 0xDF, 0xA3])) return Matroska(r, ct);
        if (s[..4].SequenceEqual("fLaC"u8)) return Flac(r);
        if (s[..4].SequenceEqual("RIFF"u8) && s.Slice(8, 4).SequenceEqual("WAVE"u8)) return Wave(r);
        if (s[..4].SequenceEqual("RIFF"u8) && s.Slice(8, 4).SequenceEqual("AVI "u8)) return Avi(r);
        if (s[..4].SequenceEqual("OggS"u8)) return Ogg(r);
        if (s[..3].SequenceEqual("ID3"u8) || s[0] == 0xFF && (s[1] & 0xE0) == 0xE0 && Mpeg(s) is not null) return Mp3(r);
        return null;
    }

    private static string Duration(double seconds) =>
        seconds <= 0 || double.IsNaN(seconds) || double.IsInfinity(seconds) ? "unknown"
            : TimeSpan.FromSeconds(Math.Min(seconds, TimeSpan.MaxValue.TotalSeconds - 1)) is var t && t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes}:{t.Seconds:00}.{t.Milliseconds / 100}";

    private static string Printable(string s) => new(s.Where(c => c != '\0').Select(c => char.IsControl(c) ? '?' : c).ToArray());

    // ---- MP4 / MOV ----------------------------------------------------------------------------------------------

    private static InspectionReport IsoMedia(ContentReader r, CancellationToken ct)
    {
        var warnings = new List<string>();
        var ftyp = r.Read(8, 8);
        string brand = Printable(Encoding.ASCII.GetString(ftyp, 0, Math.Min(4, ftyp.Length))).Trim();
        var general = new List<(string, string)> { ("Brand", brand) };
        var tracks = new List<(string, string)>();
        // Top-level boxes: find moov (often at the end) without reading media data.
        long at = 0;
        byte[]? moov = null;
        for (int i = 0; i < MaxBoxes && at + 8 <= r.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            var bh = r.Read(at, 16);
            if (bh.Length < 8) break;
            long size = BinaryPrimitives.ReadUInt32BigEndian(bh);
            string type = Encoding.ASCII.GetString(bh, 4, 4);
            int headerLength = 8;
            if (size == 1 && bh.Length >= 16)
            {
                size = (long)Math.Min(BinaryPrimitives.ReadUInt64BigEndian(bh.AsSpan(8)), long.MaxValue);
                headerLength = 16;
            }
            else if (size == 0) size = r.Length - at;
            if (size < headerLength) break;
            if (type == "moov")
            {
                if (size > MaxMovie) warnings.Add("The movie header is unusually large; only its beginning is read.");
                moov = r.Read(at + headerLength, (int)Math.Min(size - headerLength, MaxMovie));
                break;
            }
            at += size;
        }
        if (moov is null)
        {
            warnings.Add("No movie header (moov) was found: the file is incomplete or fragmented.");
            return new InspectionReport($"MP4 / QuickTime ({brand})", [new InspectionSection("General", general)], warnings);
        }
        foreach (var (type, box) in Boxes(moov))
        {
            if (type == "mvhd" && box.Length >= 20)
            {
                bool v1 = box[0] == 1;
                uint scale = BinaryPrimitives.ReadUInt32BigEndian(box.AsSpan(v1 ? 20 : 12));
                ulong duration = v1 && box.Length >= 32 ? BinaryPrimitives.ReadUInt64BigEndian(box.AsSpan(24)) : BinaryPrimitives.ReadUInt32BigEndian(box.AsSpan(16));
                if (scale > 0) general.Add(("Duration", Duration(duration / (double)scale)));
            }
            else if (type == "trak") tracks.Add(Track(box));
            else if (type == "udta") general.AddRange(Tags(box));
        }
        var sections = new List<InspectionSection> { new("General", general) };
        if (tracks.Count > 0) sections.Add(new InspectionSection($"Tracks ({tracks.Count})", tracks));
        return new InspectionReport($"MP4 / QuickTime video ({brand})", sections, warnings);
    }

    private static (string, string) Track(byte[] trak)
    {
        string handler = "track", codec = "?", details = string.Empty;
        int width = 0, height = 0;
        foreach (var (type, box) in Boxes(trak))
        {
            if (type == "tkhd" && box.Length >= 84)
            {
                int o = box[0] == 1 ? 88 : 76;
                if (box.Length >= o + 8)
                {
                    width = (int)(BinaryPrimitives.ReadUInt32BigEndian(box.AsSpan(o)) >> 16);
                    height = (int)(BinaryPrimitives.ReadUInt32BigEndian(box.AsSpan(o + 4)) >> 16);
                }
            }
            else if (type == "mdia")
            {
                foreach (var (t2, b2) in Boxes(box))
                {
                    if (t2 == "hdlr" && b2.Length >= 12) handler = Encoding.ASCII.GetString(b2, 8, 4);
                    else if (t2 == "minf")
                        foreach (var (t3, b3) in Boxes(b2))
                            if (t3 == "stbl")
                                foreach (var (t4, b4) in Boxes(b3))
                                    if (t4 == "stsd" && b4.Length >= 16)
                                    {
                                        codec = Printable(Encoding.ASCII.GetString(b4, 12, 4));
                                        if (handler == "soun" && b4.Length >= 16 + 28)
                                        {
                                            int channels = BinaryPrimitives.ReadUInt16BigEndian(b4.AsSpan(16 + 16));
                                            uint rate = BinaryPrimitives.ReadUInt32BigEndian(b4.AsSpan(16 + 24)) >> 16;
                                            details = $"{channels} channel{(channels == 1 ? "" : "s")}, {rate:N0} Hz";
                                        }
                                    }
                }
            }
        }
        string kind = handler switch { "vide" => "Video", "soun" => "Audio", "text" or "sbtl" or "subt" => "Subtitles", "hint" => "Hint", "meta" => "Metadata", _ => handler };
        if (handler == "vide" && width > 0) details = $"{width} × {height}";
        return (kind, $"{CodecName(codec)}{(details.Length > 0 ? ", " + details : "")}");
    }

    private static string CodecName(string fourcc) => fourcc switch
    {
        "avc1" or "avc3" => "H.264 (avc1)",
        "hvc1" or "hev1" => "H.265/HEVC",
        "av01" => "AV1",
        "vp09" => "VP9",
        "mp4a" => "AAC (mp4a)",
        "Opus" => "Opus",
        "fLaC" => "FLAC",
        "ac-3" => "AC-3",
        "ec-3" => "E-AC-3",
        "alac" => "Apple Lossless",
        "tx3g" => "Timed text",
        _ => fourcc,
    };

    /// <summary>iTunes-style tags (©nam, ©ART, ©alb, ©day) inside udta/meta/ilst.</summary>
    private static IEnumerable<(string, string)> Tags(byte[] udta)
    {
        foreach (var (type, box) in Boxes(udta))
        {
            if (type != "meta" || box.Length < 4) continue;
            foreach (var (t2, b2) in Boxes(box[4..]))
            {
                if (t2 != "ilst") continue;
                foreach (var (tag, item) in Boxes(b2))
                {
                    string? name = tag switch { "©nam" => "Title", "©ART" => "Artist", "©alb" => "Album", "©day" => "Year", "©too" => "Encoder", _ => null };
                    if (name is null) continue;
                    foreach (var (t3, data) in Boxes(item))
                        if (t3 == "data" && data.Length > 8) yield return (name, Printable(Encoding.UTF8.GetString(data, 8, Math.Min(data.Length - 8, 200))));
                }
            }
        }
    }

    /// <summary>The child boxes of a box's payload (type, payload), bounded and tolerant of damage.</summary>
    private static List<(string Type, byte[] Payload)> Boxes(byte[] payload)
    {
        var list = new List<(string, byte[])>();
        int at = 0;
        while (at + 8 <= payload.Length && list.Count < MaxBoxes)
        {
            long size = BinaryPrimitives.ReadUInt32BigEndian(payload.AsSpan(at));
            int header = 8;
            if (size == 1 && at + 16 <= payload.Length)
            {
                size = (long)Math.Min(BinaryPrimitives.ReadUInt64BigEndian(payload.AsSpan(at + 8)), int.MaxValue);
                header = 16;
            }
            else if (size == 0) size = payload.Length - at;
            if (size < header || at + size > payload.Length) break;
            string type = Encoding.Latin1.GetString(payload, at + 4, 4);
            list.Add((type, payload[(at + header)..(int)(at + size)]));
            at += (int)size;
        }
        return list;
    }

    // ---- Matroska / WebM ----------------------------------------------------------------------------------------

    private static InspectionReport Matroska(ContentReader r, CancellationToken ct)
    {
        var data = r.Read(0, 4 * 1024 * 1024);
        var general = new List<(string, string)>();
        var tracks = new List<(string, string)>();
        string docType = "matroska";
        double scale = 1_000_000, duration = 0;
        int at = 0;
        // EBML header, then the Segment's children (read flat: Info and Tracks usually come first).
        void Walk(int start, int end, int depth)
        {
            int pos = start;
            int guard = 0;
            while (pos < end && guard++ < 50_000)
            {
                ct.ThrowIfCancellationRequested();
                if (!ReadId(data, ref pos, out uint id) || !ReadSize(data, ref pos, out long size)) return;
                long stop = size < 0 ? end : Math.Min(end, pos + size);
                int bodyEnd = (int)Math.Min(stop, data.Length);
                switch (id)
                {
                    case 0x1A45DFA3: Walk(pos, bodyEnd, depth + 1); break; // EBML header
                    case 0x4282: docType = Str(data, pos, bodyEnd); break;
                    case 0x18538067: Walk(pos, bodyEnd, depth + 1); return; // Segment: its children
                    case 0x1549A966: Walk(pos, bodyEnd, depth + 1); break; // Info
                    case 0x2AD7B1: scale = UInt(data, pos, bodyEnd); break; // TimestampScale
                    case 0x4489: duration = Float(data, pos, bodyEnd); break;
                    case 0x4D80: general.Add(("Muxing application", Str(data, pos, bodyEnd))); break;
                    case 0x5741: general.Add(("Writing application", Str(data, pos, bodyEnd))); break;
                    case 0x7BA9: general.Add(("Title", Str(data, pos, bodyEnd))); break;
                    case 0x1654AE6B: Walk(pos, bodyEnd, depth + 1); break; // Tracks
                    case 0xAE: tracks.Add(MatroskaTrack(pos, bodyEnd)); break; // TrackEntry
                    case 0x1F43B675: return; // Cluster: media data from here on
                }
                if (size < 0 || stop > data.Length) return;
                pos = (int)stop;
            }
        }
        (string, string) MatroskaTrack(int start, int end)
        {
            int pos = start;
            string codec = "?", kind = "Track", language = string.Empty;
            string details = string.Empty;
            while (pos < end)
            {
                if (!ReadId(data, ref pos, out uint id) || !ReadSize(data, ref pos, out long size) || size < 0 || pos + size > end) break;
                int e = (int)(pos + size);
                switch (id)
                {
                    case 0x83: kind = UInt(data, pos, e) switch { 1 => "Video", 2 => "Audio", 17 => "Subtitles", 0x10 => "Logo", _ => "Track" }; break;
                    case 0x86: codec = Str(data, pos, e); break;
                    case 0x22B59C: language = Str(data, pos, e); break;
                    case 0xE0: // Video
                        int vp = pos;
                        long w = 0, hgt = 0;
                        while (vp < e && ReadId(data, ref vp, out uint vid) && ReadSize(data, ref vp, out long vs) && vs >= 0 && vp + vs <= e)
                        {
                            if (vid == 0xB0) w = UInt(data, vp, (int)(vp + vs));
                            else if (vid == 0xBA) hgt = UInt(data, vp, (int)(vp + vs));
                            vp += (int)vs;
                        }
                        if (w > 0) details = $"{w} × {hgt}";
                        break;
                    case 0xE1: // Audio
                        int ap = pos;
                        double rate = 0;
                        long channels = 0;
                        while (ap < e && ReadId(data, ref ap, out uint aid) && ReadSize(data, ref ap, out long asz) && asz >= 0 && ap + asz <= e)
                        {
                            if (aid == 0xB5) rate = Float(data, ap, (int)(ap + asz));
                            else if (aid == 0x9F) channels = UInt(data, ap, (int)(ap + asz));
                            ap += (int)asz;
                        }
                        details = $"{(channels > 0 ? channels : 1)} channel{(channels == 1 ? "" : "s")}, {rate:N0} Hz";
                        break;
                }
                pos = e;
            }
            return (kind, $"{codec}{(details.Length > 0 ? ", " + details : "")}{(language is { Length: > 0 } and not "und" ? $" [{language}]" : "")}");
        }
        Walk(at, data.Length, 0);
        if (duration > 0) general.Insert(0, ("Duration", Duration(duration * scale / 1e9)));
        var sections = new List<InspectionSection> { new("General", general) };
        if (tracks.Count > 0) sections.Add(new InspectionSection($"Tracks ({tracks.Count})", tracks));
        return new InspectionReport(docType == "webm" ? "WebM video" : "Matroska video", sections, []);
    }

    private static bool ReadId(byte[] d, ref int pos, out uint id)
    {
        id = 0;
        if (pos >= d.Length) return false;
        byte first = d[pos];
        int length = first >= 0x80 ? 1 : first >= 0x40 ? 2 : first >= 0x20 ? 3 : first >= 0x10 ? 4 : 0;
        if (length == 0 || pos + length > d.Length) return false;
        for (int i = 0; i < length; i++) id = (id << 8) | d[pos + i];
        pos += length;
        return true;
    }

    private static bool ReadSize(byte[] d, ref int pos, out long size)
    {
        size = 0;
        if (pos >= d.Length) return false;
        byte first = d[pos];
        int length = 1;
        while (length <= 8 && (first & (0x80 >> (length - 1))) == 0) length++;
        if (length > 8 || pos + length > d.Length) return false;
        long value = first & (0xFF >> length);
        bool unknown = value == (0xFF >> length);
        for (int i = 1; i < length; i++)
        {
            value = (value << 8) | d[pos + i];
            unknown &= d[pos + i] == 0xFF;
        }
        pos += length;
        size = unknown ? -1 : value;
        return true;
    }

    private static long UInt(byte[] d, int start, int end)
    {
        long v = 0;
        for (int i = start; i < end && i < start + 8 && i < d.Length; i++) v = (v << 8) | d[i];
        return v;
    }

    private static double Float(byte[] d, int start, int end) => (end - start) switch
    {
        4 when start + 4 <= d.Length => BinaryPrimitives.ReadSingleBigEndian(d.AsSpan(start)),
        8 when start + 8 <= d.Length => BinaryPrimitives.ReadDoubleBigEndian(d.AsSpan(start)),
        _ => 0,
    };

    private static string Str(byte[] d, int start, int end) =>
        start >= d.Length || end <= start ? string.Empty : Printable(Encoding.UTF8.GetString(d, start, Math.Min(Math.Min(end, d.Length) - start, 200)));

    // ---- FLAC, WAV, AVI, Ogg --------------------------------------------------------------------------------------

    private static InspectionReport Flac(ContentReader r)
    {
        var general = new List<(string, string)>();
        var tags = new List<(string, string)>();
        long at = 4;
        for (int i = 0; i < 64; i++)
        {
            var h = r.Read(at, 4);
            if (h.Length < 4) break;
            bool last = (h[0] & 0x80) != 0;
            int type = h[0] & 0x7F, length = (h[1] << 16) | (h[2] << 8) | h[3];
            if (type == 0 && length >= 18)
            {
                var b = r.Read(at + 4, 18);
                if (b.Length == 18)
                {
                    int rate = (b[10] << 12) | (b[11] << 4) | (b[12] >> 4);
                    int channels = ((b[12] >> 1) & 7) + 1, bits = (((b[12] & 1) << 4) | (b[13] >> 4)) + 1;
                    long samples = ((long)(b[13] & 0xF) << 32) | BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(14));
                    general.Add(("Duration", rate > 0 ? Duration(samples / (double)rate) : "unknown"));
                    general.Add(("Audio", $"{channels} channel{(channels == 1 ? "" : "s")}, {rate:N0} Hz, {bits}-bit"));
                }
            }
            else if (type == 4) tags.AddRange(VorbisComments(r.Read(at + 4, Math.Min(length, 256 * 1024)), 0));
            if (last) break;
            at += 4 + length;
        }
        var sections = new List<InspectionSection> { new("General", general) };
        if (tags.Count > 0) sections.Add(new InspectionSection("Tags", tags));
        return new InspectionReport("FLAC audio", sections, []);
    }

    /// <summary>Vorbis comments (FLAC, Ogg Vorbis, Opus): vendor, then NAME=value pairs.</summary>
    private static List<(string, string)> VorbisComments(byte[] b, int at)
    {
        var list = new List<(string, string)>();
        if (at + 4 > b.Length) return list;
        long vendor = BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(at));
        if (at + 4L + vendor + 4 > b.Length) return list;
        at += 4 + (int)vendor;
        uint count = BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(at));
        at += 4;
        for (uint i = 0; i < count && at + 4 <= b.Length && list.Count < 20; i++)
        {
            int length = (int)Math.Min(BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(at)), int.MaxValue);
            at += 4;
            if (length < 0 || (long)at + length > b.Length) break;
            var text = Encoding.UTF8.GetString(b, at, length);
            at += length;
            int eq = text.IndexOf('=');
            if (eq <= 0) continue;
            string key = text[..eq].ToUpperInvariant();
            string? name = key switch { "TITLE" => "Title", "ARTIST" => "Artist", "ALBUM" => "Album", "DATE" => "Date", "TRACKNUMBER" => "Track", "GENRE" => "Genre", _ => null };
            if (name is not null) list.Add((name, Printable(text[(eq + 1)..].Length > 200 ? text[(eq + 1)..][..200] : text[(eq + 1)..])));
        }
        return list;
    }

    private static InspectionReport Wave(ContentReader r)
    {
        var general = new List<(string, string)>();
        long at = 12;
        int rate = 0, channels = 0, bits = 0, blockAlign = 0;
        for (int i = 0; i < 64; i++)
        {
            var h = r.Read(at, 8);
            if (h.Length < 8) break;
            string id = Encoding.ASCII.GetString(h, 0, 4);
            long size = BinaryPrimitives.ReadUInt32LittleEndian(h.AsSpan(4));
            if (id == "fmt ")
            {
                var f = r.Read(at + 8, 16);
                if (f.Length == 16)
                {
                    ushort format = BinaryPrimitives.ReadUInt16LittleEndian(f);
                    channels = BinaryPrimitives.ReadUInt16LittleEndian(f.AsSpan(2));
                    rate = (int)BinaryPrimitives.ReadUInt32LittleEndian(f.AsSpan(4));
                    blockAlign = BinaryPrimitives.ReadUInt16LittleEndian(f.AsSpan(12));
                    bits = BinaryPrimitives.ReadUInt16LittleEndian(f.AsSpan(14));
                    general.Add(("Encoding", format switch { 1 => "PCM", 3 => "IEEE float", 6 => "A-law", 7 => "µ-law", 0xFFFE => "extensible", 0x55 => "MP3", _ => $"format 0x{format:X}" }));
                    general.Add(("Audio", $"{channels} channel{(channels == 1 ? "" : "s")}, {rate:N0} Hz, {bits}-bit"));
                }
            }
            else if (id == "data" && blockAlign > 0 && rate > 0)
                general.Insert(0, ("Duration", Duration(size / (double)blockAlign / rate)));
            at += 8 + size + (size & 1);
        }
        return new InspectionReport("WAV audio", [new InspectionSection("General", general)], []);
    }

    private static InspectionReport Avi(ContentReader r)
    {
        var general = new List<(string, string)>();
        var head = r.Read(0, 64 * 1024);
        int avih = head.AsSpan().IndexOf("avih"u8);
        if (avih >= 0 && avih + 8 + 40 <= head.Length)
        {
            var b = head.AsSpan(avih + 8);
            uint usPerFrame = BinaryPrimitives.ReadUInt32LittleEndian(b), frames = BinaryPrimitives.ReadUInt32LittleEndian(b[16..]);
            uint width = BinaryPrimitives.ReadUInt32LittleEndian(b[32..]), height = BinaryPrimitives.ReadUInt32LittleEndian(b[36..]);
            general.Add(("Duration", Duration(frames * (usPerFrame / 1e6))));
            general.Add(("Dimensions", $"{width} × {height}"));
            if (usPerFrame > 0) general.Add(("Frame rate", $"{1e6 / usPerFrame:0.##} fps"));
        }
        int vids = head.AsSpan().IndexOf("vids"u8);
        if (vids >= 0 && vids + 8 <= head.Length) general.Add(("Video codec", Printable(Encoding.ASCII.GetString(head, vids + 4, 4))));
        return new InspectionReport("AVI video", [new InspectionSection("General", general)], []);
    }

    private static InspectionReport Ogg(ContentReader r)
    {
        var general = new List<(string, string)>();
        var first = r.Read(0, 64 * 1024);
        if (first.Length < 28) return new InspectionReport("Ogg", [], ["The file is too short."]);
        int segments = first[26];
        int packet = 27 + segments;
        string codec = "unknown";
        int rate = 0;
        if (packet + 8 <= first.Length)
        {
            var p = first.AsSpan(packet);
            if (p.Length >= 30 && p[0] == 1 && p[1..7].SequenceEqual("vorbis"u8))
            {
                codec = "Vorbis";
                general.Add(("Audio", $"{p[11]} channel{(p[11] == 1 ? "" : "s")}, {BinaryPrimitives.ReadUInt32LittleEndian(p[12..]):N0} Hz"));
                rate = (int)BinaryPrimitives.ReadUInt32LittleEndian(p[12..]);
            }
            else if (p.Length >= 19 && p[..8].SequenceEqual("OpusHead"u8))
            {
                codec = "Opus";
                rate = 48_000; // Opus timestamps count 48 kHz samples
                general.Add(("Audio", $"{p[9]} channel{(p[9] == 1 ? "" : "s")}, originally {BinaryPrimitives.ReadUInt32LittleEndian(p[12..]):N0} Hz"));
            }
            else if (p.Length >= 5 && p[0] == 0x7F && p[1..5].SequenceEqual("FLAC"u8)) codec = "FLAC";
            else if (p.Length >= 7 && p[0] == 0x80 && p[1..7].SequenceEqual("theora"u8)) codec = "Theora video";
        }
        general.Insert(0, ("Codec", codec));
        // Duration: the granule position of the last page.
        long length = r.Length;
        var tail = r.Read(Math.Max(0, length - 64 * 1024), (int)Math.Min(length, 64 * 1024));
        int last = tail.AsSpan().LastIndexOf("OggS"u8);
        if (rate > 0 && last >= 0 && last + 14 <= tail.Length)
        {
            long granule = BinaryPrimitives.ReadInt64LittleEndian(tail.AsSpan(last + 6));
            if (granule > 0) general.Insert(0, ("Duration", Duration(granule / (double)rate)));
        }
        return new InspectionReport($"Ogg {codec}", [new InspectionSection("General", general)], []);
    }

    // ---- MP3 ------------------------------------------------------------------------------------------------------

    private static InspectionReport Mp3(ContentReader r)
    {
        var general = new List<(string, string)>();
        var tags = new List<(string, string)>();
        long audioStart = 0;
        var h = r.Read(0, 10);
        if (h.Length == 10 && h.AsSpan(0, 3).SequenceEqual("ID3"u8))
        {
            int size = (h[6] & 0x7F) << 21 | (h[7] & 0x7F) << 14 | (h[8] & 0x7F) << 7 | (h[9] & 0x7F);
            audioStart = 10 + size;
            general.Add(("ID3 tag", $"version 2.{h[3]}"));
            var tag = r.Read(10, Math.Min(size, 512 * 1024));
            int at = 0;
            int idLength = h[3] == 2 ? 3 : 4;
            while (at + idLength * 2 <= tag.Length && tags.Count < 20)
            {
                string id = Encoding.ASCII.GetString(tag, at, idLength);
                if (id[0] == '\0') break;
                int frameSize = h[3] switch
                {
                    2 => (tag[at + 3] << 16) | (tag[at + 4] << 8) | tag[at + 5],
                    4 => (tag[at + 4] & 0x7F) << 21 | (tag[at + 5] & 0x7F) << 14 | (tag[at + 6] & 0x7F) << 7 | (tag[at + 7] & 0x7F),
                    _ => (int)Math.Min(BinaryPrimitives.ReadUInt32BigEndian(tag.AsSpan(at + 4)), int.MaxValue),
                };
                int headerSize = h[3] == 2 ? 6 : 10;
                if (frameSize <= 0 || (long)at + headerSize + frameSize > tag.Length) break;
                string? name = id switch { "TIT2" or "TT2" => "Title", "TPE1" or "TP1" => "Artist", "TALB" or "TAL" => "Album", "TYER" or "TYE" or "TDRC" => "Year", "TRCK" or "TRK" => "Track", "TCON" or "TCO" => "Genre", _ => null };
                if (name is not null) tags.Add((name, Id3Text(tag.AsSpan(at + headerSize, frameSize))));
                at += headerSize + frameSize;
            }
        }
        // The first MPEG audio frame after the tag.
        var frames = r.Read(audioStart, 64 * 1024);
        for (int i = 0; i + 4 <= frames.Length; i++)
        {
            if (frames[i] != 0xFF || (frames[i + 1] & 0xE0) != 0xE0 || Mpeg(frames.AsSpan(i)) is not { } m) continue;
            general.Add(("Audio", $"MPEG {m.Version} layer {m.Layer}, {m.Bitrate} kbit/s, {m.Rate:N0} Hz, {m.Mode}"));
            if (m.Bitrate > 0) general.Insert(0, ("Duration", Duration((r.Length - audioStart - i) * 8.0 / (m.Bitrate * 1000)) + " (estimated from the bit rate)"));
            break;
        }
        var sections = new List<InspectionSection> { new("General", general) };
        if (tags.Count > 0) sections.Add(new InspectionSection("Tags", tags));
        return new InspectionReport("MP3 audio", sections, []);
    }

    private static string Id3Text(ReadOnlySpan<byte> frame)
    {
        if (frame.Length < 2) return string.Empty;
        var body = frame[1..];
        string text = frame[0] switch
        {
            // UTF-16 with a byte-order mark.
            1 when body.Length >= 2 && body[0] == 0xFE && body[1] == 0xFF => Encoding.BigEndianUnicode.GetString(body[2..]),
            1 when body.Length >= 2 && body[0] == 0xFF && body[1] == 0xFE => Encoding.Unicode.GetString(body[2..]),
            1 => Encoding.Unicode.GetString(body),
            2 => Encoding.BigEndianUnicode.GetString(body),
            3 => Encoding.UTF8.GetString(body),
            _ => Encoding.Latin1.GetString(body),
        };
        text = text.TrimStart('﻿', '￾').TrimEnd('\0');
        return Printable(text.Length > 200 ? text[..200] : text);
    }

    private sealed record MpegFrame(string Version, int Layer, int Bitrate, int Rate, string Mode);

    private static MpegFrame? Mpeg(ReadOnlySpan<byte> h)
    {
        if (h.Length < 4) return null;
        int version = (h[1] >> 3) & 3, layer = (h[1] >> 1) & 3, bitrateIndex = h[2] >> 4, rateIndex = (h[2] >> 2) & 3;
        if (version == 1 || layer == 0 || bitrateIndex is 0 or 15 || rateIndex == 3) return null;
        int[] rates = version switch { 3 => [44100, 48000, 32000], 2 => [22050, 24000, 16000], _ => [11025, 12000, 8000] };
        int layerNumber = 4 - layer;
        int[] v1 = layerNumber switch { 1 => [0, 32, 64, 96, 128, 160, 192, 224, 256, 288, 320, 352, 384, 416, 448], 2 => [0, 32, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, 384], _ => [0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320] };
        int[] v2 = layerNumber == 1 ? [0, 32, 48, 56, 64, 80, 96, 112, 128, 144, 160, 176, 192, 224, 256] : [0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160];
        int bitrate = (version == 3 ? v1 : v2)[bitrateIndex];
        string mode = (h[3] >> 6) switch { 0 => "stereo", 1 => "joint stereo", 2 => "dual channel", _ => "mono" };
        return new MpegFrame(version switch { 3 => "1", 2 => "2", _ => "2.5" }, layerNumber, bitrate, rates[rateIndex], mode);
    }
}

/// <summary>
/// HTML inspector: what a page is and what it would load, read statically. Nothing is rendered, and no script runs.
/// Title, language, character set, meta description and generator, and counts of scripts, stylesheets, links, images,
/// forms, and frames, plus the other sites it loads resources from.
/// </summary>
public static class HtmlInspector
{
    private const int MaxRead = 4 * 1024 * 1024;

    public static InspectionReport? Inspect(IContentSource source, CancellationToken ct)
    {
        var r = new ContentReader(source);
        var head = r.Read(0, 1024);
        string start = Encoding.UTF8.GetString(head).TrimStart('﻿', ' ', '\t', '\r', '\n');
        bool html = start.StartsWith("<!doctype html", StringComparison.OrdinalIgnoreCase) || start.StartsWith("<html", StringComparison.OrdinalIgnoreCase) ||
                    start.StartsWith("<!--", StringComparison.Ordinal) && start.Contains("<html", StringComparison.OrdinalIgnoreCase);
        if (!html) return null;
        ct.ThrowIfCancellationRequested();
        var bytes = r.Read(0, MaxRead);
        string text = Encoding.UTF8.GetString(bytes);
        var warnings = new List<string>();
        if (r.Length > MaxRead) warnings.Add($"Only the first {MaxRead / (1024 * 1024)} MiB were read.");
        const System.Text.RegularExpressions.RegexOptions Options =
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.NonBacktracking;
        string? First(string pattern) => System.Text.RegularExpressions.Regex.Match(text, pattern, Options) is { Success: true } m ? Clean(m.Groups[1].Value) : null;
        int Count(string pattern) => System.Text.RegularExpressions.Regex.Count(text, pattern, Options);
        var page = new List<(string, string)>();
        void Add(string name, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value)) page.Add((name, value));
        }
        Add("Title", First(@"<title[^>]*>([^<]{0,300})"));
        Add("Language", First(@"<html[^>]*\blang\s*=\s*[""']?([A-Za-z0-9-]{1,20})"));
        Add("Character set", First(@"<meta[^>]*charset\s*=\s*[""']?([A-Za-z0-9_-]{1,40})"));
        Add("Description", First(@"<meta[^>]*name\s*=\s*[""']description[""'][^>]*content\s*=\s*[""']([^""']{0,300})"));
        Add("Generator", First(@"<meta[^>]*name\s*=\s*[""']generator[""'][^>]*content\s*=\s*[""']([^""']{0,200})"));
        var counts = new List<(string, string)>
        {
            ("Scripts", $"{Count(@"<script\b[^>]*\bsrc\s*=")} external, {Count(@"<script\b") - Count(@"<script\b[^>]*\bsrc\s*=")} inline"),
            ("Stylesheets", Count(@"<link\b[^>]*rel\s*=\s*[""']?stylesheet").ToString(CultureInfo.CurrentCulture)),
            ("Links", Count(@"<a\b[^>]*\bhref\s*=").ToString(CultureInfo.CurrentCulture)),
            ("Images", Count(@"<img\b").ToString(CultureInfo.CurrentCulture)),
            ("Forms", Count(@"<form\b").ToString(CultureInfo.CurrentCulture)),
            ("Frames", Count(@"<i?frame\b").ToString(CultureInfo.CurrentCulture)),
        };
        // The sites the page loads resources from (scripts, styles, images, frames): links alone load nothing.
        var hosts = System.Text.RegularExpressions.Regex.Matches(text, @"<(?:script|img|iframe|frame|link|source|video|audio|embed)\b[^>]*\b(?:src|href)\s*=\s*[""']?(?:https?:)?//([A-Za-z0-9.-]{1,253})", Options)
            .Select(m => m.Groups[1].Value.ToLowerInvariant()).Distinct().Order().Take(30).ToList();
        var sections = new List<InspectionSection> { new("Page", page), new("Contents", counts) };
        if (hosts.Count > 0) sections.Add(new InspectionSection($"Loads from other sites ({hosts.Count})", hosts.Select(h => (h, string.Empty)).ToList()));
        return new InspectionReport("HTML page", sections, warnings);
    }

    private static string Clean(string s)
    {
        var decoded = System.Net.WebUtility.HtmlDecode(s).Trim();
        return new string(decoded.Select(c => char.IsControl(c) ? ' ' : c).ToArray());
    }
}
