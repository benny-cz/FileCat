using FileCat.Core.FileSystem;
using System.Runtime.InteropServices;

namespace FileCat.Core.Analysis;

public sealed unsafe partial class UnixAnalysisPrivateDirectory
{
    /// <summary>Opens an explicitly selected local inventory for inert import; public source permissions are allowed.</summary>
    public static AnalysisInventoryInput OpenChosenInventory(string path) => new ChosenInventory(path);
    /// <summary>Opens an explicitly selected comparison using the same read-only native boundary.</summary>
    public static AnalysisInventoryInput OpenChosenComparison(string path) => new ChosenInventory(path, comparison: true);

    private sealed class ChosenInventory : AnalysisInventoryInput
    {
        private readonly bool _mac = OperatingSystem.IsMacOS();
        private readonly List<(string Name, OwnedHandle Handle, UnixStat Identity)> _parents = [];
        private OwnedHandle? _file;
        private FileStream? _stream;
        private readonly string _name;
        private readonly UnixStat _revision;
        private readonly Stream _guarded;
        private bool _disposed;
        private SafeHandle Parent => _parents[^1].Handle.Handle;
        private int DirFlags => _mac ? 0x100000 | 0x1000000 | 0x100 | 4 : LinuxDirectoryFlags(RuntimeInformation.ProcessArchitecture);
        private int InputFlags => _mac ? 0x1000000 | 0x100 | 4 : LinuxFileFlags(RuntimeInformation.ProcessArchitecture);
        internal ChosenInventory(string path, bool comparison = false)
        {
            if (!OperatingSystem.IsLinux() && !_mac) throw new PlatformNotSupportedException();
            if (AnalysisAdmission.Refusal() is { } reason) throw new InvalidOperationException(reason);
            ValidateChosenPath(path, comparison);
            if (path.StartsWith("//", StringComparison.Ordinal)) throw new NotSupportedException("Choose a local inventory path.");
            _name = Path.GetFileName(path);
            try
            {
                AddParent("/", OpenOwned(null, "/", DirFlags, 0));
                foreach (string component in Path.GetDirectoryName(path)!.Split('/', StringSplitOptions.RemoveEmptyEntries))
                    AddParent(component, OpenOwned(Parent, component, DirFlags, 0));
                _file = OpenOwned(Parent, _name, InputFlags, 0); _revision = Stat(_file.Handle);
                if (!_revision.IsRegular || _revision.IsLink || !_revision.HasChangeTime || _revision.LinkCount == 0 || _revision.Size is < 1 or > AnalysisLimits.InventoryBytes)
                    throw new IOException("Choose an ordinary readable inventory no larger than 8 GiB; links and special files are unavailable.");
                bool locked = false;
                for (int attempt = 0; attempt < 8; attempt++)
                {
                    if (Flock(_file.Handle, 1 | 4) == 0) { locked = true; break; }
                    if (Marshal.GetLastPInvokeError() != 4) break;
                }
                if (!locked) throw new IOException("The selected inventory is busy or native reader locking is unavailable.");
                Verify(); _stream = _file.Handle.OpenStream(FileAccess.Read); _guarded = Guard(_stream, Verify);
            }
            catch { Dispose(); throw; }
        }
        private void AddParent(string name, OwnedHandle handle)
        {
            try
            {
                var stat = Stat(handle.Handle);
                if (!stat.IsDirectory || stat.IsLink) throw new IOException("The selected inventory ancestry is not an ordinary local directory.");
                _parents.Add((name, handle, stat));
            }
            catch { handle.Dispose(); throw; }
        }
        internal override Stream Stream => !_disposed ? _guarded : throw new ObjectDisposedException(nameof(ChosenInventory));
        internal override string Identity => (_mac ? "darwin:" : "linux:") + _revision.Device.ToString("X") + ":" + _revision.Inode.ToString("X");
        internal override void Verify()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            for (int i = 0; i < _parents.Count; i++)
            {
                var parent = _parents[i]; var actual = Stat(parent.Handle.Handle);
                if (!Same(parent.Identity, actual) || !actual.IsDirectory || actual.IsLink) throw new IOException("The selected inventory ancestry changed.");
                if (i == 0) continue;
                using var probe = OpenOwned(_parents[i - 1].Handle.Handle, parent.Name, DirFlags, 0);
                if (!Same(actual, Stat(probe.Handle))) throw new IOException("The selected inventory folder moved or changed.");
            }
            var file = _file ?? throw new ObjectDisposedException(nameof(ChosenInventory));
            var current = Stat(file.Handle);
            if (!current.IsRegular || current.IsLink || current.Size != _revision.Size || !current.HasChangeTime ||
                current.ChangeSeconds != _revision.ChangeSeconds || current.ChangeNanoseconds != _revision.ChangeNanoseconds || !Same(current, _revision))
                throw new IOException("The selected inventory changed during import.");
            using var name = OpenOwned(Parent, _name, InputFlags, 0);
            if (!Same(current, Stat(name.Handle))) throw new IOException("The selected inventory name was replaced during import.");
        }
        public override void Dispose()
        {
            if (_disposed) return; _disposed = true; var errors = new List<Exception>();
            try { _stream?.Dispose(); } catch (Exception error) { errors.Add(error); }
            try { _file?.Dispose(); } catch (Exception error) { errors.Add(error); }
            for (int i = _parents.Count - 1; i >= 0; i--) try { _parents[i].Handle.Dispose(); } catch (Exception error) { errors.Add(error); }
            _parents.Clear(); _file = null; _stream = null;
            if (errors.Count != 0) throw new AggregateException(errors);
        }
    }
}
