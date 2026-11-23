using System.Collections.Immutable;
using FileCat.Core.Resources;

namespace FileCat.Core.Search;

/// <summary>Pure admission for the actual FileSystemInfo.Attributes projection; no stat, provider or content read.</summary>
public static class NativeSearchPredicates
{
    public sealed record Unavailable(FileAttributes Attribute, string Name, string Reason);
    public const FileAttributes Portable = FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.Directory;
    public const FileAttributes Windows = Portable | FileAttributes.Archive | FileAttributes.System |
        FileAttributes.SparseFile | FileAttributes.ReparsePoint | FileAttributes.Compressed | FileAttributes.Offline | FileAttributes.Encrypted;

    public static FileAttributes Supported(bool windows, bool nativeScope = true) => nativeScope ? windows ? Windows : Portable : FileAttributes.Directory;
    public static bool HasNativeMask(FileAttributes set, FileAttributes clear) => ((set | clear) & ~FileAttributes.Directory) != 0;
    public static bool HasNativeScope(IReadOnlyList<(ItemRef Item, string Relative)>? within) =>
        within is not { Count: > 0 } || within.Any(r => r.Item.FileSystemPath is not null);
    public static string Name(FileAttributes attribute) => attribute switch
    {
        FileAttributes.Archive => "Archive attribute (Windows)", FileAttributes.System => "System attribute (Windows)",
        FileAttributes.Compressed => "Compressed attribute (Windows)", FileAttributes.Encrypted => "EFS attribute (Windows)",
        FileAttributes.ReparsePoint => "Reparse point attribute (Windows)", FileAttributes.SparseFile => "Sparse attribute (Windows)",
        FileAttributes.Offline => "Offline attribute (Windows)", FileAttributes.Directory => "Folder",
        FileAttributes.Hidden => "Hidden (.NET attribute)", FileAttributes.ReadOnly => "Read-only (.NET attribute)",
        _ => Enum.IsDefined(attribute) ? attribute + " attribute" : $"Unknown attribute bit 0x{(uint)attribute:X8}",
    };
    public static string Help(FileAttributes attribute) => attribute switch
    {
        FileAttributes.Offline => "Tests only the Windows Offline attribute bit. It does not determine recall, residency, provider synchronization or all cloud-managed files.",
        FileAttributes.SparseFile => "Tests the Windows SparseFile attribute. It does not measure current holes, nonzero data or physical savings.",
        FileAttributes.ReparsePoint => "Tests the Windows ReparsePoint attribute, including unknown types. It does not follow targets or change recursion policy.",
        FileAttributes.Compressed => "Tests the Windows Compressed attribute. It does not inspect Btrfs policy, APFS compression or dataset/volume state. Each folder's listing supplies the bit, and NTFS can still list a file with more than one name in its earlier state there until that name is opened.",
        FileAttributes.Encrypted => "Tests the Windows Encrypted (EFS) attribute. It does not inspect fscrypt, APFS volumes, BitLocker or dataset encryption. Each folder's listing supplies the bit, and NTFS can still list a file with more than one name in its earlier state there until that name is opened.",
        FileAttributes.ReadOnly => "Tests the FileSystemInfo.Attributes ReadOnly projection. It is not an effective-access or immutable-flag decision.",
        _ => "Tests the FileSystemInfo.Attributes projection for this platform; archive-member attributes are a separate scope.",
    };

    public static ImmutableArray<Unavailable> Issues(FileAttributes active, bool windows, bool nativeScope = true)
    {
        uint unsupported = (uint)(active & ~Supported(windows, nativeScope));
        var items = ImmutableArray.CreateBuilder<Unavailable>();
        for (int bit = 0; bit < 32; bit++)
        {
            uint mask = 1u << bit;
            if ((unsupported & mask) == 0) continue;
            var attribute = (FileAttributes)mask;
            items.Add(new(attribute, Name(attribute), nativeScope
                ? "FileCat has no qualified predicate for this attribute on the current platform."
                : "The selected scope contains no local filesystem subjects; archive/provider members do not supply this native attribute."));
        }
        return items.ToImmutable();
    }

    public static bool Validate(SearchQuery query, out string? error) =>
        Validate(query.AttributesSet, query.AttributesClear, query.WithinResults, query.CarriesHiddenData is not null, out error);

    public static bool Validate(FileAttributes set, FileAttributes clear, IReadOnlyList<(ItemRef Item, string Relative)>? within, bool extraData, out string? error)
    {
        if ((set & clear) != 0)
        {
            error = "An attribute cannot be both required and excluded. Open Advanced criteria and choose one state.";
            return false;
        }
        var issues = Issues(set | clear, OperatingSystem.IsWindows(), HasNativeScope(within));
        if (!issues.IsEmpty)
        {
            error = string.Join(" ", issues.Select(i => $"{i.Name}: {i.Reason}")) + " Open Advanced criteria and use Remove this criterion, or clear it before running.";
            return false;
        }
        if (extraData && !HasNativeScope(within))
        {
            error = "The extra-data criterion requires a local filesystem subject. Clear that criterion before searching only archive/provider members.";
            return false;
        }
        error = null;
        return true;
    }
}
