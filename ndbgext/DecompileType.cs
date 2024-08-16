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
                    case "-ip":
                        var ipStr = arguments[1];
                        if (Helper.TryParseAddress(ipStr, out var ip))
                        {
                            foreach (var runtime in Runtimes)
                            {
                                _provider.RunForInstructionPointer(runtime, ip);
                            }
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
                    case "-mt":
                        var mt = arguments[1];
                        if(Helper.TryParseAddress(arguments[1], out var parsedMt))
                        {
                            foreach (var runtime in Runtimes)
                            {
                                _provider.RunForMethodTable(runtime, parsedMt);
                            }
                        }
                        else
                        {
                            Console.WriteLine("Could not parse {0} as address", parsedMt);
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
        var type = runtime.Heap.GetObjectType(address);
        if (type != null)
        {
            var code = _decompiler.DecompileType(runtime, type.Name, type);
            Console.WriteLine(code);
        }
        else
        {
            Console.WriteLine("Could not find type at address {0:X}", address);
        }
    }
    
    public void RunForMethodTable(ClrRuntime runtime, ulong methodTable)
    {
        ClrType? type = null;
        foreach (var clrObject in runtime.Heap.EnumerateObjects())
        {
            if (clrObject.Type?.MethodTable == methodTable)
            {
                type = clrObject.Type;
                break;
            }
        }
        if (type != null)
        {
            var code = _decompiler.DecompileType(runtime, type.Name, type);
            Console.WriteLine(code);
        }
        else
        {
            Console.WriteLine("Could not find type at metadataToken {0:X}", methodTable);
        }
    }
    
    public void RunForMetadataToken(ClrRuntime runtime, int metadataToken)
    {
        ClrType? type = null;
        foreach (var clrObject in runtime.Heap.EnumerateObjects())
        {
            if (clrObject.Type?.MetadataToken == metadataToken)
            {
                type = clrObject.Type;
                break;
            };
        }
        if (type != null)
        {
            var code = _decompiler.DecompileType(runtime, type.Name, type);
            Console.WriteLine(code);
        }
        else
        {
            Console.WriteLine("Could not find type at metadataToken {0:X}", metadataToken);
        }
    }
    
    public void RunForInstructionPointer(ClrRuntime runtime, ulong instructionPointer)
    {
        var method = runtime.GetMethodByInstructionPointer(instructionPointer);
        if (method != null && method.Type.Name != null)
        {
            var code = _decompiler.DecompileType(runtime, method.Type.Name, method.Type);
            Console.WriteLine(code);
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
            var code = _decompiler.DecompileType(runtime, type.Name, type);
            Console.WriteLine(code);
        }
        else
        {
            Console.WriteLine("Could not find type at typeName {0}", typeName);
        }
    }
}