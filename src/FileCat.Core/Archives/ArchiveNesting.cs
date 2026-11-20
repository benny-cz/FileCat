using FileCat.Core.Resources;

namespace FileCat.Core.Archives;

internal static class ArchiveNesting
{
    internal const int MaximumDepth = 8;

    internal static void RequireWithinLimit(Location location, int additionalDepth = 0)
    {
        int depth = additionalDepth;
        for (var container = location.Container; container is { IsFileSystem: false }; container = container.Container)
        {
            if (++depth > MaximumDepth)
                throw new InvalidDataException($"Archives are nested more than {MaximumDepth} levels deep.");
        }
    }
}
