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
        if (address.StartsWith("0x"))
        {
            // remove "0x" for parsing
            address = address.Substring(2).TrimStart('0');
        }

        // remove the leading 0000 that WinDBG often add in 64 bit
        address = address.TrimStart('0');

        if (!ulong.TryParse(address, System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out var reference))
        {
            Console.WriteLine("numeric address value expected");
            return;
        }

        foreach (var runtime in Runtimes)
        {
            var method = _getMethodName.Get(runtime, reference);
            Console.WriteLine("TypeName: {0}", method.Type.Name);
            Console.WriteLine("MethodName: {0}", method.Name);
        }
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