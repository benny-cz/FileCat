using System.Globalization;
using System.Runtime.InteropServices;
using FileCat.Core.FileSystem;

namespace FileCat.Core.Analysis;

public sealed unsafe partial class UnixAnalysisPrivateDirectory
{
    /// <summary>Anonymous staging and create-new publication in a chosen ext4/XFS folder, or APFS using qualified same-volume private scratch.</summary>
    public static AnalysisExportDestination OpenExportDestination(string folder, Func<AnalysisPrivateDirectory>? createPrivateScratch = null,
        IReadOnlySet<string>? protectedDirectories = null) => new ExportDestination(folder, createPrivateScratch, protectedDirectories);
    private sealed class ExportDestination : AnalysisExportDestination
    {
        private readonly bool _mac = OperatingSystem.IsMacOS();
        private readonly uint _owner;
        private readonly List<(string Name, OwnedHandle Handle, UnixStat Identity)> _parents = [];
        private OwnedHandle? _file;
        private AnalysisPrivateDirectory? _private;
        private AnalysisPrivateFile? _staging;
        private FileStream? _stream;
        private bool _disposed, _attempted, _published;
        private UnixStat _initial;
        private SafeHandle Parent => _parents[^1].Handle.Handle;
        private SafeHandle File => (SafeHandle?)_file?.Handle ?? _stream?.SafeFileHandle ?? throw new ObjectDisposedException(nameof(ExportDestination));
        private int DirFlags => _mac ? 0x100000 | 0x1000000 | 0x100 | 4 : 0x10000 | 0x80000 | 0x20000;
        private int ReadFlags => _mac ? 0x1000000 | 0x100 | 4 : 0x80000 | 0x20000 | 0x800;
        internal ExportDestination(string folder, Func<AnalysisPrivateDirectory>? createPrivateScratch, IReadOnlySet<string>? protectedDirectories)
        {
            if (!_mac && !OperatingSystem.IsLinux()) throw new PlatformNotSupportedException();
            if (AnalysisAdmission.Refusal() is { } reason) throw new InvalidOperationException(reason);
            ValidateFolder(folder); if (folder.StartsWith("//", StringComparison.Ordinal)) throw new NotSupportedException("Choose a directly accessible local folder.");
            _owner = GetEuid();
            try
            {
                AddParent("/", OpenOwned(null, "/", DirFlags, 0));
                foreach (string component in folder.Split('/', StringSplitOptions.RemoveEmptyEntries)) AddParent(component, OpenOwned(Parent, component, DirFlags, 0));
                _ = ReadSpace(Parent, _mac);
                if (protectedDirectories is not null)
                {
                    if (protectedDirectories.Count > 64) throw new NotSupportedException("The protected output boundary exceeds its finite limit.");
                    foreach (var parent in _parents)
                        if (protectedDirectories.Contains($"{(_mac ? "darwin" : "linux")}:{parent.Identity.Device:X}:{parent.Identity.Inode:X}"))
                            throw new IOException("Exports into protected FileCat artifacts are unavailable. Choose another folder.");
                }
                if (_mac)
                {
                    _private = createPrivateScratch?.Invoke() ?? throw new NotSupportedException("APFS export requires qualified private staging on the output volume.");
                    if (_private is not UnixAnalysisPrivateDirectory) throw new NotSupportedException("APFS native private staging is required.");
                    _private.Verify(); _staging = _private.CreateStaging(); _staging.Verify();
                    _stream = _staging.Stream as FileStream ?? throw new NotSupportedException("Native APFS staging ownership is required.");
                    if (Stat(File).Device != Stat(Parent).Device) throw new NotSupportedException("Choose an APFS output folder on the private staging volume; cross-volume publication is unavailable.");
                }
                else
                {
                    _file = OpenOwned(Parent, ".", 0x410000 | 0x80000 | 2, 0x180); // O_TMPFILE, publication allowed; 0600.
                    _stream = _file.Handle.OpenStream(FileAccess.ReadWrite);
                }
                _initial = Stat(File); Verify();
            }
            catch { Dispose(); throw; }
        }
        private void AddParent(string name, OwnedHandle handle)
        {
            try
            {
                var stat = Stat(handle.Handle); if (!stat.IsDirectory || stat.IsLink) throw new IOException("Output ancestry is not an ordinary directory.");
                _parents.Add((name, handle, stat));
            }
            catch { handle.Dispose(); throw; }
        }
        internal override Stream Stream => _stream ?? throw new ObjectDisposedException(nameof(ExportDestination));
        internal override void Verify()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            for (int i = 0; i < _parents.Count; i++)
            {
                var parent = _parents[i]; var current = Stat(parent.Handle.Handle);
                if (!Same(parent.Identity, current) || !current.IsDirectory || current.IsLink) throw new IOException("The chosen output ancestry changed.");
                if (i == 0) continue;
                using var probe = OpenOwned(_parents[i - 1].Handle.Handle, parent.Name, DirFlags, 0);
                if (!Same(current, Stat(probe.Handle))) throw new IOException("The chosen output folder moved or changed.");
            }
            _private?.Verify(); _staging?.Verify(); var file = Stat(File);
            if (!Same(_initial, file) || !file.IsRegular || file.IsLink || file.Owner != _owner || file.Mode != 0x180 ||
                file.LinkCount != (_published && !_mac ? 1u : 0u) || file.Size is < 0 or > AnalysisLimits.InventoryBytes)
                throw new IOException("Unpublished export bytes no longer have qualified private native ownership.");
        }
        internal override (UInt128 Capacity, UInt128 Available) Space() { Verify(); return ReadSpace(Parent, _mac); }
        internal override void FlushDurable()
        {
            Verify(); _stream!.Flush(flushToDisk: true);
            if (_mac && Fcntl(File, 51) != 0) throw Error("Export data durability could not be confirmed.");
            Verify();
        }
        internal override void PublishNew(string name)
        {
            ValidateName(name); Verify();
            if (_attempted) throw new InvalidOperationException("Choose a fresh output owner after any publication attempt.");
            _attempted = true;
            if (_mac)
            {
                if (CloneFileAt(File, Parent, name, 0) != 0) throw PublicationError(name);
            }
            else
            {
                using var proc = OpenOwned(null, "/proc", DirFlags, 0);
                byte* fs = stackalloc byte[512]; new Span<byte>(fs, 512).Clear();
                if (FstatFs(proc.Handle, fs) != 0 || *(ulong*)fs != 0x9fa0) throw new IOException("The kernel descriptor publication route is unavailable.");
                using var process = OpenOwned(proc.Handle, Environment.ProcessId.ToString(CultureInfo.InvariantCulture), DirFlags, 0);
                using var descriptors = OpenOwned(process.Handle, "fd", DirFlags, 0);
                bool held = false;
                try
                {
                    File.DangerousAddRef(ref held);
                    if (LinkAt(descriptors.Handle, Fd(File).ToString(CultureInfo.InvariantCulture), Parent, name, 0x400) != 0)
                        throw PublicationError(name);
                }
                finally { if (held) File.DangerousRelease(); }
            }
            _published = true;
            try
            {
                Verify(); using var committed = OpenOwned(Parent, name, ReadFlags, 0); var actual = Stat(committed.Handle);
                if (!actual.IsRegular || actual.IsLink || actual.Owner != _owner || actual.Mode != 0x180 || actual.LinkCount != 1 ||
                    actual.Size != Stat(File).Size || !_mac && !Same(actual, Stat(File))) throw new IOException("The new output binding changed.");
                if (Fsync(committed.Handle) != 0 || _mac && Fcntl(committed.Handle, 51) != 0 || Fsync(Parent) != 0)
                    throw Error("Output publication durability could not be confirmed.");
                Verify(); using var final = OpenOwned(Parent, name, ReadFlags, 0);
                if (!Same(actual, Stat(final.Handle))) throw new IOException("The new output name was replaced.");
            }
            catch (Exception error) { throw new AnalysisExportUncertainException(name, error); }
        }
        private static Exception PublicationError(string name)
        {
            int code = Marshal.GetLastPInvokeError(); var error = new IOException("Create-new output publication failed. Existing files were kept.", new System.ComponentModel.Win32Exception(code));
            return code == 17 ? new AnalysisExportTargetExistsException(name, error) : error;
        }
        public override void Dispose()
        {
            if (_disposed) return; _disposed = true; var errors = new List<Exception>();
            // The private staging owner closes its stream. Linux directly owns its anonymous file.
            if (_staging is null) try { _stream?.Dispose(); } catch (Exception error) { errors.Add(error); }
            try { _file?.Dispose(); } catch (Exception error) { errors.Add(error); }
            try { _staging?.Dispose(); } catch (Exception error) { errors.Add(error); }
            try { _private?.Dispose(); } catch (Exception error) { errors.Add(error); }
            for (int i = _parents.Count - 1; i >= 0; i--) try { _parents[i].Handle.Dispose(); } catch (Exception error) { errors.Add(error); }
            _stream = null; _file = null; _private = null; _staging = null; _parents.Clear();
            if (errors.Count != 0) throw new AggregateException(errors);
        }
    }
}
