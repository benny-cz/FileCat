using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using FileCat.Platform.Windows.Mtp;

namespace FileCat.Platform.Windows.Tests;

/// <summary>
/// What FileCat offers on a phone or camera: what the device's driver lists as supported, as far as the storage allows.
/// The owner's iPhone lists deleting and no command to create anything or set a name, while its storage says read-write;
/// FileCat used to offer F7, renaming and copying onto it all the same. No device is needed here: the answers are given.
/// </summary>
public sealed class MtpCapabilityTests : IDisposable
{
    private static readonly DeviceAbilities IPhone = new(CreateFolders: false, CreateFiles: false, Rename: false, Delete: true);
    private static readonly DeviceAbilities ReadsOnly = new(CreateFolders: false, CreateFiles: false, Rename: false, Delete: false);
    private const LocationCapabilities Read = LocationCapabilities.Enumerate | LocationCapabilities.ReadContent;
    private const LocationCapabilities Writes = LocationCapabilities.CreateDirectory | LocationCapabilities.TransferTarget | LocationCapabilities.Rename;

    private readonly string _local = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "filecat-mtp-caps", Guid.NewGuid().ToString("N")[..8])).FullName;

    public void Dispose()
    {
        try { Directory.Delete(_local, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void An_iPhone_is_offered_for_copying_off_and_deleting_only()
    {
        Assert.Equal(Read | LocationCapabilities.Delete, MtpProvider.Offered(IPhone, StorageAccess.ReadWrite));
        Assert.Equal("the device does not let a computer create folders on it; it offers its files to copy off and to delete, as iPhones do.",
            MtpProvider.Refusal(IPhone, StorageAccess.ReadWrite, LocationCapabilities.CreateDirectory));
        Assert.Contains("add files to it", MtpProvider.Refusal(IPhone, StorageAccess.ReadWrite, LocationCapabilities.TransferTarget));
        Assert.Contains("rename its files", MtpProvider.Refusal(IPhone, StorageAccess.ReadWrite, LocationCapabilities.Rename));
        // What it does allow keeps the general explanations: no Recycle Bin, no moving off a device in one step.
        foreach (var allowed in new[] { LocationCapabilities.Delete, LocationCapabilities.Recycle, LocationCapabilities.MoveSource, LocationCapabilities.Watch })
            Assert.Null(MtpProvider.Refusal(IPhone, StorageAccess.ReadWrite, allowed));
    }

    [Fact]
    public void A_device_that_lists_every_command_is_offered_everything_its_storage_allows()
    {
        Assert.Equal(Read | Writes | LocationCapabilities.Delete, MtpProvider.Offered(DeviceAbilities.Unknown, StorageAccess.ReadWrite));
        Assert.Equal(Read, MtpProvider.Offered(DeviceAbilities.Unknown, StorageAccess.ReadOnly));
        Assert.Equal(Read | LocationCapabilities.Delete, MtpProvider.Offered(DeviceAbilities.Unknown, StorageAccess.ReadOnlyWithDeletion));
        foreach (var change in new[] { LocationCapabilities.CreateDirectory, LocationCapabilities.TransferTarget, LocationCapabilities.Rename, LocationCapabilities.Delete })
        {
            Assert.Null(MtpProvider.Refusal(DeviceAbilities.Unknown, StorageAccess.ReadWrite, change));
            Assert.Contains("read-only, so nothing on it can be added, renamed or deleted", MtpProvider.Refusal(DeviceAbilities.Unknown, StorageAccess.ReadOnly, change));
        }
        Assert.Contains("read-only apart from deleting", MtpProvider.Refusal(DeviceAbilities.Unknown, StorageAccess.ReadOnlyWithDeletion, LocationCapabilities.Rename));
        Assert.Null(MtpProvider.Refusal(DeviceAbilities.Unknown, StorageAccess.ReadOnlyWithDeletion, LocationCapabilities.Delete));
    }

    [Fact]
    public void Each_missing_command_takes_away_only_its_own_change()
    {
        Assert.Equal(Read, MtpProvider.Offered(ReadsOnly, StorageAccess.ReadWrite));
        Assert.Equal("the device does not let a computer delete its files; it offers its files to copy off only.",
            MtpProvider.Refusal(ReadsOnly, StorageAccess.ReadWrite, LocationCapabilities.Delete));
        var noRenames = DeviceAbilities.Unknown with { Rename = false };
        Assert.Equal(Read | LocationCapabilities.CreateDirectory | LocationCapabilities.TransferTarget | LocationCapabilities.Delete,
            MtpProvider.Offered(noRenames, StorageAccess.ReadWrite));
        Assert.Equal("the device does not let a computer rename its files.", MtpProvider.Refusal(noRenames, StorageAccess.ReadWrite, LocationCapabilities.Rename));
        Assert.Equal(Read | LocationCapabilities.TransferTarget | LocationCapabilities.Rename | LocationCapabilities.Delete,
            MtpProvider.Offered(DeviceAbilities.Unknown with { CreateFolders = false }, StorageAccess.ReadWrite));
    }

    [Fact]
    public void Inside_a_storage_the_explanation_is_the_devices_and_its_list_and_storages_stay_as_they_were()
    {
        var mtp = new MtpProvider();
        mtp.Assume("an-iphone", IPhone, new Dictionary<string, StorageAccess> { ["Internal Storage"] = StorageAccess.ReadWrite });
        var storage = new Location(Schemes.Mtp, "Internal Storage", session: "an-iphone");
        var folder = storage.WithPath("Internal Storage/DCIM/100APPLE");
        Assert.Equal(Read | LocationCapabilities.Delete, mtp.GetCapabilities(folder));
        Assert.Equal(Read | LocationCapabilities.Delete, mtp.GetCapabilities(storage));
        Assert.StartsWith("The device does not let a computer create folders on it", mtp.ExplainUnavailable(folder, LocationCapabilities.CreateDirectory));
        Assert.Equal("Devices do not report changes; press Ctrl+R to refresh.", mtp.ExplainUnavailable(folder, LocationCapabilities.Watch));
        Assert.Equal(LocationCapabilities.Enumerate, mtp.GetCapabilities(storage.WithPath(string.Empty)));
        Assert.Equal(LocationCapabilities.Enumerate, mtp.GetCapabilities(MtpProvider.Devices));
        // A device FileCat has not opened yet: everything, and the device decides.
        Assert.Equal(Read | Writes | LocationCapabilities.Delete, mtp.GetCapabilities(new Location(Schemes.Mtp, "Internal shared storage/Download", session: "another")));
    }

    /// <summary>
    /// Every way a change reaches a device (a drop, a typed destination, a resumed job) ends in its job: the job refuses
    /// with the reason, before anything is sent. The device here does not exist, so any attempt to reach it would fail
    /// with a different message.
    /// </summary>
    [Fact]
    public async Task A_job_refuses_what_the_device_does_not_allow_before_reaching_it()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Windows Portable Devices exist only on Windows.");
        var mtp = new MtpProvider();
        mtp.Assume("an-iphone", IPhone, new Dictionary<string, StorageAccess> { ["Internal Storage"] = StorageAccess.ReadWrite });
        mtp.Assume("a-camera", DeviceAbilities.Unknown, new Dictionary<string, StorageAccess> { ["SD"] = StorageAccess.ReadOnly });
        var providers = new ProviderRegistry();
        providers.Register(new WindowsFileSystemProvider());
        providers.Register(mtp);
        MtpJobs.Register();
        var jobs = new JobManager(new WindowsFileOperations(), providers, Path.Combine(_local, "journal"));
        string file = Path.Combine(_local, "photo.jpg");
        File.WriteAllBytes(file, [1, 2, 3]);
        var phone = new Location(Schemes.Mtp, "Internal Storage/DCIM", session: "an-iphone");
        var card = new Location(Schemes.Mtp, "SD/DCIM", session: "a-camera");

        var cases = new (JobRequest Request, string Expected)[]
        {
            (new JobRequest { Kind = JobKind.Copy, Sources = [ItemRef.ForFileSystemPath(file, EntryKind.File)], Destination = phone },
                "Not copied: the device does not let a computer add files to it; it offers its files to copy off and to delete, as iPhones do."),
            (new JobRequest { Kind = JobKind.Move, Sources = [ItemRef.ForFileSystemPath(file, EntryKind.File)], Destination = phone },
                "Not copied: the device does not let a computer add files to it; it offers its files to copy off and to delete, as iPhones do."),
            (new JobRequest { Kind = JobKind.CreateDirectory, Destination = phone, NewName = "New folder" },
                "Not created: the device does not let a computer create folders on it; it offers its files to copy off and to delete, as iPhones do."),
            (new JobRequest { Kind = JobKind.Rename, Sources = [new ItemRef(phone, "IMG_0001.HEIC", EntryKind.File)], NewName = "renamed.HEIC" },
                "Not renamed: the device does not let a computer rename its files; it offers its files to copy off and to delete, as iPhones do."),
            (new JobRequest { Kind = JobKind.Delete, Sources = [new ItemRef(card, "IMG_0001.JPG", EntryKind.File)] },
                "Not deleted: the device offers this storage read-only, so nothing on it can be added, renamed or deleted; copy files off it with F5."),
        };
        foreach (var (request, expected) in cases)
        {
            var job = jobs.Submit(request);
            for (int i = 0; i < 1000 && !job.State.IsFinished(); i++) await Task.Delay(5, TestContext.Current.CancellationToken);
            Assert.True(job.State.IsFinished(), $"{request.Kind} did not finish");
            Assert.Equal(expected, Assert.Single(job.Issues).Message);
            Assert.NotEqual(JobState.Completed, job.State);
        }
        Assert.True(File.Exists(file), "A refused move must leave its source alone.");
    }
}
