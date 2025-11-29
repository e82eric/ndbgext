using DbgEngExtension;

namespace ndbgext;

public class TypesCommand : DbgEngCommand
{
    public TypesCommand(nint pUnknown, bool redirectConsoleOutput = true) : base(pUnknown, redirectConsoleOutput)
    {
    }

    internal void Run(string args)
    {
        var arguments = args.Split(' ');
        var printShort = arguments.Contains("-short");
        if (arguments.Length >= 2 && arguments.Contains("-implements"))
        {
            var searchString = arguments.Last();

            foreach (var runtime in Runtimes)
            {
                var types = ClrTypeExtensions.GetTypesThatImplement(runtime, searchString, true);
                foreach (var (type, interfaceName) in types)
                {
                    if (printShort)
                    {
                        Console.WriteLine($"{type.Name}");
                    }
                    else
                    {
                        Console.WriteLine($"{type.Name} : {interfaceName}");
                    }
                }
            }
        }
    }
}