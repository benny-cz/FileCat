// A worker stand-in for FileCat's restricted-helper tests. It reports only what the test asked for and never
// fails the process on a denied read, so a test can tell "refused" from "could not start at all".
if (args.Length == 0)
{
    Console.Error.WriteLine("usage: env | read <path>... | exit0 | dropprivs [path]");
    return 2;
}

switch (args[0])
{
    case "exit0":
        return 0;

    case "env":
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
            Console.WriteLine($"{entry.Key}={entry.Value}");
        return 0;

    case "read":
        foreach (var path in args.Skip(1))
        {
            try { Console.WriteLine(File.ReadAllText(path)); }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                Console.WriteLine($"refused {Path.GetFileName(path)}: {ex.GetType().Name}");
            }
        }
        return 0;

    case "dropprivs":
    {
        // Reports the worker identity change the picture decoder performs, and then whether a root-only file
        // is still readable, so a test can see the drop took effect rather than take the report on trust.
        var result = FileCat.Core.FileSystem.UnixWorkerIdentity.Drop();
        Console.WriteLine($"before={result.UserBefore}:{result.GroupBefore} after={result.UserAfter}:{result.GroupAfter} " +
                          $"regainRefused={result.RegainRefused} startedPrivileged={result.StartedPrivileged} " +
                          $"unprivileged={result.Unprivileged} refusal={result.Refusal ?? "<none>"}");
        if (args.Length > 1)
        {
            try { Console.WriteLine("read=" + File.ReadAllText(args[1]).Trim()); }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                Console.WriteLine("read=refused " + ex.GetType().Name);
            }
        }
        return 0;
    }

    default:
        Console.Error.WriteLine("unknown request");
        return 2;
}
