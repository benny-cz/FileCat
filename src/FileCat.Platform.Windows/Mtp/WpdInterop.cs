using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace FileCat.Platform.Windows.Mtp;

/// <summary>
/// Windows Portable Devices (WPD) COM declarations for phones, cameras, and players over MTP (P8). Methods are declared in
/// the exact vtable order of PortableDeviceApi.idl and PortableDeviceTypes.idl; slots FileCat never calls keep
/// placeholder signatures only to hold their place.
/// </summary>
internal static class Wpd
{
    public static readonly Guid CLSID_PortableDeviceManager = new("0af10cec-2ecd-4b92-9581-34f6ae0637f3");
    public static readonly Guid CLSID_PortableDeviceFTM = new("f7c0039a-4762-488a-b4b3-760ef9a1ba9b");
    public static readonly Guid CLSID_PortableDeviceValues = new("0c15d503-d017-47ce-9016-7b3f978721cc");
    public static readonly Guid CLSID_PortableDeviceKeyCollection = new("de2d022d-2480-43be-97f0-d1fa2cf98f4f");
    public static readonly Guid CLSID_PortableDevicePropVariantCollection = new("08a99e2f-6d6d-4b80-af5a-baf2bcbe4cb9");

    public const string DeviceObjectId = "DEVICE";

    private static readonly Guid ObjectProperties = new("EF6B490D-5CD8-437A-AFFC-DA8B60EE4A3C");
    public static readonly PropertyKey ObjectId = new(ObjectProperties, 2);
    public static readonly PropertyKey ParentId = new(ObjectProperties, 3);
    public static readonly PropertyKey Name = new(ObjectProperties, 4);
    public static readonly PropertyKey PersistentUniqueId = new(ObjectProperties, 5);
    public static readonly PropertyKey Format = new(ObjectProperties, 6);
    public static readonly PropertyKey ContentType = new(ObjectProperties, 7);
    public static readonly PropertyKey IsHidden = new(ObjectProperties, 9);
    public static readonly PropertyKey IsSystem = new(ObjectProperties, 10);
    public static readonly PropertyKey Size = new(ObjectProperties, 11);
    public static readonly PropertyKey OriginalFileName = new(ObjectProperties, 12);
    public static readonly PropertyKey DateModified = new(ObjectProperties, 19);
    public static readonly PropertyKey CanDelete = new(ObjectProperties, 26);

    private static readonly Guid StorageProperties = new("01A3057A-74D6-4E80-BEA7-DC4C212CE50A");
    public static readonly PropertyKey StorageCapacity = new(StorageProperties, 4);
    public static readonly PropertyKey StorageFreeSpace = new(StorageProperties, 5);
    public static readonly PropertyKey StorageAccessCapability = new(StorageProperties, 11);

    public static readonly PropertyKey ResourceDefault = new(new Guid("E81E79BE-34F0-41BF-B53F-F1A06AE87842"), 0);

    // Commands a driver lists as supported (an iPhone's lists deleting, and none of creating or setting properties).
    private static readonly Guid ObjectManagementCommands = new("EF1E43DD-A9ED-4341-8BCC-186192AEA089");
    public static readonly PropertyKey CommandCreateWithPropertiesOnly = new(ObjectManagementCommands, 2);
    public static readonly PropertyKey CommandCreateWithPropertiesAndData = new(ObjectManagementCommands, 3);
    public static readonly PropertyKey CommandDeleteObjects = new(ObjectManagementCommands, 7);
    public static readonly PropertyKey CommandSetProperties = new(new Guid("9E5582E4-0814-44E6-981A-B2998D583804"), 5);

    private static readonly Guid ClientInfo = new("204D9F0C-2292-4080-9F42-40664E70F859");
    public static readonly PropertyKey ClientName = new(ClientInfo, 2);
    public static readonly PropertyKey ClientMajorVersion = new(ClientInfo, 3);
    public static readonly PropertyKey ClientMinorVersion = new(ClientInfo, 4);
    public static readonly PropertyKey ClientRevision = new(ClientInfo, 5);

