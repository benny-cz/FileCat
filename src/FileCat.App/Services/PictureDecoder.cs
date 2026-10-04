using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using FileCat.Core.Content;
using FileCat.Core.Resources;

namespace FileCat.App.Services;

/// <summary>A picture decoded for showing: upright, scaled to fit, with what the file says about itself.</summary>
/// <param name="Width">The picture's own width in pixels, as shown (after its EXIF orientation).</param>
/// <param name="Frames">More than one for an animation (the first frame is shown).</param>
/// <param name="Incomplete">The data ended before the picture did: the rest is blank.</param>
public sealed record DecodedPicture(WriteableBitmap Bitmap, int Width, int Height, string Format, int Frames, bool Incomplete);

/// <summary>
/// Pictures for the viewer, decoded by <see cref="PictureWorker"/> in a process of its own: one per picture, fed the bytes
/// through standard input and read back within bounds, and stopped after <see cref="Timeout"/>. On Windows the worker
/// runs in the Shell helper's sandbox (low integrity, a job that caps its memory). A picture that crashes or exhausts the
/// worker takes nothing else with it, and the viewer says why it shows nothing.
/// </summary>
public static class PictureDecoder
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
    private const long MemoryLimit = 1536L * 1024 * 1024;

    /// <summary>What a picture's first bytes say it is, for formats the worker decodes; null otherwise.</summary>
    public static string? Recognize(ReadOnlySpan<byte> head)
    {
        if (head.StartsWith((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A])) return "PNG";
        if (head.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xD8, 0xFF])) return "JPEG";
        if (head.StartsWith("GIF87a"u8) || head.StartsWith("GIF89a"u8)) return "GIF";
        if (head.Length >= 12 && head.StartsWith("RIFF"u8) && head[8..12].SequenceEqual("WEBP"u8)) return "WEBP";
        if (head.Length >= 18 && head.StartsWith("BM"u8) && BinaryPrimitives.ReadUInt32LittleEndian(head[14..]) is 12 or 40 or 52 or 56 or 64 or 108 or 124) return "BMP";
        if (head.Length >= 6 && head[0] == 0 && head[1] == 0 && head[2] is 1 or 2 && head[3] == 0 && head[4] + head[5] > 0) return "ICO";
        return null;
    }

    /// <summary>Decodes borrowed content; completion includes the feeder's last source call, even after cancellation.</summary>
    public static Task<DecodedPicture> DecodeAsync(IContentSource source, int maxSide, CancellationToken ct) =>
        DecodeAsync(source.Length, (input, token) => Feed(source, input, token), maxSide, ct, drainFeed: true);

    /// <summary>
    /// Decodes for a view. Cancellation can finish its UI demand while a synchronous source read is still held;
    /// the reader keeps that source alive until the actual feeder returns.
    /// </summary>
    public static Task<DecodedPicture> DecodeAsync(PagedReader reader, int maxSide, CancellationToken ct) =>
        DecodeAsync(reader.Length, (input, token) =>
        {
            token.ThrowIfCancellationRequested();
            reader.WithSource(source => Feed(source, input, token));
        }, maxSide, ct, drainFeed: false);

    private static async Task<DecodedPicture> DecodeAsync(long length, Action<Stream, CancellationToken> feed,
        int maxSide, CancellationToken ct, bool drainFeed)
    {
        ct.ThrowIfCancellationRequested();
        if (length > PictureWorker.MaxInputBytes)
            throw new InvalidDataException($"at {Formatters.SizeWithUnit(length)} it is larger than FileCat shows as a picture");
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(Timeout);
        var token = limit.Token;
        var (executable, arguments) = WorkerCommand(maxSide);
        using var worker = Worker.Start(executable, arguments);
        using var stop = token.Register(worker.Kill);
        // Written and read at once: the worker may stop reading early (a damaged file), and a full pipe must not block.
        var input = worker.Input;
        var feeding = Task.Run(() => feed(input, token), CancellationToken.None);
        DecodedPicture? picture = null;
        try
        {
            picture = await Task.Run(() => Read(worker.Output, maxSide), CancellationToken.None).ConfigureAwait(false);
            try { await feeding.ConfigureAwait(false); }
            catch (IOException) { } // the worker had what it needed
            token.ThrowIfCancellationRequested();
            var result = picture;
            picture = null; // ownership moves to the caller only on success
            return result;
        }
        catch (Exception) when (limit.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            throw new TimeoutException($"decoding it took longer than {Timeout.TotalSeconds:0} seconds");
        }
        catch (Exception) when (ct.IsCancellationRequested)
        {
            throw new OperationCanceledException(ct);
        }
        finally
        {
            picture?.Bitmap.Dispose();
            limit.Cancel(); // no more source calls after a worker failure, either
            var drained = ObserveFeedAsync(feeding);
            if (drainFeed) await drained.ConfigureAwait(false);
        }
    }

    private static async Task ObserveFeedAsync(Task feeding)
    {
        try { await feeding.ConfigureAwait(false); }
        catch (Exception) { } // already reported by the decode task, or abandoned after cancellation/worker failure
    }

    private static void Feed(IContentSource source, Stream input, CancellationToken ct)
    {
        try
        {
            var buffer = new byte[1024 * 1024];
            for (long offset = 0; offset < source.Length;)
            {
                ct.ThrowIfCancellationRequested();
                int n = source.Read(offset, buffer);
                ct.ThrowIfCancellationRequested();
                if (n <= 0) break;
                input.Write(buffer, 0, n);
                offset += n;
            }
        }
        finally
        {
            input.Dispose(); // the end of the picture
        }
    }

    /// <summary>The worker's answer, checked field by field: a reason, or the pixels of a picture no larger than asked.</summary>
    internal static DecodedPicture Read(Stream output, int maxSide)
    {
        Span<byte> magic = stackalloc byte[4];
        if (!ReadFully(output, magic)) throw new InvalidDataException(Died);
        if (magic.SequenceEqual(PictureWorker.FailureMagic))
        {
            Span<byte> length = stackalloc byte[4];
            if (!ReadFully(output, length)) throw new InvalidDataException(Died);
            int n = BinaryPrimitives.ReadInt32LittleEndian(length);
            if (n is < 0 or > 4096) throw new InvalidDataException(Garbled);
            var text = new byte[n];
            if (!ReadFully(output, text)) throw new InvalidDataException(Died);
            throw new InvalidDataException(Encoding.UTF8.GetString(text));
        }
        if (!magic.SequenceEqual(PictureWorker.SuccessMagic)) throw new InvalidDataException(Garbled);
        Span<byte> header = stackalloc byte[28];
        if (!ReadFully(output, header)) throw new InvalidDataException(Died);
        int width = BinaryPrimitives.ReadInt32LittleEndian(header), height = BinaryPrimitives.ReadInt32LittleEndian(header[4..]);
        int sourceWidth = BinaryPrimitives.ReadInt32LittleEndian(header[8..]), sourceHeight = BinaryPrimitives.ReadInt32LittleEndian(header[12..]);
        int frames = BinaryPrimitives.ReadInt32LittleEndian(header[16..]), flags = BinaryPrimitives.ReadInt32LittleEndian(header[20..]);
        int formatLength = BinaryPrimitives.ReadInt32LittleEndian(header[24..]);
        if (width < 1 || height < 1 || width > maxSide || height > maxSide || sourceWidth < 1 || sourceHeight < 1 || frames < 1 || formatLength is < 0 or > 32)
            throw new InvalidDataException(Garbled);
        var format = new byte[formatLength];
        if (!ReadFully(output, format)) throw new InvalidDataException(Died);
        var bitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        try
        {
            var row = new byte[width * 4];
            using (var target = bitmap.Lock())
            {
                for (int y = 0; y < height; y++)
                {
                    if (!ReadFully(output, row)) throw new InvalidDataException(Died);
                    Marshal.Copy(row, 0, target.Address + y * target.RowBytes, row.Length);
                }
            }
            return new DecodedPicture(bitmap, sourceWidth, sourceHeight, Encoding.ASCII.GetString(format), frames, (flags & 1) != 0);
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
    }

    private const string Died = "the decoder stopped part way: the picture is damaged, or needs more memory than FileCat allows a picture";
    private const string Garbled = "the decoder answered something FileCat does not accept";

    private static bool ReadFully(Stream stream, Span<byte> buffer)
    {
        try
        {
            stream.ReadExactly(buffer);
            return true;
        }
        catch (EndOfStreamException)
        {
            return false;
        }
    }

    /// <summary>
    /// FileCat itself, in worker mode: the app host next to FileCat.dll, the running file when FileCat is one file, or
    /// the dotnet host with the dll.
    /// </summary>
    internal static (string Executable, string Arguments) WorkerCommand(int maxSide)
    {
        string arguments = $"{PictureWorker.Argument} {maxSide}";
        string dll = typeof(PictureWorker).Assembly.Location;
        string? process = Environment.ProcessPath;
        if (dll.Length == 0 && process is not null) return (process, arguments);
        string host = Path.Combine(Path.GetDirectoryName(dll)!, OperatingSystem.IsWindows() ? "FileCat.exe" : "FileCat");
        if (File.Exists(host)) return (host, arguments);
        string dotnet = process is not null && Path.GetFileNameWithoutExtension(process).Equals("dotnet", StringComparison.OrdinalIgnoreCase) ? process : "dotnet";
        return (dotnet, $"\"{dll}\" {arguments}");
    }

    /// <summary>The worker process, sandboxed where the platform allows.</summary>
    private sealed class Worker : IDisposable
    {
        private readonly Platform.Windows.Shell.SandboxedWorker? _sandboxed;
        private readonly Process? _process;

        private Worker(Platform.Windows.Shell.SandboxedWorker? sandboxed, Process? process)
        {
            _sandboxed = sandboxed;
            _process = process;
        }

        public Stream Input => _sandboxed?.Input ?? _process!.StandardInput.BaseStream;
        public Stream Output => _sandboxed?.Output ?? _process!.StandardOutput.BaseStream;

        public static Worker Start(string executable, string arguments)
        {
            if (OperatingSystem.IsWindows()) return new Worker(Platform.Windows.Shell.SandboxedWorker.Start(executable, arguments, MemoryLimit), null);
            var psi = new ProcessStartInfo(executable, arguments)
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            var process = Process.Start(psi) ?? throw new InvalidOperationException("The picture decoder could not be started.");
            process.ErrorDataReceived += (_, _) => { };
            process.BeginErrorReadLine();
            return new Worker(null, process);
        }

        public void Kill()
        {
            try
            {
                _sandboxed?.Kill();
                if (_process is { HasExited: false }) _process.Kill();
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
        }

        public void Dispose()
        {
            Kill();
            _sandboxed?.Dispose();
            _process?.Dispose();
        }
    }
}
