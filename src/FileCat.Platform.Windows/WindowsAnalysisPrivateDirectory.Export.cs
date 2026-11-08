using System.Runtime.InteropServices;
using System.Security.Principal;
using FileCat.Core.Analysis;
using static FileCat.Platform.Windows.Elevation.NtFile;

namespace FileCat.Platform.Windows;

public sealed unsafe partial class WindowsAnalysisPrivateDirectory
{
    /// <summary>Private delete-on-close staging in an explicitly selected local NTFS folder; no existing target is replaced.</summary>
    public static AnalysisExportDestination OpenExportDestination(string folder, IReadOnlySet<string>? protectedDirectories = null) => new ExportDestination(folder, protectedDirectories);
    private sealed class ExportDestination : AnalysisExportDestination
    {
        private readonly List<OwnedHandle> _parents = [];
        private readonly SecurityIdentifier _owner;
        private OwnedHandle? _file;
        private FileStream? _stream;
        private bool _disposed, _attempted, _published;
        private SafeHandle Parent => _parents[^1].Handle;
        internal ExportDestination(string folder, IReadOnlySet<string>? protectedDirectories)
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
            if (AnalysisAdmission.Refusal() is { } reason) throw new InvalidOperationException(reason);
            ValidateFolder(folder); string drive = Path.GetPathRoot(folder) ?? "";
            if (drive.Length != 3 || drive[1] != ':' || drive[2] != '\\' || GetDriveType(drive) is not (2 or 3) || folder.Contains('/'))
                throw new NotSupportedException("Choose an output folder on a directly accessible local NTFS drive.");
            using var identity = WindowsIdentity.GetCurrent(); _owner = identity.User ?? throw new IOException("The output owner is unavailable.");
            try
            {
                string[] parts = folder[drive.Length..].Split('\\', StringSplitOptions.RemoveEmptyEntries);
                _parents.Add(OpenOwned(null, @"\??\" + drive, (parts.Length == 0 ? GenericRead | GenericWrite : Traverse | ReadAttributes) | ReadControl | Synchronize,
                    ShareRead | ShareWrite, Open, DirectoryFile | OpenReparsePoint));
                for (int i = 0; i < parts.Length; i++)
                {
                    string component = parts[i];
                    if (!AnalysisNames.IsOrdinaryChild(component, windows: true)) throw new IOException("The output ancestry has an unsupported spelling.");
                    RejectReparse(Parent);
                    _parents.Add(OpenOwned(Parent, component, (i == parts.Length - 1 ? GenericRead | GenericWrite : Traverse | ReadAttributes) | ReadControl | Synchronize,
                        ShareRead | ShareWrite, Open, DirectoryFile | OpenReparsePoint));
                }
                RejectReparse(Parent); _ = ReadSpace(Parent);
                if (protectedDirectories is not null)
                {
                    if (protectedDirectories.Count > 64) throw new NotSupportedException("The protected output boundary exceeds its finite limit.");
                    foreach (var parent in _parents)
                        if (Analysis.WindowsAnalysisBackend.CaptureDirectoryIdentity(parent.Handle) is not { } id || protectedDirectories.Contains(id))
                            throw new IOException("Exports into protected FileCat artifacts are unavailable. Choose another folder.");
                }
                _file = OpenOwned(Parent, ".filecat-export-" + Guid.NewGuid().ToString("N"), GenericRead | GenericWrite | Delete | ReadControl | Synchronize,
                    0, Create, NonDirectoryFile | OpenReparsePoint | 0x1000, DescriptorForOwner(_owner, directory: false));
                Verify(); _stream = _file.Handle.OpenStream(FileAccess.ReadWrite);
            }
            catch { Dispose(); throw; }
        }
        internal override Stream Stream => _stream ?? throw new ObjectDisposedException(nameof(ExportDestination));
        internal override void Verify()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            foreach (var parent in _parents) RejectReparse(parent.Handle);
            PrivateForOwner((_file ?? throw new ObjectDisposedException(nameof(ExportDestination))).Handle, _owner, directory: false, allowDeletePending: !_published);
        }
        internal override (UInt128 Capacity, UInt128 Available) Space() { Verify(); return ReadSpace(Parent); }
        internal override void FlushDurable() { Verify(); _stream!.Flush(flushToDisk: true); Verify(); }
        internal override void PublishNew(string name)
        {
            ValidateName(name); Verify();
            if (_attempted) throw new InvalidOperationException("Choose a fresh output owner after any publication attempt.");
            _attempted = true;
            try { Rename(_file!.Handle, Parent, name, replace: false); }
            catch (IOException error) when ((error.HResult & 0xffff) is 80 or 183)
            { throw new AnalysisExportTargetExistsException(name, error); }
            try
            {
                KeepPublishedStaging(_file.Handle); _published = true;
                FlushDurable(); Verify();
            }
            catch (Exception error) { throw new AnalysisExportUncertainException(name, error); }
        }
        public override void Dispose()
        {
            if (_disposed) return; _disposed = true; var errors = new List<Exception>();
            try { _stream?.Dispose(); } catch (Exception error) { errors.Add(error); }
            try { _file?.Dispose(); } catch (Exception error) { errors.Add(error); }
            for (int i = _parents.Count - 1; i >= 0; i--) try { _parents[i].Dispose(); } catch (Exception error) { errors.Add(error); }
            _stream = null; _file = null; _parents.Clear();
            if (errors.Count != 0) throw new AggregateException(errors);
        }
    }
}
