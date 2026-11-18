using FileCat.Core.Analysis;
using FileCat.Core.Jobs;
using FileCat.Platform.Windows.Analysis;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Platform.Windows.Elevation;

internal interface INtfsMetadataAdmission : IDisposable
{
    bool ConsentIsCurrent { get; }
    void Verify();
    ElevatedStepResult Run(Func<bool> stopRequested);
}

public static partial class ElevationBroker
{
    internal static INtfsMetadataAdmission AdmitNtfsMetadata(ElevationPlan plan, string brokerDirectory)
    {
        if (plan.Steps is not [var step] || ElevationPlanCodec.MetadataStepProblem(step) is not null ||
            !NtfsMetadataProtocol.ValidNonce(plan.Nonce) || plan.RequesterProcessId <= 0)
            throw new InvalidDataException("The broker requires one fixed NTFS metadata scope.");
        var scope = step.NtfsMetadata!;
        string helperImage = Path.Combine(brokerDirectory, ExecutableName);
        string requesterImage = Path.Combine(brokerDirectory, RequesterExecutableName);
        using var helper = NtfsMetadataPeer.HoldSelf(elevated: true);
        helper.RequireIdentity(helperImage, scope.RequesterSession, elevated: true);
        if (!HasTrustedExecutable(helperImage) || !HasTrustedExecutable(requesterImage))
            throw new UnauthorizedAccessException("The installed NTFS metadata helper and requester could not be verified.");

        var requester = NtfsMetadataPeer.Hold((uint)plan.RequesterProcessId);
        SafeFileHandle? image = null, root = null;
        try
        {
            requester.RequireIdentity(requesterImage, helper.Session, elevated: false);
            if (requester.Creation != scope.RequesterCreatedFileTime || requester.LogonSid.Value != scope.RequesterLogonSid ||
                requester.UserSid.Value != plan.UserSid)
                throw new UnauthorizedAccessException("The recorded NTFS metadata requester has changed.");
            image = OpenNtfsMetadataImage(requesterImage);
            if (ImageId(image) != scope.RequesterImageId)
                throw new UnauthorizedAccessException("The installed requester identity has changed.");
            var volume = new NtfsMetadataVolume(scope.Volume, scope.VolumeSerial, scope.RootFileId);
            root = volume.ReopenRoot(CancellationToken.None);
            return new NtfsMetadataAdmission(plan, requesterImage, requester, image, volume, root);
        }
        catch { root?.Dispose(); image?.Dispose(); requester.Dispose(); throw; }
    }

    internal static SafeFileHandle OpenNtfsMetadataImage(string path)
    {
        var image = CreateFileForQuery(path, 0x80 /* FILE_READ_ATTRIBUTES */, 1, 0, 3, 0x00200000 /* OPEN_REPARSE_POINT */, 0);
        if (!image.IsInvalid) return image;
        image.Dispose();
        throw new IOException("The installed image identity is unavailable.");
    }

    internal static string ImageId(SafeFileHandle image)
    {
        var observed = ProtectedHexFile.Identity(image);
        return observed.VolumeSerial.ToString("X16") + observed.FileId;
    }

    private sealed class NtfsMetadataAdmission(ElevationPlan plan, string imagePath, NtfsMetadataPeer requester,
        SafeFileHandle image, NtfsMetadataVolume volume, SafeFileHandle root) : INtfsMetadataAdmission
    {
        private readonly ulong _started = NtfsMetadataChannel.MonotonicMilliseconds();
        private readonly ulong _remaining = Remaining(plan);
        private int _used;

        private bool Expired => NtfsMetadataChannel.MonotonicMilliseconds() - _started >= _remaining;
        public bool ConsentIsCurrent => !Expired && requester.IsAlive;

        public void Verify()
        {
            if (Expired)
                throw new IOException("The NTFS metadata consent has expired.");
            requester.RequireIdentity(imagePath, requester.Session, elevated: false);
            if (ImageId(image) != plan.Steps[0].NtfsMetadata!.RequesterImageId ||
                NtfsMetadataVolume.Native.Capture(volume.GuidPath, root) != volume)
                throw new AnalysisSourceChangedException();
            using var current = volume.ReopenRoot(CancellationToken.None);
        }

        public ElevatedStepResult Run(Func<bool> stopRequested)
        {
            if (Interlocked.Exchange(ref _used, 1) != 0) throw new InvalidOperationException("This NTFS metadata admission has ended.");
            Verify();
            return NtfsMetadataHost.RunAsync(volume, plan.Nonce, requester, stopRequested).GetAwaiter().GetResult();
        }

        public void Dispose()
        {
            root.Dispose();
            image.Dispose();
            requester.Dispose();
        }

        private static ulong Remaining(ElevationPlan plan)
        {
            double milliseconds = (plan.CreatedUtc + ElevationPlanCodec.MaxAge - DateTime.UtcNow).TotalMilliseconds;
            return (ulong)Math.Clamp(milliseconds, 0, ElevationPlanCodec.MaxAge.TotalMilliseconds);
        }
    }
}