    public static readonly Guid ContentFolder = new("27E2E392-A111-48E0-AB0C-E17705A05F85");
    public static readonly Guid ContentFunctionalObject = new("99ED0160-17FF-4C44-9D98-1D7A6F941921");
    public static readonly Guid ContentGenericFile = new("0085E0A6-8D34-45D7-BC5C-447E59C73D48");
    public static readonly Guid FormatUnspecified = new("30000000-AE6C-4804-98BA-C57B46965FE7");
    public static readonly Guid FormatPropertiesOnly = new("30010000-AE6C-4804-98BA-C57B46965FE7");

    public const uint STGM_READ = 0;
    public const uint DeleteWithRecursion = 1;
    public const ushort VT_LPWSTR = 31, VT_DATE = 7;

    [DllImport("ole32.dll")]
    public static extern int PropVariantClear(ref PropVariant pvar);

    public static T Create<T>(Guid clsid) where T : class =>
        (T)(Activator.CreateInstance(Type.GetTypeFromCLSID(clsid, throwOnError: true)!) ?? throw new COMException("The portable device service is unavailable."));
}

[StructLayout(LayoutKind.Sequential)]
internal readonly record struct PropertyKey(Guid FmtId, uint Pid);

/// <summary>PROPVARIANT on 64-bit Windows: a type tag, reserved words, then a 16-byte union.</summary>
[StructLayout(LayoutKind.Explicit, Size = 24)]
internal struct PropVariant
{
    [FieldOffset(0)] public ushort VarType;
    [FieldOffset(8)] public IntPtr Pointer;
    [FieldOffset(8)] public double Date;
    [FieldOffset(8)] public long Int64;
}

