using System.Globalization;
using DbgEngExtension;
using Microsoft.Diagnostics.Runtime;

namespace ndbgext;

public class DecompileTypeCommand : DbgEngCommand
{
    private readonly DecompileTypeProvider _provider;

    public DecompileTypeCommand(DecompileTypeProvider provider, nint pUnknown, bool redirectConsoleOutput = true)
        : base(pUnknown, redirectConsoleOutput)
    {
        _provider = provider;
    }
    
    internal void Run(string args)
    {
        var arguments = args.Split(' ');
        switch (arguments.Length)
        {
            case 1:
                var stackPointer = arguments[0];
                if (stackPointer.StartsWith("0x"))
                {
                    // remove "0x" for parsing
                    stackPointer = stackPointer.Substring(2).TrimStart('0');
                }

                // remove the leading 0000 that WinDBG often add in 64 bit
                stackPointer = stackPointer.TrimStart('0');

                if (ulong.TryParse(stackPointer, NumberStyles.HexNumber,
                        CultureInfo.InvariantCulture, out var parsedStackPointer))
                {
                    foreach (var runtime in Runtimes)
                    {
                        _provider.Run(runtime, parsedStackPointer);
                    }
                }
                else
                {
                    Console.WriteLine("Could not parse {0} as address", stackPointer);
                }

                break;
            case 2:
                if (arguments[0] == "-nm")
                {
                    var name = arguments[1];
                    foreach (var runtime in Runtimes)
                    {
                        _provider.RunByName(runtime, name);
                    }

                    break;
                }
                var metadataToken = arguments[1];
                if (metadataToken.StartsWith("0x"))
                {
                    // remove "0x" for parsing
                    metadataToken = metadataToken.Substring(2).TrimStart('0');
                }

                // remove the leading 0000 that WinDBG often add in 64 bit
                metadataToken = metadataToken.TrimStart('0');

                if (int.TryParse(metadataToken, NumberStyles.HexNumber,
                        CultureInfo.InvariantCulture, out var parsedmetadataToken))
                {
                    foreach (var runtime in Runtimes)
                    {
                        _provider.RunForMetadataToken(runtime, parsedmetadataToken);
                    }
                }
                else
                {
                    Console.WriteLine("Could not parse {0} as address", metadataToken);
                }
                break;
            default:
                Console.WriteLine("Usage decompiletype stackpointer|decompiletype -md metadataToken");
                break;
        }
        
        //if (arguments.Length == 1)
        //{
        //    Console.WriteLine("missing instruction pointer address");
        //}
        //var stackPointer = arguments[0];
        //if (stackPointer.StartsWith("0x"))
        //{
        //    // remove "0x" for parsing
        //    stackPointer = stackPointer.Substring(2).TrimStart('0');
        //}

        //// remove the leading 0000 that WinDBG often add in 64 bit
        //stackPointer = stackPointer.TrimStart('0');

        //if (ulong.TryParse(stackPointer, NumberStyles.HexNumber,
        //        CultureInfo.InvariantCulture, out var parsedStackPointer))
        //{
        //    foreach (var runtime in Runtimes)
        //    {
        //        _provider.Run(runtime, parsedStackPointer);
        //    }
        //}
        //else
        //{
        //    Console.WriteLine("Could not parse {0} as address", stackPointer);
        //}
    }
}

public class DecompileTypeProvider
{
    private readonly Decompiler _decompiler;

    public DecompileTypeProvider(Decompiler decompiler)
    {
        _decompiler = decompiler;
    }
    
    public void Run(ClrRuntime runtime, ulong address)
    {
        runtime.DacLibrary.SOSDacInterface.GetAppDomainData(address, out var d);
        var type = runtime.Heap.GetObjectType(address);
        if (type != null)
        {
            var code = _decompiler.DecompileType(runtime, type.Module.Name, type);
            Console.WriteLine(code);
        }
        else
        {
            Console.WriteLine("Could not find type at address {0:X}", address);
        }
    }
    
    public void RunForMetadataToken(ClrRuntime runtime, int metadataToken)
    {
        ClrType? type = null;
        foreach (var clrModule in runtime.EnumerateModules())
        {
            type = clrModule.ResolveToken(metadataToken);
        }
        if (type != null)
        {
            var code = _decompiler.DecompileType(runtime, type.Module.Name, type);
            Console.WriteLine(code);
        }
        else
        {
            Console.WriteLine("Could not find type at metadataToken {0:X}", metadataToken);
        }
    }
    
    public void RunByName(ClrRuntime runtime, string typeName)
    {
        ClrType? type = null;
        foreach (var clrModule in runtime.EnumerateModules())
        {
            type = clrModule.GetTypeByName(typeName);
            if (type != null)
            {
                break;
            }
        }
        if (type != null)
        {
            var code = _decompiler.DecompileType(runtime, type.Module.Name, type);
            Console.WriteLine(code);
        }
        else
        {
            Console.WriteLine("Could not find type at typeName {0}", typeName);
        }
    }
}