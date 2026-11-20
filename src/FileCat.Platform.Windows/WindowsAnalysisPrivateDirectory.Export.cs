using System.Runtime.InteropServices;
using System.Security.Principal;
using FileCat.Core.Analysis;
using static FileCat.Platform.Windows.Elevation.NtFile;

namespace FileCat.Platform.Windows;

public sealed unsafe partial class WindowsAnalysisPrivateDirectory
{
    /// <summary>Private delete-on-close staging in an explicitly selected local NTFS folder; no existing target is replaced.</summary>
    public static AnalysisExportDestination OpenExportDestination(string folder, IReadOnlySet<string>? protectedDirectories = null)
    {
        var reviewed = new ReviewedOutputFolder(folder, protectedDirectories);
        try { return reviewed.CreateStage(forExport: true); }
        catch { AnalysisIo.CloseOnWorker(reviewed); throw; }
    }

    /// <summary>A reviewed ordinary NTFS folder. Holding this lease creates no output file.</summary>
    internal sealed class ReviewedOutputFolder : IAnalysisScratchOwned
    {
        private readonly List<OwnedHandle> _parents = [];
        private readonly SecurityIdentifier _owner;
        private bool _disposed;
        internal string DisplayPath { get; }
        internal string Identity { get; }
        internal SafeHandle Parent => _parents[^1].Handle;

