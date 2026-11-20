namespace FileCat.Core.Analysis;

/// <summary>A trusted read-only input owner. Its bytes are untrusted; it grants no live-source or write authority.</summary>
public abstract class AnalysisInventoryInput : IAnalysisScratchOwned
{
    internal AnalysisInventoryInput() { }
    internal abstract Stream Stream { get; }
    internal abstract string Identity { get; }
    internal abstract void Verify();
    public abstract void Dispose();

    internal static string ValidateChosenPath(string path, bool comparison = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!Path.IsPathFullyQualified(path) || path.Contains('\0') || path.Contains('\ufffd') ||
            !AnalysisPrivateInventoryStore.WellFormedText(path) || System.Text.Encoding.UTF8.GetByteCount(path) > AnalysisLimits.LocatorBytes ||
            path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(component => component is "." or ".."))
            throw new ArgumentException("Choose an absolute local inventory path whose spelling can be preserved exactly.");
        string name = Path.GetFileName(path), extension = comparison ? ".fcdsc" : ".fcdsa";
        // Canonicalize the known suffix only for portable-name validation. The
        // returned path and the native opener's basename keep the exact spelling
        // chosen by the user, including on a case-sensitive filesystem.
        if (name.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) name = name[..^extension.Length] + extension;
        if (comparison) AnalysisPrivateInventoryStore.ValidateComparisonName(name);
        else AnalysisPrivateInventoryStore.ValidateName(name);
        return path;
    }

    // Native owners, not the stream wrapper, close their handles on admitted cleanup workers.
    internal static Stream Guard(Stream source, Action verify) => new InputStream(source, verify);
    private sealed class InputStream(Stream source, Action verify) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length { get { verify(); return source.Length; } }
        public override long Position { get => source.Position; set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer)
        { verify(); int count = source.Read(buffer[..Math.Min(buffer.Length, 64 * 1024)]); verify(); return count; }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
