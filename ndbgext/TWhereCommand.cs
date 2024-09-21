using DbgEngExtension;
using Microsoft.Diagnostics.Runtime;

namespace ndbgext;

public class TWhereCommand : DbgEngCommand
{
    public TWhereCommand(nint pUnknown, bool redirectConsoleOutput = true)
        : base(pUnknown, redirectConsoleOutput)
    {
    }
    
    internal void Run(string args)
    {
        var argsSplit = args.Split(' ');
        if (argsSplit.Length == 3)
        {
            if (Helper.TryParseAddress(argsSplit[0], out var reference))
            {
                new TWhere().Execute(Runtimes, reference, argsSplit[1], argsSplit[2]);
            }
            else
            {
                Console.WriteLine($"Cannot parse {argsSplit[0]} as address");
            }
        }
    }
}

public class TWhere
{
    public void Execute(IList<ClrRuntime> runtimes, ulong reference, string path, string expected)
    {
        var pathParts = path.Split('.');
        if(Helper.TryParseAddress(expected, out var eq))
        {
            foreach (var runtime in runtimes)
            {
                var obj = runtime.Heap.GetObject(reference);

                if (obj.IsArray)
                {
                    var arr = obj.AsArray();
                    for (var i = 0; i < arr.Length; i++)
                    {
                        var o = arr.GetObjectValue(i);
                        if (o.IsValid)
                        {
                            Console.WriteLine(o.Address);
                            var addr = GetAddressForPath(o, pathParts);
                            if (addr == eq)
                            {
                                Console.Write("Found it!");
                            }
                            else
                            {
                                Console.WriteLine($"{addr} != {eq}");
                            }
                        }
                    }
                }
            }
        }
    }

    public ulong? GetAddressForPath(ClrObject obj, string[] pathParts)
    {
        for(var i = 0; i < pathParts.Length; i++)
        {
            var fieldName = pathParts[i];
            if (i == pathParts.Length - 1)
            {
                var fd = obj.Type?.Fields.FirstOrDefault(f => f.Name == fieldName);
                if (fd != null)
                {
                    if (obj.TryReadObjectField(fd.Name, out var result))
                    {
                        return result.Address;
                    }
                    else
                    {
                        Console.WriteLine($"Cannot find {fieldName} on {obj.Address:x16}");
                        Console.WriteLine("Available fields:");
                        foreach (var field in obj.Type.Fields)
                        {
                            Console.WriteLine($"{field.Name} {field.ElementType}");
                        }
                    }
                }
                else
                {
                    Console.WriteLine($"Cannot find {fieldName} on {obj.Address:x16}");
                    Console.WriteLine("Available fields:");
                    foreach (var field in obj.Type.Fields)
                    {
                        Console.WriteLine($"{field.Name} {field.ElementType}");
                    }
                }
            }
            else if (obj.TryReadObjectField(pathParts[i], out var f))
            {
                obj = f;
                if (i == pathParts.Length - 1)
                {
                    Console.WriteLine($"Success! Addr {f.Address:x16}");
                }
            }
            else
            {
                Console.WriteLine($"Cannot find {fieldName} on {obj.Address:x16}");
                Console.WriteLine("Available fields:");
                foreach (var field in obj.Type.Fields)
                {
                    Console.WriteLine($"{field.Name} {field.ElementType}");
                }
            }
        }

        return null;
    }
}