        internal ReviewedOutputFolder(string folder, IReadOnlySet<string>? protectedDirectories)
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
            if (AnalysisAdmission.Refusal() is { } reason) throw new InvalidOperationException(reason);
            AnalysisExportDestination.ValidateFolder(folder);
            string drive = Path.GetPathRoot(folder) ?? "";
            if (drive.Length != 3 || drive[1] != ':' || drive[2] != '\\' || GetDriveType(drive) is not (2 or 3) || folder.Contains('/'))
                throw new NotSupportedException("Choose an output folder on a directly accessible local NTFS drive.");
            DisplayPath = folder;
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
                Verify();
                _ = ReadSpace(Parent);
                Identity = Analysis.WindowsAnalysisBackend.CaptureDirectoryIdentity(Parent) ??
                    throw new IOException("The output folder has no verified identity.");
                if (protectedDirectories is not null) VerifyProtection(protectedDirectories);
            }
            catch { AnalysisIo.CloseOnWorker(this); throw; }
        }

        internal void Verify()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            foreach (var parent in _parents) RejectReparse(parent.Handle);
        }

        internal void VerifyProtection(IReadOnlySet<string> protectedDirectories)
        {
            Verify();
            if (protectedDirectories.Count > 64) throw new NotSupportedException("The protected output boundary exceeds its finite limit.");
            foreach (var parent in _parents)
                if (Analysis.WindowsAnalysisBackend.CaptureDirectoryIdentity(parent.Handle) is not { } id || protectedDirectories.Contains(id))
                    throw new IOException("The chosen folder is within protected FileCat artifacts. Choose another folder.");
            if (Analysis.WindowsAnalysisBackend.CaptureDirectoryIdentity(Parent) != Identity)
                throw new AnalysisSourceChangedException();
        }

        internal (UInt128 Capacity, UInt128 Available) Space()
        {
            Verify();
            return ReadSpace(Parent);
        }

        internal void RequireNewCopyName(string name)
        {
            StagedOutput.ValidateCopyName(name);
            Verify();
            var reservation = AnalysisNativeHandles.Acquire();
            OwnedHandle? existing = null;
            try
            {
                int status = TryOpen(Parent, name, ReadAttributes | Synchronize, ShareAll, Open, OpenReparsePoint, out var handle);
                using var opened = handle;
                if (status == NameNotFound) return;
                if (status < 0) throw Error(status, "Check the reviewed output name");
                existing = new(opened, reservation);
            }
            finally { if (existing is null) reservation.Dispose(); }
            existing.Dispose();
            throw new AnalysisExportTargetExistsException(name, new IOException("The destination name is already in use."));
        }

        internal StagedOutput CreateStage(bool forExport)
        {
            Verify();
            string prefix = forExport ? ".filecat-export-" : ".filecat-copy-";
            var file = OpenOwned(Parent, prefix + Guid.NewGuid().ToString("N"), GenericRead | GenericWrite | Delete | ReadControl | Synchronize,
                0, Create, NonDirectoryFile | OpenReparsePoint | 0x1000, DescriptorForOwner(_owner, directory: false));
            return new StagedOutput(this, _owner, file.Handle, forExport);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { AnalysisNativeHandle.CloseAll(_parents.AsEnumerable().Reverse()); }
            finally { _parents.Clear(); }
        }
    }

    internal sealed class StagedOutput : AnalysisExportDestination
    {
        private readonly ReviewedOutputFolder _folder;
        private readonly SecurityIdentifier _owner;
        private readonly AnalysisNativeHandle _file;
        private readonly bool _forExport;
        private readonly FileStream? _stream;
        private bool _disposed, _attempted, _published;
        internal AnalysisNativeHandle File => _file;

        internal StagedOutput(ReviewedOutputFolder folder, SecurityIdentifier owner, AnalysisNativeHandle file, bool forExport)
        {
            _folder = folder;
            _owner = owner;
            _file = file;
            _forExport = forExport;
            try
            {
                Verify();
                if (forExport) _stream = _file.OpenStream(FileAccess.ReadWrite);
            }
            catch { AnalysisIo.CloseOnWorker(_file); throw; }
        }

        internal override Stream Stream => _stream ?? throw new ObjectDisposedException(nameof(StagedOutput));
        internal override void Verify()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _folder.Verify();
            PrivateForOwner(_file, _owner, directory: false, allowDeletePending: !_published);
        }
        internal override (UInt128 Capacity, UInt128 Available) Space() { Verify(); return _folder.Space(); }
        internal override void FlushDurable()
        {
            Verify();
            if (_stream is not null) _stream.Flush(flushToDisk: true);
            else
            {
                using var file = _file.BorrowFile();
                RandomAccess.FlushToDisk(file.View);
            }
            Verify();
        }
        internal override void PublishNew(string name)
        {
            if (!_forExport) throw new InvalidOperationException("Use the reviewed copy publication boundary.");
            ValidateName(name);
            Publish(name);
        }
        internal void PublishCopy(string name)
        {
            if (_forExport) throw new InvalidOperationException("Use the reviewed export publication boundary.");
            ValidateCopyName(name);
            Publish(name);
        }
        internal static void ValidateCopyName(string name)
        {
            if (!AnalysisNames.IsOrdinaryChild(name, windows: true) || name.Length > 255 || !AnalysisPrivateInventoryStore.WellFormedText(name))
                throw new ArgumentException("Choose one ordinary NTFS filename.", nameof(name));
        }
        private void Publish(string name)
        {
            Verify();
            if (_attempted) throw new InvalidOperationException("Choose a fresh output owner after any publication attempt.");
            _attempted = true;
            try { Rename(_file, _folder.Parent, name, replace: false); }
            catch (IOException error) when ((error.HResult & 0xffff) is 80 or 183)
            { throw new AnalysisExportTargetExistsException(name, error); }
            try
            {
                KeepPublishedStaging(_file); _published = true;
                FlushDurable(); Verify();
            }
            catch (Exception error) { throw new AnalysisExportUncertainException(name, error); }
        }
        public override void Dispose()
        {
            if (_disposed) return; _disposed = true; var errors = new List<Exception>();
            try { _stream?.Dispose(); } catch (Exception error) { errors.Add(error); }
            try { _file.CloseChecked(); } catch (Exception error) { errors.Add(error); }
            if (_forExport)
                try { _folder.Dispose(); } catch (Exception error) { errors.Add(error); }
            if (errors.Count != 0) throw new AggregateException(errors);
        }
    }
}
