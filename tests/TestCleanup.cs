namespace FileCat.Tests;

/// <summary>
/// Removes a test's own directory tree, retrying for a moment first. A hosted Windows runner's antivirus or search
/// indexer can hold a file open briefly after the test released it, and a teardown that throws then reports a
/// failure that says nothing about the product. The last attempt still throws, so a tree the test really did leave
/// behind — a staged copy, an undeleted temporary file — fails as loudly as before.
/// </summary>
internal static class TestCleanup
{
    /// <summary>Deletes <paramref name="path"/> and everything under it, for up to five seconds.</summary>
    public static void DeleteTree(string path)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException && attempt < 20)
            {
                Thread.Sleep(250);
            }
        }
    }
}
