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
            {
                var objAddress = arguments[0];
                if (Helper.TryParseAddress(arguments[0], out var parsedObjAddress))
                {
                    foreach (var runtime in Runtimes)
                    {
                        _provider.Run(runtime, parsedObjAddress);
                    }
                }
                else
                {
                    Console.WriteLine("Could not parse {0} as address", objAddress);
                }

                break;
            }
            case 2:
                switch (arguments[0])
                {
                    case "-ad":
                        var objAddress = arguments[1];
                        if(Helper.TryParseAddress(arguments[1], out var parsedObjAddress))
                        {
                            foreach (var runtime in Runtimes)
                            {
                                _provider.Run(runtime, parsedObjAddress);
                            }
                        }
                        else
                        {
                            Console.WriteLine("Could not parse {0} as address", objAddress);
                        }

                        break;
                    case "-nm":
                        var name = arguments[1];
                        foreach (var runtime in Runtimes)
                        {
                            _provider.RunByName(runtime, name);
                        }

                        break;
                    case "-md":
                        var md = arguments[1];
                        if(Helper.TryParseToken(arguments[1], out var parsedMd))
                        {
                            foreach (var runtime in Runtimes)
                            {
                                _provider.RunForMetadataToken(runtime, parsedMd);
                            }
                        }
                        else
                        {
                            Console.WriteLine("Could not parse {0} as address", parsedMd);
                        }
                        break;
                }
                break;
            default:
                Console.WriteLine("Usage decompiletype stackpointer|decompiletype -md metadataToken");
                break;
        }
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