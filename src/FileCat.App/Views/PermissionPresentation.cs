using FileCat.Core.Permissions;
using FileCat.Platform.Windows.Permissions;

namespace FileCat.App.Views;

/// <summary>
/// Plain-language names for the facts the permissions inspector shows. Every name is derived from the stored value, which
/// stays available beside it (an entry's details, a copy button): a name explains, the value is the evidence.
/// </summary>
internal static class PermissionPresentation
{
    // Windows file and folder rights (winnt.h). Explorer's basic names are exact combinations of these.
    private const uint ReadData = 0x1, WriteData = 0x2, AppendData = 0x4, ReadEa = 0x8, WriteEa = 0x10, Execute = 0x20,
        DeleteChild = 0x40, ReadAttributes = 0x80, WriteAttributes = 0x100, Delete = 0x10000, ReadControl = 0x20000,
        WriteDac = 0x40000, WriteOwner = 0x80000, Synchronize = 0x100000, AccessSystemSecurity = 0x1000000;
    private const uint WriteSet = WriteData | AppendData | WriteEa | WriteAttributes | Synchronize;

    /// <summary>Explorer's basic name for an access mask, or "Special" when the mask is no basic combination.</summary>
    internal static string WindowsAccess(uint mask, byte flags, bool directory)
    {
        // Generic bits mean what Windows maps them to for files; SYNCHRONIZE never changes the name Explorer shows.
        uint rights = WindowsFileAccessRights.MapGeneric(mask) | Synchronize;
        uint read = WindowsFileAccessRights.PresetMask(WindowsFileAccessPreset.Read);
        uint readExecute = WindowsFileAccessRights.PresetMask(WindowsFileAccessPreset.ReadAndExecute);
        if (rights == WindowsFileAccessRights.PresetMask(WindowsFileAccessPreset.FullControl)) return "Full control";
        if (rights == WindowsFileAccessRights.PresetMask(WindowsFileAccessPreset.Modify)) return "Modify";
        // A folder rule granting read and execute to subfolders only is Explorer's "List folder contents".
        if (rights == readExecute) return directory && (flags & 3) == 2 ? "List folder contents" : "Read and execute";
        if (rights == read) return "Read";
        if ((rights & ~ReadControl) == WriteSet) return "Write";
        if (rights == (read | WriteSet)) return "Read and write";
        if (rights == (readExecute | WriteSet)) return "Read, write and execute";
        return "Special";
    }

    /// <summary>Every right a mask grants, by its file or folder name; unknown bits are listed by value.</summary>
    internal static IReadOnlyList<string> WindowsRights(uint mask, bool directory)
    {
        uint rights = WindowsFileAccessRights.MapGeneric(mask);
        var names = new List<string>();
        void Add(uint bit, string file, string folder) { if ((rights & bit) != 0) names.Add(directory ? folder : file); }
        Add(ReadData, "Read data", "List folder");
        Add(WriteData, "Write data", "Create files");
        Add(AppendData, "Append data", "Create folders");
        Add(Execute, "Execute file", "Traverse folder");
        Add(ReadAttributes, "Read attributes", "Read attributes");
        Add(ReadEa, "Read extended attributes", "Read extended attributes");
        Add(WriteAttributes, "Write attributes", "Write attributes");
        Add(WriteEa, "Write extended attributes", "Write extended attributes");
        Add(DeleteChild, "Delete children", "Delete subfolders and files");
        Add(Delete, "Delete", "Delete");
        Add(ReadControl, "Read permissions", "Read permissions");
        Add(WriteDac, "Change permissions", "Change permissions");
        Add(WriteOwner, "Take ownership", "Take ownership");
        Add(Synchronize, "Synchronize", "Synchronize");
        Add(AccessSystemSecurity, "Access audit rules", "Access audit rules");
        const uint named = ReadData | WriteData | AppendData | Execute | ReadAttributes | ReadEa | WriteAttributes | WriteEa |
            DeleteChild | Delete | ReadControl | WriteDac | WriteOwner | Synchronize | AccessSystemSecurity;
        if ((rights & ~named) is uint other and not 0) names.Add($"Other rights 0x{other:X8}");
        return names;
    }

    /// <summary>Which objects a rule reaches. A file's rule reaches only the file; a folder's follows its inheritance flags.</summary>
    internal static string WindowsAppliesTo(byte flags, bool directory)
    {
        if (!directory) return "This file";
        bool files = (flags & 1) != 0, folders = (flags & 2) != 0, oneLevel = (flags & 4) != 0, inheritOnly = (flags & 8) != 0;
        string scope = (files, folders, inheritOnly) switch
        {
            (false, false, false) => "This folder only",
            (false, false, true) => "Nothing (inherit only, no targets)",
            (true, true, false) => "This folder, subfolders and files",
            (false, true, false) => "This folder and subfolders",
            (true, false, false) => "This folder and files",
            (true, true, true) => "Subfolders and files only",
            (false, true, true) => "Subfolders only",
            (true, false, true) => "Files only",
        };
        return oneLevel && (files || folders) ? scope + ", one level" : scope;
    }

