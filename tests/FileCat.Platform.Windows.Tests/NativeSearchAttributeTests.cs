using System.Runtime.InteropServices;
using FileCat.Core.Search;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Platform.Windows.Tests;

public sealed partial class NativeSearchAttributeTests
{
    [Fact]
    public unsafe void Raw_reparse_sparse_and_offline_predicates_use_the_native_bits_without_content_search()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "Windows native attribute fixture.");
        string root = Directory.CreateTempSubdirectory("filecat-find-attributes-").FullName;
        try
        {
            string plain = Path.Combine(root, "plain"), sparse = Path.Combine(root, "sparse"), offline = Path.Combine(root, "offline"), link = Path.Combine(root, "link");
            foreach (string path in new[] { plain, sparse, offline }) File.WriteAllText(path, "untouched payload");
            using (var file = File.OpenHandle(sparse, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete))
                Assert.True(Control(file, 0x900C4 /* FSCTL_SET_SPARSE */, null, 0, null, 0, out _, 0), $"SET_SPARSE: {Marshal.GetLastPInvokeError()}");
            File.SetAttributes(offline, File.GetAttributes(offline) | FileAttributes.Offline);
            File.CreateSymbolicLink(link, plain);
            foreach (var (attribute, name) in new[] { (FileAttributes.SparseFile, "sparse"), (FileAttributes.Offline, "offline"), (FileAttributes.ReparsePoint, "link") })
            {
                var yes = new ResultSet("set", "set", "native fixture");
                var required = new SearchSession(new SearchQuery { Roots = [root], AttributesSet = attribute }, yes);
                required.Run(TestContext.Current.CancellationToken);
                Assert.Equal(name, Assert.Single(yes.Snapshot()).Item.Name); Assert.Equal(0, required.FilesExamined); Assert.True(yes.IsComplete);
                var no = new ResultSet("clear", "clear", "native fixture");
                var excluded = new SearchSession(new SearchQuery { Roots = [root], AttributesClear = attribute }, no);
                excluded.Run(TestContext.Current.CancellationToken);
                Assert.Equal(3, no.Count); Assert.DoesNotContain(no.Snapshot(), r => r.Item.Name == name); Assert.Equal(0, excluded.FilesExamined); Assert.True(no.IsComplete);
            }
            Assert.Equal("untouched payload", File.ReadAllText(plain));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [LibraryImport("kernel32.dll", EntryPoint = "DeviceIoControl", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool Control(SafeFileHandle file, uint code, void* input, uint inputLength, void* output, uint outputLength, out uint returned, nint overlapped);
}
