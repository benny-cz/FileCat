// A worker stand-in for FileCat's restricted-helper tests. It reports only what the test asked for and never
// fails the process on a denied read, so a test can tell "refused" from "could not start at all".
if (args.Length == 0)
{
    Console.Error.WriteLine("usage: env | read <path>... | exit0");
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

    default:
        Console.Error.WriteLine("unknown request");
        return 2;
}
