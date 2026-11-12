using FileCat.Core.Edit;

namespace FileCat.App.Tests;

/// <summary>
/// What an edit store keeps on disk: its legacy folder and, where native storage is composed (listed runtimes, an ordinary
/// process, a folder held without links), its native root too, whose admission gate file is not an edit.
/// </summary>
internal static class EditStoreEntries
{
    internal static int Count(EditSessionStore store) =>
        new[] { store.Root, store.NativeRoot }.Distinct(StringComparer.Ordinal).Where(Directory.Exists)
            .Sum(root => Directory.EnumerateFileSystemEntries(root).Count(entry => Path.GetFileName(entry) != EditStorageAdmission.GateName));
}