[ComImport, Guid("a1567595-4c2f-4574-a6fa-ecef917b9a40"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPortableDeviceManager
{
    // Arrays in COM interfaces marshal as SAFEARRAY unless declared as C arrays.
    void GetDevices([In, Out, MarshalAs(UnmanagedType.LPArray)] IntPtr[]? pnpDeviceIds, ref uint count);
    void RefreshDeviceList();
    void GetDeviceFriendlyName([MarshalAs(UnmanagedType.LPWStr)] string pnpDeviceId, [In, Out, MarshalAs(UnmanagedType.LPArray)] char[]? name, ref uint length);
    void GetDeviceDescription([MarshalAs(UnmanagedType.LPWStr)] string pnpDeviceId, [In, Out, MarshalAs(UnmanagedType.LPArray)] char[]? description, ref uint length);
    void GetDeviceManufacturer([MarshalAs(UnmanagedType.LPWStr)] string pnpDeviceId, [In, Out, MarshalAs(UnmanagedType.LPArray)] char[]? manufacturer, ref uint length);
    void GetDeviceProperty(IntPtr slot0, IntPtr slot1, IntPtr slot2, IntPtr slot3, IntPtr slot4);
    void GetPrivateDevices(IntPtr slot0, IntPtr slot1);
}

[ComImport, Guid("625e2df8-6392-4cf0-9ad1-3cfa5f17775c"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPortableDevice
{
    void Open([MarshalAs(UnmanagedType.LPWStr)] string pnpDeviceId, IPortableDeviceValues clientInfo);
    void SendCommand(uint flags, IntPtr parameters, IntPtr results);
    void Content(out IPortableDeviceContent content);
    void Capabilities(out IPortableDeviceCapabilities capabilities);
    void Cancel();
    void Close();
    void Advise(IntPtr slot0, IntPtr slot1, IntPtr slot2, IntPtr slot3);
    void Unadvise(IntPtr cookie);
    void GetPnPDeviceID(out IntPtr id);
}

[ComImport, Guid("2c8c6dbf-e3dc-4061-becc-8542e810d126"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPortableDeviceCapabilities
{
    void GetSupportedCommands(out IPortableDeviceKeyCollection commands);
    void GetCommandOptions(IntPtr slot0, IntPtr slot1);
    void GetFunctionalCategories(IntPtr slot0);
    void GetFunctionalObjects(IntPtr slot0, IntPtr slot1);
    void GetSupportedContentTypes(IntPtr slot0, IntPtr slot1);
    void GetSupportedFormats(IntPtr slot0, IntPtr slot1);
    void GetSupportedFormatProperties(IntPtr slot0, IntPtr slot1);
    void GetFixedPropertyAttributes(IntPtr slot0, IntPtr slot1, IntPtr slot2);
    void Cancel();
    void GetSupportedEvents(IntPtr slot0);
    void GetEventOptions(IntPtr slot0, IntPtr slot1);
}

/// <summary>
/// The stream CreateObjectWithPropertiesAndData returns: an IStream (its eleven methods hold their vtable places here and are
/// called through ComTypes.IStream instead), plus the ID of the object it created once committed.
/// </summary>
[ComImport, Guid("88e04db3-1012-4d64-9996-f703a950d3f4"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPortableDeviceDataStream
{
    void Read();
    void Write();
    void Seek();
    void SetSize();
    void CopyTo();
    void Commit();
    void Revert();
    void LockRegion();
    void UnlockRegion();
    void Stat();
    void Clone();
    void GetObjectID([MarshalAs(UnmanagedType.LPWStr)] out string objectId);
    void Cancel();
}

[ComImport, Guid("6a96ed84-7c73-4480-9938-bf5af477d426"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPortableDeviceContent
{
    void EnumObjects(uint flags, [MarshalAs(UnmanagedType.LPWStr)] string parentObjectId, IntPtr filter, out IEnumPortableDeviceObjectIDs objects);
    void Properties(out IPortableDeviceProperties properties);
    void Transfer(out IPortableDeviceResources resources);
    void CreateObjectWithPropertiesOnly(IPortableDeviceValues values, ref IntPtr objectId);
    void CreateObjectWithPropertiesAndData(IPortableDeviceValues values, out IStream data, ref uint optimalWriteBufferSize, IntPtr cookie);
    void Delete(uint options, IPortableDevicePropVariantCollection objectIds, IntPtr results);
    void GetObjectIDsFromPersistentUniqueIDs(IntPtr slot0, IntPtr slot1);
    void Cancel();
    void Move(IntPtr slot0, IntPtr slot1, IntPtr slot2);
    void Copy(IntPtr slot0, IntPtr slot1, IntPtr slot2);
}

[ComImport, Guid("10ece955-cf41-4728-bfa0-41eedf1bbf19"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IEnumPortableDeviceObjectIDs
{
    [PreserveSig]
    int Next(uint count, [Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] IntPtr[] objectIds, ref uint fetched);
    void Skip(uint count);
    void Reset();
    void Clone(out IEnumPortableDeviceObjectIDs clone);
    void Cancel();
}

[ComImport, Guid("7f6d695c-03df-4439-a809-59266beee3a6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPortableDeviceProperties
{
    void GetSupportedProperties([MarshalAs(UnmanagedType.LPWStr)] string objectId, out IntPtr keys);
    void GetPropertyAttributes([MarshalAs(UnmanagedType.LPWStr)] string objectId, ref PropertyKey key, out IntPtr attributes);
    void GetValues([MarshalAs(UnmanagedType.LPWStr)] string objectId, IPortableDeviceKeyCollection? keys, out IPortableDeviceValues values);
    void SetValues([MarshalAs(UnmanagedType.LPWStr)] string objectId, IPortableDeviceValues values, out IPortableDeviceValues results);
    void Delete([MarshalAs(UnmanagedType.LPWStr)] string objectId, IntPtr keys);
    void Cancel();
}

[ComImport, Guid("fd8878ac-d841-4d17-891c-e6829cdb6934"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPortableDeviceResources
{
    void GetSupportedResources([MarshalAs(UnmanagedType.LPWStr)] string objectId, out IntPtr keys);
    void GetResourceAttributes([MarshalAs(UnmanagedType.LPWStr)] string objectId, ref PropertyKey key, out IntPtr attributes);
    void GetStream([MarshalAs(UnmanagedType.LPWStr)] string objectId, ref PropertyKey key, uint mode, ref uint optimalBufferSize, out IStream stream);
    void Delete([MarshalAs(UnmanagedType.LPWStr)] string objectId, IntPtr keys);
    void Cancel();
    void CreateResource(IntPtr attributes, out IStream data, ref uint optimalWriteBufferSize, IntPtr cookie);
}

[ComImport, Guid("6848f6f2-3155-4f86-b6f5-263eeeab3143"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPortableDeviceValues
{
    void GetCount(ref uint count);
    void GetAt(uint index, ref PropertyKey key, IntPtr value);
    void SetValue(ref PropertyKey key, ref PropVariant value);
    [PreserveSig]
    int GetValue(ref PropertyKey key, out PropVariant value);
    void SetStringValue(ref PropertyKey key, [MarshalAs(UnmanagedType.LPWStr)] string value);
    [PreserveSig]
    int GetStringValue(ref PropertyKey key, out IntPtr value);
    void SetUnsignedIntegerValue(ref PropertyKey key, uint value);
    [PreserveSig]
    int GetUnsignedIntegerValue(ref PropertyKey key, out uint value);
    void SetSignedIntegerValue(ref PropertyKey key, int value);
    [PreserveSig]
    int GetSignedIntegerValue(ref PropertyKey key, out int value);
    void SetUnsignedLargeIntegerValue(ref PropertyKey key, ulong value);
    [PreserveSig]
    int GetUnsignedLargeIntegerValue(ref PropertyKey key, out ulong value);
    void SetSignedLargeIntegerValue(ref PropertyKey key, long value);
    [PreserveSig]
    int GetSignedLargeIntegerValue(ref PropertyKey key, out long value);
    void SetFloatValue(ref PropertyKey key, float value);
    void GetFloatValue(ref PropertyKey key, out float value);
    void SetErrorValue(ref PropertyKey key, int value);
    [PreserveSig]
    int GetErrorValue(ref PropertyKey key, out int value);
    void SetKeyValue(ref PropertyKey key, ref PropertyKey value);
    void GetKeyValue(ref PropertyKey key, out PropertyKey value);
    void SetBoolValue(ref PropertyKey key, int value);
    [PreserveSig]
    int GetBoolValue(ref PropertyKey key, out int value);
    void SetIUnknownValue(ref PropertyKey key, IntPtr value);
    void GetIUnknownValue(ref PropertyKey key, out IntPtr value);
    void SetGuidValue(ref PropertyKey key, ref Guid value);
    [PreserveSig]
    int GetGuidValue(ref PropertyKey key, out Guid value);
    void SetBufferValue(ref PropertyKey key, IntPtr value, uint length);
    void GetBufferValue(ref PropertyKey key, out IntPtr value, out uint length);
    void SetIPortableDeviceValuesValue(ref PropertyKey key, IntPtr value);
    void GetIPortableDeviceValuesValue(ref PropertyKey key, out IntPtr value);
    void SetIPortableDevicePropVariantCollectionValue(ref PropertyKey key, IntPtr value);
    void GetIPortableDevicePropVariantCollectionValue(ref PropertyKey key, out IntPtr value);
    void SetIPortableDeviceKeyCollectionValue(ref PropertyKey key, IntPtr value);
    void GetIPortableDeviceKeyCollectionValue(ref PropertyKey key, out IntPtr value);
    void SetIPortableDeviceValuesCollectionValue(ref PropertyKey key, IntPtr value);
    void GetIPortableDeviceValuesCollectionValue(ref PropertyKey key, out IntPtr value);
    void RemoveValue(ref PropertyKey key);
    void CopyValuesFromPropertyStore(IntPtr store);
    void CopyValuesToPropertyStore(IntPtr store);
    void Clear();
}

[ComImport, Guid("dada2357-e0ad-492e-98db-dd61c53ba353"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPortableDeviceKeyCollection
{
    void GetCount(ref uint count);
    void GetAt(uint index, ref PropertyKey key);
    void Add(ref PropertyKey key);
    void Clear();
    void RemoveAt(uint index);
}

[ComImport, Guid("89b2e422-4f1b-4316-bcef-a44afea83eb3"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPortableDevicePropVariantCollection
{
    void GetCount(ref uint count);
    void GetAt(uint index, ref PropVariant value);
    void Add(ref PropVariant value);
    void GetType(out ushort type);
    void ChangeType(ushort type);
    void Clear();
    void RemoveAt(uint index);
}
