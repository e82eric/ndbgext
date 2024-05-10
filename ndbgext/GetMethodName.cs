using DbgEngExtension;
using Microsoft.Diagnostics.Runtime;

namespace ndbgext;

public class GetMethodNameCommand : DbgEngCommand
{
    private GetMethodName _getMethodName = new GetMethodName();

    public GetMethodNameCommand(nint pUnknown, bool redirectConsoleOutput = true)
        : base(pUnknown, redirectConsoleOutput)
    {
    }

    public GetMethodNameCommand(IDisposable dbgeng, bool redirectConsoleOutput = false)
        : base(dbgeng, redirectConsoleOutput)
    {
    }

    internal void Run(string args)
    {
        if (string.IsNullOrEmpty(args))
        {
            Console.WriteLine("Missing address of a ConcurrentDictionary");
            return;
        }

        var arguments = args.Split(' ');

        var address = arguments[0];
        if (Helper.TryParseAddress(arguments[0], out var reference))
        {
            foreach (var runtime in Runtimes)
            {
                var method = _getMethodName.Get(runtime, reference);
                Console.WriteLine("TypeName: {0}", method.Type.Name);
                Console.WriteLine("MethodName: {0}", method.Name);
            }
        }
        
        Console.WriteLine("usage: [address]");
    }
}

public class GetMethodName
{
    public ClrMethod Get(ClrRuntime runtime, ulong address)
    {
        var method = runtime.GetMethodByInstructionPointer(address);
        return method;
    }
}