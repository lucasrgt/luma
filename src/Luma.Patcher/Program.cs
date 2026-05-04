namespace Luma.Patcher;

internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 0)
            {
                PrintUsage();
                return 1;
            }

            return args[0].ToLowerInvariant() switch
            {
                "inspect" => RunInspect(args),
                "search" => RunSearch(args),
                "type" => RunType(args),
                "il" => RunIl(args),
                "patch" => RunPatch(args),
                _ => UnknownCommand(args[0])
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static int RunInspect(string[] args)
    {
        if (args.Length != 2)
        {
            Console.Error.WriteLine("Usage: luma-patcher inspect <game-assembly.dll>");
            return 1;
        }

        AssemblyInspector.Inspect(args[1], Console.Out);
        return 0;
    }

    private static int RunPatch(string[] args)
    {
        if (args.Length != 4)
        {
            Console.Error.WriteLine("Usage: luma-patcher patch <game-assembly.dll> <manifest.json> <output-assembly.dll>");
            return 1;
        }

        var patcher = new AssemblyPatcher();
        PatchReport report = patcher.Patch(args[1], args[2], args[3]);
        Console.WriteLine($"Patched {report.HooksApplied} hook(s); skipped {report.HooksSkipped} existing hook(s).");
        Console.WriteLine($"Output: {report.OutputPath}");
        return 0;
    }

    private static int RunSearch(string[] args)
    {
        if (args.Length != 3)
        {
            Console.Error.WriteLine("Usage: luma-patcher search <assembly.dll> <pattern>");
            return 1;
        }

        AssemblySearcher.Search(args[1], args[2], Console.Out);
        return 0;
    }

    private static int RunType(string[] args)
    {
        if (args.Length != 3)
        {
            Console.Error.WriteLine("Usage: luma-patcher type <assembly.dll> <full-type-name>");
            return 1;
        }

        AssemblyTypeDumper.Dump(args[1], args[2], Console.Out);
        return 0;
    }

    private static int RunIl(string[] args)
    {
        if (args.Length != 4)
        {
            Console.Error.WriteLine("Usage: luma-patcher il <assembly.dll> <full-type-name> <method-name>");
            return 1;
        }

        AssemblyIlDumper.Dump(args[1], args[2], args[3], Console.Out);
        return 0;
    }

    private static int UnknownCommand(string command)
    {
        Console.Error.WriteLine($"Unknown command: {command}");
        PrintUsage();
        return 1;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Luma.Patcher");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  inspect <game-assembly.dll>");
        Console.WriteLine("  search <assembly.dll> <pattern>");
        Console.WriteLine("  type <assembly.dll> <full-type-name>");
        Console.WriteLine("  il <assembly.dll> <full-type-name> <method-name>");
        Console.WriteLine("  patch <game-assembly.dll> <manifest.json> <output-assembly.dll>");
    }
}
