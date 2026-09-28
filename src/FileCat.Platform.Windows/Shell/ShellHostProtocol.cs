namespace FileCat.Platform.Windows.Shell;

public enum ShellImageKind : byte
{
    /// <summary>What a thumbnail handler draws for the file's content; nothing when the type has no handler.</summary>
    Thumbnail = 1,
    /// <summary>The file's own icon (an executable's embedded icon, say), through its icon handler.</summary>
    Icon = 2,
    /// <summary>
    /// An icon from a resource file that a shortcut or a folder's desktop.ini names: the request's path is
    /// "index|file" (see <see cref="IconResourceRequest"/>). No handler runs; the icon is read from the file's resources.
    /// </summary>
    IconResource = 3,
    /// <summary>The installed Shell overlay, already composed with the item icon (for example TortoiseGit).</summary>
    OverlayIcon = 4,
}

public static class IconResourceRequest
{
    public static string Format(IconLocation location) => location.Index.ToString(System.Globalization.CultureInfo.InvariantCulture) + "|" + location.File;

    public static IconLocation? Parse(string request)
    {
        int bar = request.IndexOf('|');
        return bar > 0 && int.TryParse(request.AsSpan(0, bar), System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out int index)
            ? new IconLocation(request[(bar + 1)..], index) : null;
    }
}

/// <summary>A picture from the Shell helper: top-down rows of premultiplied BGRA pixels.</summary>
public sealed record ShellImage(int Width, int Height, byte[] Bgra);

/// <summary>
/// Framing between FileCat and FileCat.ShellHost over the helper's standard input and output. A request is a kind, a
/// size in pixels, and a path; the answer is a status and, for success, the picture. Everything is length-checked on
/// both sides, so a confused or hostile peer ends the conversation instead of allocating without bound.
/// </summary>
public static class ShellHostProtocol
{
    public const string ServeArgument = "--serve";
    public const string TestFaultsArgument = "--test-faults";
    public const int MaxPixels = 1024;
    private const int MaxPathChars = 32_767;
    private const int MaxMessageChars = 2_000;

    // Test-only requests, honored only by a helper started with --test-faults.
    public const byte HangRequest = 250, CrashRequest = 251, SpawnRequest = 252, IntegrityRequest = 253, WriteProbeRequest = 254;

    public enum Status : byte
    {
        Image = 0,
        /// <summary>The type has no handler for this kind of picture.</summary>
        None = 1,
        Failed = 2,
        /// <summary>A text answer (test requests).</summary>
        Text = 3,
    }

    public static void WriteRequest(BinaryWriter w, byte kind, int size, string path)
    {
        w.Write(kind);
        w.Write(size);
        w.Write(path.Length);
        foreach (char c in path) w.Write((ushort)c);
        w.Flush();
    }

    /// <summary>False at the end of the input or on a malformed request (the helper then exits).</summary>
    public static bool TryReadRequest(BinaryReader r, out byte kind, out int size, out string path)
    {
        kind = 0;
        size = 0;
        path = string.Empty;
        try
        {
            kind = r.ReadByte();
            size = r.ReadInt32();
            int length = r.ReadInt32();
            if (size is < 1 or > MaxPixels || length is < 0 or > MaxPathChars) return false;
            var chars = new char[length];
            for (int i = 0; i < length; i++) chars[i] = (char)r.ReadUInt16();
            path = new string(chars);
            return true;
        }
        catch (EndOfStreamException)
        {
            return false;
        }
    }

    public static void WriteImage(BinaryWriter w, ShellImage image)
    {
        w.Write((byte)Status.Image);
        w.Write(image.Width);
        w.Write(image.Height);
        w.Write(image.Bgra);
        w.Flush();
    }

    public static void WriteStatus(BinaryWriter w, Status status, string? message = null)
    {
        w.Write((byte)status);
        var text = (message ?? string.Empty).Length > MaxMessageChars ? message![..MaxMessageChars] : message ?? string.Empty;
        w.Write(text.Length);
        foreach (char c in text) w.Write((ushort)c);
        w.Flush();
    }

    /// <summary>Reads one answer; throws <see cref="InvalidDataException"/> when it breaks the protocol.</summary>
    public static (Status Status, ShellImage? Image, string? Message) ReadResponse(BinaryReader r)
    {
        var status = (Status)r.ReadByte();
        if (status == Status.Image)
        {
            int width = r.ReadInt32(), height = r.ReadInt32();
            if (width is < 1 or > MaxPixels || height is < 1 or > MaxPixels) throw new InvalidDataException("The Shell helper sent an image of an impossible size.");
            var bgra = r.ReadBytes(width * height * 4);
            if (bgra.Length != width * height * 4) throw new EndOfStreamException();
            return (status, new ShellImage(width, height, bgra), null);
        }
        if (status is not (Status.None or Status.Failed or Status.Text)) throw new InvalidDataException("The Shell helper sent an unknown answer.");
        int length = r.ReadInt32();
        if (length is < 0 or > MaxMessageChars) throw new InvalidDataException("The Shell helper sent an overlong message.");
        var chars = new char[length];
        for (int i = 0; i < length; i++) chars[i] = (char)r.ReadUInt16();
        return (status, null, new string(chars));
    }
}
