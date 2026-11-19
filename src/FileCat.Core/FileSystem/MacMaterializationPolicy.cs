using System.Runtime.InteropServices;

namespace FileCat.Core.FileSystem;

internal static partial class MacMaterializationPolicy
{
    internal const int Off = 1; // IOPOL_MATERIALIZE_DATALESS_FILES_OFF
    internal const int Required = 11; // Darwin EDEADLK when a lookup requires materialization under this policy
    internal const uint DatalessFlag = 0x40000000; // SF_DATALESS
    private const int MaterializeDatalessFiles = 3, ThreadScope = 1;

    internal static int Get() => GetIoPolicy(MaterializeDatalessFiles, ThreadScope);
    internal static int Set(int policy) => SetIoPolicy(MaterializeDatalessFiles, ThreadScope, policy);

    [LibraryImport("libc", EntryPoint = "getiopolicy_np", SetLastError = true)]
    private static partial int GetIoPolicy(int type, int scope);

    [LibraryImport("libc", EntryPoint = "setiopolicy_np", SetLastError = true)]
    private static partial int SetIoPolicy(int type, int scope, int policy);
}
