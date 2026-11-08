using System.Runtime.InteropServices;
using FileCat.Core.Analysis;
using static FileCat.Platform.Windows.Elevation.NtFile;

namespace FileCat.Platform.Windows;

public sealed unsafe partial class WindowsAnalysisPrivateDirectory
{
    /// <summary>Opens an explicitly selected local inventory for inert import; it does not require a private source ACL.</summary>
    public static AnalysisInventoryInput OpenChosenInventory(string path) => new ChosenInventory(path);

    private sealed class ChosenInventory : AnalysisInventoryInput
    {
        private readonly List<OwnedHandle> _parents = [];
        private OwnedHandle? _file;
        private FileStream? _stream;
        private readonly Stream _guarded;
        private readonly InputRevision _revision;
        private bool _disposed;
        private readonly record struct InputRevision(string Identity, long Length, long Changed, long Modified);
        internal ChosenInventory(string path)
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
            if (AnalysisAdmission.Refusal() is { } reason) throw new InvalidOperationException(reason);
            ValidateChosenPath(path);
            string drive = Path.GetPathRoot(path) ?? "";
            if (drive.Length != 3 || drive[1] != ':' || drive[2] != '\\' || GetDriveType(drive) is not (2 or 3))
                throw new NotSupportedException("Choose an inventory on a directly accessible local drive.");
            try
            {
                _parents.Add(OpenOwned(null, @"\??\" + drive, Traverse | ReadAttributes | ReadControl | Synchronize,
                    ShareRead | ShareWrite, Open, DirectoryFile | OpenReparsePoint));
                string parent = Path.GetDirectoryName(path)!;
                foreach (string component in parent[drive.Length..].Split('\\', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (!AnalysisNames.IsOrdinaryChild(component, windows: true)) throw new IOException("The inventory ancestry has an unsupported spelling.");
                    RejectReparse(_parents[^1].Handle);
                    _parents.Add(OpenOwned(_parents[^1].Handle, component, Traverse | ReadAttributes | ReadControl | Synchronize,
                        ShareRead | ShareWrite, Open, DirectoryFile | OpenReparsePoint));
                }
                RejectReparse(_parents[^1].Handle);
                _file = OpenOwned(_parents[^1].Handle, Path.GetFileName(path), GenericRead | ReadControl | Synchronize,
                    ShareRead, Open, NonDirectoryFile | OpenReparsePoint);
                _revision = ReadRevision(_file.Handle); Verify();
                _stream = _file.Handle.OpenStream(FileAccess.Read); _guarded = Guard(_stream, Verify);
            }
            catch { Dispose(); throw; }
        }
        private static InputRevision ReadRevision(SafeHandle handle)
        {
            RejectReparse(handle);
            byte* standard = stackalloc byte[24]; byte* basic = stackalloc byte[40]; byte* identity = stackalloc byte[24];
            if (!GetFileInformationByHandleEx(handle, 1, standard, 24) || !GetFileInformationByHandleEx(handle, 0, basic, 40) ||
                !GetFileInformationByHandleEx(handle, 18, identity, 24)) throw new IOException("Stable inventory input metadata is unavailable.");
            long bytes = *(long*)(standard + 8); uint attributes = *(uint*)(basic + 32);
            if (standard[21] != 0 || standard[20] != 0 || *(uint*)(standard + 16) == 0 || bytes is < 1 or > AnalysisLimits.InventoryBytes ||
                (attributes & (0x400u | 0x1000u | 0x40000u | 0x400000u)) != 0)
                throw new IOException("Choose an ordinary available inventory no larger than 8 GiB; links, offline and special files are unavailable.");
            var id = new ReadOnlySpan<byte>(identity + 8, 16);
            if (id.IndexOfAnyExcept((byte)0) < 0) throw new IOException("The selected inventory identity is unavailable.");
            return new("win:" + (*(ulong*)identity).ToString("X") + ":" + Convert.ToHexString(id), bytes, *(long*)(basic + 24), *(long*)(basic + 16));
        }
        internal override Stream Stream => !_disposed ? _guarded : throw new ObjectDisposedException(nameof(ChosenInventory));
        internal override string Identity => _revision.Identity;
        internal override void Verify()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            foreach (var parent in _parents) RejectReparse(parent.Handle);
            if (_file is null || ReadRevision(_file.Handle) != _revision) throw new IOException("The selected inventory changed during import.");
        }
        public override void Dispose()
        {
            if (_disposed) return; _disposed = true; var errors = new List<Exception>();
            try { _stream?.Dispose(); } catch (Exception error) { errors.Add(error); }
            try { _file?.Dispose(); } catch (Exception error) { errors.Add(error); }
            for (int i = _parents.Count - 1; i >= 0; i--) try { _parents[i].Dispose(); } catch (Exception error) { errors.Add(error); }
            _parents.Clear(); _file = null; _stream = null;
            if (errors.Count != 0) throw new AggregateException(errors);
        }
    }
}