    internal static string WindowsSource(byte flags) => (flags & 0x10) != 0 ? "Inherited" : "Explicit";

    internal static string WindowsType(WindowsAce entry) => entry.Kind switch
    {
        WindowsAceKind.Allow => "Allow",
        WindowsAceKind.Deny => "Deny",
        _ => $"Other (type 0x{entry.Type:X2})"
    };

    internal static string AuditType(WindowsAuditAce entry) => entry.Type switch
    {
        2 when entry.AuditsSuccess && entry.AuditsFailure => "Audit success and failure",
        2 when entry.AuditsSuccess => "Audit success",
        2 when entry.AuditsFailure => "Audit failure",
        2 => "Audit (no outcome)",
        0x11 => "Integrity label",
        _ => $"Other (type 0x{entry.Type:X2})"
    };

    /// <summary>The integrity level a mandatory label names (S-1-16-x), with the access it blocks from lower levels.</summary>
    internal static string IntegrityLabel(string sid, uint mask)
    {
        string level = sid switch
        {
            "S-1-16-0" => "Untrusted",
            "S-1-16-4096" => "Low",
            "S-1-16-8192" => "Medium",
            "S-1-16-8448" => "Medium plus",
            "S-1-16-12288" => "High",
            "S-1-16-16384" => "System",
            "S-1-16-20480" => "Protected process",
            _ => sid
        };
        var policy = new List<string>();
        if ((mask & 1) != 0) policy.Add("no write up");
        if ((mask & 2) != 0) policy.Add("no read up");
        if ((mask & 4) != 0) policy.Add("no execute up");
        return policy.Count == 0 ? level : $"{level} ({string.Join(", ", policy)})";
    }

    /// <summary>
    /// English names of Windows' well-known accounts, shown at once and without any lookup. A local lookup may then replace
    /// one with the name this Windows uses (for example a localized one).
    /// </summary>
    internal static string? WellKnownWindowsName(string sid) => sid switch
    {
        "S-1-1-0" => "Everyone",
        "S-1-2-0" => "LOCAL",
        "S-1-3-0" => "CREATOR OWNER",
        "S-1-3-1" => "CREATOR GROUP",
        "S-1-3-4" => "OWNER RIGHTS",
        "S-1-5-1" => "DIALUP",
        "S-1-5-2" => "NETWORK",
        "S-1-5-3" => "BATCH",
        "S-1-5-4" => "INTERACTIVE",
        "S-1-5-6" => "SERVICE",
        "S-1-5-7" => "ANONYMOUS LOGON",
        "S-1-5-9" => "ENTERPRISE DOMAIN CONTROLLERS",
        "S-1-5-10" => "SELF",
        "S-1-5-11" => "Authenticated Users",
        "S-1-5-12" => "RESTRICTED",
        "S-1-5-13" => "TERMINAL SERVER USER",
        "S-1-5-14" => "REMOTE INTERACTIVE LOGON",
        "S-1-5-15" => "This Organization",
        "S-1-5-17" => "IUSR",
        "S-1-5-18" => "SYSTEM",
        "S-1-5-19" => "LOCAL SERVICE",
        "S-1-5-20" => "NETWORK SERVICE",
        "S-1-5-32-544" => "Administrators",
        "S-1-5-32-545" => "Users",
        "S-1-5-32-546" => "Guests",
        "S-1-5-32-547" => "Power Users",
        "S-1-5-32-551" => "Backup Operators",
        "S-1-5-32-555" => "Remote Desktop Users",
        "S-1-5-32-568" => "IIS_IUSRS",
        "S-1-5-80-0" => "ALL SERVICES",
        "S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464" => "TrustedInstaller",
        "S-1-15-2-1" => "ALL APPLICATION PACKAGES",
        "S-1-15-2-2" => "ALL RESTRICTED APPLICATION PACKAGES",
        "S-1-15-3-1024-1065365936-1281604716-3511738428-1654721687-432734479-3232135806-4053264122-3456934681" => "Internet Explorer AppContainer",
        _ when sid.StartsWith("S-1-16-", StringComparison.Ordinal) => IntegrityLabel(sid, 0) + " Mandatory Level",
        _ => null
    };

    /// <summary>The Unix permission bits for one class as "Read, write, execute" style text, or "None".</summary>
    internal static string UnixClassRights(ushort mode, int shift)
    {
        var names = new List<string>();
        if ((mode & (4 << shift)) != 0) names.Add("read");
        if ((mode & (2 << shift)) != 0) names.Add("write");
        if ((mode & (1 << shift)) != 0) names.Add("execute");
        return names.Count == 0 ? "None" : char.ToUpperInvariant(names[0][0]) + string.Join(", ", names)[1..];
    }

    /// <summary>The special Unix bits a mode sets, by name.</summary>
    internal static IReadOnlyList<string> UnixSpecialBits(ushort mode)
    {
        var names = new List<string>();
        if ((mode & 0x800) != 0) names.Add("Set user ID");
        if ((mode & 0x400) != 0) names.Add("Set group ID");
        if ((mode & 0x200) != 0) names.Add("Sticky");
        return names;
    }
}
