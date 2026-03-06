using DbgEngExtension;

namespace ndbgext;

public sealed class BuildMemoryGraphCommand : DbgEngCommand
{
    public BuildMemoryGraphCommand(nint pUnknown, bool redirectConsoleOutput = true)
        : base(pUnknown, redirectConsoleOutput)
    {
    }

    internal void Run(string args)
    {
        if (!string.IsNullOrWhiteSpace(args))
        {
            Console.Error.WriteLine("Usage: buildmemorygraph");
            return;
        }

        MemoryGraphRunner.Build(Runtimes);
    }
}

public sealed class TypeBytesCommand : DbgEngCommand
{
    public TypeBytesCommand(nint pUnknown, bool redirectConsoleOutput = true)
        : base(pUnknown, redirectConsoleOutput)
    {
    }

    internal void Run(string args)
    {
        if (MemoryGraphCache.Graph == null)
        {
            Console.Error.WriteLine("No memory graph cached. Run buildmemorygraph first.");
            return;
        }

        if (string.IsNullOrWhiteSpace(args))
        {
            Console.Error.WriteLine("Usage: typebytes <TypeName> [--max-type-roots N] [--max-tree-nodes N] [--max-depth N] [--quiet]");
            return;
        }

        MemoryGraphOptions options = ParseOptions(args);
        if (string.IsNullOrWhiteSpace(options.TypeFilter))
        {
            Console.Error.WriteLine("Type filter is required.");
            return;
        }

        MemoryGraphRunner.PrintTypeBytes(MemoryGraphCache.Graph, options);
    }

    private static MemoryGraphOptions ParseOptions(string args)
    {
        MemoryGraphOptions options = new MemoryGraphOptions();
        string[] parts = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        for (int i = 0; i < parts.Length; i++)
        {
            string arg = parts[i];
            if (string.IsNullOrWhiteSpace(arg))
            {
                continue;
            }

            if (arg.Equals("--quiet", StringComparison.OrdinalIgnoreCase))
            {
                options.Verbose = false;
                continue;
            }

            if (TryReadIntArg(arg, parts, ref i, "--max-type-roots", out int maxRoots))
            {
                options.MaxTypeRoots = maxRoots;
                continue;
            }

            if (TryReadIntArg(arg, parts, ref i, "--max-tree-nodes", out int maxTree))
            {
                options.MaxTreeNodes = maxTree;
                continue;
            }

            if (TryReadIntArg(arg, parts, ref i, "--max-depth", out int maxDepth))
            {
                options.MaxDepth = maxDepth;
                continue;
            }

            if (arg.StartsWith("-", StringComparison.OrdinalIgnoreCase) || arg.StartsWith("/", StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine("Unknown option: {0}", arg);
                continue;
            }

            if (options.TypeFilter == null)
            {
                options.TypeFilter = arg;
            }
            else
            {
                Console.Error.WriteLine("Unexpected argument: {0}", arg);
            }
        }

        return options;
    }

    private static bool TryReadIntArg(string arg, string[] parts, ref int i, string name, out int value)
    {
        value = 0;
        if (arg.StartsWith(name + "=", StringComparison.OrdinalIgnoreCase))
        {
            string val = arg.Substring(name.Length + 1);
            return TryParsePositiveInt(val, name, out value);
        }

        if (arg.Equals(name, StringComparison.OrdinalIgnoreCase))
        {
            if (i + 1 >= parts.Length)
            {
                Console.Error.WriteLine("Missing value for {0}", name);
                return true;
            }

            string val = parts[++i];
            return TryParsePositiveInt(val, name, out value);
        }

        return false;
    }

    private static bool TryParsePositiveInt(string value, string name, out int parsed)
    {
        parsed = 0;
        if (!int.TryParse(value, out int temp) || temp <= 0)
        {
            Console.Error.WriteLine("Invalid {0} value: {1}", name, value);
            return true;
        }

        parsed = temp;
        return true;
    }
}

public sealed class TotalTypeBytesCommand : DbgEngCommand
{
    public TotalTypeBytesCommand(nint pUnknown, bool redirectConsoleOutput = true)
        : base(pUnknown, redirectConsoleOutput)
    {
    }

    internal void Run(string args)
    {
        if (MemoryGraphCache.Graph == null)
        {
            Console.Error.WriteLine("No memory graph cached. Run buildmemorygraph first.");
            return;
        }

        if (!string.IsNullOrWhiteSpace(args))
        {
            Console.Error.WriteLine("Usage: totaltypebytes");
            return;
        }

        MemoryGraphRunner.PrintTotalTypeBytes(MemoryGraphCache.Graph);
    }
}
