using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using FileCat.Core.FileSystem;
using FileCat.Core.Permissions;
using FileCat.Core.Resources;
using FileCat.Platform.Windows.Permissions;

namespace FileCat.Platform.Windows.Tests;

/// <summary>
/// G-WIN fetch sentinel on a second on-demand provider class: the Windows Projected File System (ProjFS, as VFS for Git
/// uses). The test virtualizes a folder of its own, projects one file, and counts every request for its data while
/// FileCat inspects the projected file, the virtualization root and a folder above it. Requires the Client-ProjFS
/// feature and FILECAT_TEST_PROJFS=1.
/// </summary>
public sealed unsafe class WindowsPermissionProjFsTests
{
    private const string ProjectedName = "projected.txt";
    private const uint RecallOnDataAccess = 0x0040_0000;
    private static int _dataRequests, _placeholderRequests;
    private static nint _context;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int StartEnumeration(nint data, Guid* id) => 0;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int EndEnumeration(nint data, Guid* id) => 0;

    // Listings stay empty: the sentinel only needs the projected name to resolve.
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int GetEnumeration(nint data, Guid* id, char* search, nint buffer) => 0;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int GetPlaceholderInfo(CallbackData* data)
    {
        Interlocked.Increment(ref _placeholderRequests);
        string name = new(data->FilePathName);
        if (!string.Equals(name, ProjectedName, StringComparison.OrdinalIgnoreCase)) return unchecked((int)0x80070002); // not found
        // PRJ_PLACEHOLDER_INFO: PRJ_FILE_BASIC_INFO (56 bytes) then the EA, security and stream ranges and version info.
        byte* info = stackalloc byte[PlaceholderInfoSize];
        new Span<byte>(info, PlaceholderInfoSize).Clear();
        long now = DateTime.UtcNow.ToFileTimeUtc();
        *(long*)(info + 8) = 5000;          // FileSize
        for (int field = 0; field < 4; field++) *(long*)(info + 16 + 8 * field) = now;
        *(uint*)(info + 48) = 0x80;         // FILE_ATTRIBUTE_NORMAL
        return PrjWritePlaceholderInfo(_context, data->FilePathName, info, PlaceholderInfoSize);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int GetFileData(CallbackData* data, ulong offset, uint length)
    {
        Interlocked.Increment(ref _dataRequests);
        return unchecked((int)0x80070005); // The sentinel serves no data.
    }

    private const int PlaceholderInfoSize = 344; // sizeof(PRJ_PLACEHOLDER_INFO) on x64

    [Fact]
    public void Permission_inspection_never_asks_a_ProjFS_provider_for_data()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "ProjFS is Windows'.");
        if (Environment.GetEnvironmentVariable("FILECAT_TEST_PROJFS") != "1")
            Assert.Skip("Set FILECAT_TEST_PROJFS=1 to virtualize a temporary folder with the Projected File System.");
        if (!NativeLibrary.TryLoad("ProjectedFSLib.dll", out nint library)) Assert.Skip("The Client-ProjFS feature is not installed.");
        NativeLibrary.Free(library);
        var envelope = Directory.CreateTempSubdirectory("filecat-projfs-");
        string root = Directory.CreateDirectory(Path.Combine(envelope.FullName, "virtual")).FullName;
        string projected = Path.Combine(root, ProjectedName);
        var instance = Guid.NewGuid();
        bool started = false;
        try
        {
            int marked = PrjMarkDirectoryAsPlaceholder(root, null, null, &instance);
            Assert.True(marked >= 0, $"PrjMarkDirectoryAsPlaceholder: 0x{marked:X8}");
            var callbacks = new Callbacks
            {
                StartDirectoryEnumeration = (nint)(delegate* unmanaged[Stdcall]<nint, Guid*, int>)&StartEnumeration,
                EndDirectoryEnumeration = (nint)(delegate* unmanaged[Stdcall]<nint, Guid*, int>)&EndEnumeration,
                GetDirectoryEnumeration = (nint)(delegate* unmanaged[Stdcall]<nint, Guid*, char*, nint, int>)&GetEnumeration,
                GetPlaceholderInfo = (nint)(delegate* unmanaged[Stdcall]<CallbackData*, int>)&GetPlaceholderInfo,
                GetFileData = (nint)(delegate* unmanaged[Stdcall]<CallbackData*, ulong, uint, int>)&GetFileData,
            };
            int start = PrjStartVirtualizing(root, &callbacks, 0, 0, out _context);
            Assert.True(start >= 0, $"PrjStartVirtualizing: 0x{start:X8}");
            started = true;
            int dataBefore = Volatile.Read(ref _dataRequests);
            Assert.True(File.Exists(projected), "The projected file did not resolve.");
            Assert.Equal(5000, new FileInfo(projected).Length);

            var ct = TestContext.Current.CancellationToken;
            var permissions = new WindowsPermissionService();
            var authority = new PermissionAuthority(PermissionAuthorityKind.Local, "projfs-fixture", 0);
            using (var file = permissions.Inspect(new PermissionSubject(Location.FileSystem(root), projected, projected, authority,
                       PermissionObjectKind.File, PermissionLinkPolicy.NoFollow), PermissionSections.Ordinary, ct))
            {
                Assert.Null(file.Snapshot.Binding);
                Assert.Equal(PermissionReadStatus.Unsupported, file.Snapshot.Access.Status);
                Assert.NotNull(WindowsPermissionService.EditRefusal(file.Snapshot));
            }
            var notes = new List<string> { $"root attributes 0x{(uint)File.GetAttributes(root):X8}, projected attributes 0x{(uint)File.GetAttributes(projected):X8}" };
            var qualified = new WindowsPermissionService(qualifyFolderOnly: true, qualifyNativePropagation: true);
            using (var folder = qualified.Inspect(new PermissionSubject(Location.FileSystem(envelope.FullName), root, root, authority,
                       PermissionObjectKind.Directory, PermissionLinkPolicy.NoFollow), PermissionSections.Ordinary, ct))
            {
                notes.Add($"root: access {folder.Snapshot.Access.Status} ({folder.Snapshot.Access.Reason}), binding {(folder.Snapshot.Binding is null ? "none" : "held")}, " +
                    $"folder-only {qualified.CanReviewFolderOnly(folder.Snapshot)}, propagation {qualified.CanReviewNativePropagation(folder.Snapshot)}");
                if (folder.Snapshot.Binding is not null)
                    notes.Add("root propagation boundary: " + Outcome(() => WindowsPermissionService.CapturePropagationBoundary(folder.Snapshot, ct)));
            }
            var above = new PermissionSubject(Location.FileSystem(Path.GetDirectoryName(envelope.FullName)!), envelope.FullName, envelope.FullName,
                authority, PermissionObjectKind.Directory, PermissionLinkPolicy.NoFollow);
            using (var parent = permissions.Inspect(above, PermissionSections.None, ct))
            {
                Assert.True(parent.Snapshot.Binding is not null, parent.Snapshot.BindingProblem);
                notes.Add("parent propagation boundary: " + Outcome(() => WindowsPermissionService.CapturePropagationBoundary(parent.Snapshot, ct)));
            }
            int requested = Volatile.Read(ref _dataRequests) - dataBefore;
            notes.Add($"ProjFS sentinel: {requested} data requests, {Volatile.Read(ref _placeholderRequests)} placeholder requests.");
            foreach (string note in notes) TestContext.Current.SendDiagnosticMessage(note);
            // Windows 11 26200: ProjFS hides the virtualization root's reparse point, so the root reads as an ordinary folder
            // whose own DACL can be edited, while its projected file carries RECALL_ON_DATA_ACCESS and is refused, and no
            // propagation boundary can be captured through it.
            Assert.Equal(0u, (uint)File.GetAttributes(root) & 0x400u);
            Assert.NotEqual(0u, (uint)File.GetAttributes(projected) & RecallOnDataAccess);
            Assert.All(notes.Where(note => note.Contains("propagation boundary", StringComparison.Ordinal)),
                note => Assert.Contains("requires recall", note, StringComparison.Ordinal));
            Assert.True(requested == 0, $"The provider received {requested} data requests.");
            File.Delete(projected);
        }
        finally
        {
            if (started) PrjStopVirtualizing(_context);
            try { envelope.Delete(recursive: true); } catch (IOException) { }
        }
    }

    private static string Outcome(Func<object> capture)
    {
        try { return "captured " + capture(); }
        catch (Exception error) { return $"{error.GetType().Name}: {error.Message}"; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Callbacks
    {
        public nint StartDirectoryEnumeration, EndDirectoryEnumeration, GetDirectoryEnumeration, GetPlaceholderInfo, GetFileData,
            QueryFileName, Notification, CancelCommand;
    }

    // PRJ_CALLBACK_DATA
    [StructLayout(LayoutKind.Sequential)]
    private struct CallbackData
    {
        public uint Size;
        public int Flags;
        public nint NamespaceVirtualizationContext;
        public int CommandId;
        public Guid FileId, DataStreamId;
        public char* FilePathName;
        public nint VersionInfo;
        public uint TriggeringProcessId;
        public char* TriggeringProcessImageFileName;
        public nint InstanceContext;
    }

    [DllImport("ProjectedFSLib.dll", CharSet = CharSet.Unicode)]
    private static extern int PrjMarkDirectoryAsPlaceholder(string root, string? target, void* versionInfo, Guid* instance);

    [DllImport("ProjectedFSLib.dll", CharSet = CharSet.Unicode)]
    private static extern int PrjStartVirtualizing(string root, Callbacks* callbacks, nint instanceContext, nint options, out nint context);

    [DllImport("ProjectedFSLib.dll")]
    private static extern void PrjStopVirtualizing(nint context);

    [DllImport("ProjectedFSLib.dll")]
    private static extern int PrjWritePlaceholderInfo(nint context, char* destination, byte* info, uint size);
}
