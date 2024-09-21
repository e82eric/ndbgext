using DbgEngExtension;
using Microsoft.Diagnostics.Runtime;

namespace ndbgext;

public class TSelectCommand : DbgEngCommand
{
    public TSelectCommand(nint pUnknown, bool redirectConsoleOutput = true)
        : base(pUnknown, redirectConsoleOutput)
    {
    }
    
    internal void Run(string args)
    {
        var argsSplit = args.Split(' ');
        if (argsSplit.Length == 2)
        {
            if (Helper.TryParseAddress(argsSplit[0], out var reference))
            {
                new TSelect().Execute(Runtimes, reference, argsSplit[1]);
            }
            else
            {
                Console.WriteLine($"Cannot parse {argsSplit[0]} as address");
            }
        }
    }
}

public class TSelect
{
    public void Execute(IList<ClrRuntime> runtimes, ulong reference, string expression)
    {
        var pathParts = expression.Split('.');
        
        foreach (var runtime in runtimes)
        {
            var obj = runtime.Heap.GetObject(reference);
            for(var i = 0; i < pathParts.Length; i++)
            {
                var fieldName = pathParts[i];
                if (i == pathParts.Length - 1)
                {
                    var fd = obj.Type?.Fields.FirstOrDefault(f => f.Name == fieldName);
                    if (fd != null)
                    {
                        PrintField(fd, obj, true);
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
        }
    }

    private void PrintField(ClrInstanceField fd, ClrObject obj, bool recurse)
    {
        var fieldName = fd.Name;
        Console.Write(fd.Type?.Name);
        Console.Write(fd.Type?.Name);
        Console.Write(" ");
        switch (fd.ElementType)
        {
            case ClrElementType.Int8:
                //Console.WriteLine($"{obj.ReadField<Int8>(fieldName)}");
                break;
            case ClrElementType.Int16:
                Console.WriteLine($"{obj.ReadField<Int16>(fieldName)}");
                break;
            case ClrElementType.Int32:
                Console.WriteLine($"{obj.ReadField<Int32>(fieldName)}");
                break;
            case ClrElementType.Int64:
                Console.WriteLine($"{obj.ReadField<Int64>(fieldName)}");
                break;
            case ClrElementType.UInt16:
                Console.WriteLine($"{obj.ReadField<UInt16>(fieldName)}");
                break;
            case ClrElementType.UInt32:
                Console.WriteLine($"{obj.ReadField<UInt32>(fieldName)}");
                break;
            case ClrElementType.UInt64:
                Console.WriteLine($"{obj.ReadField<UInt64>(fieldName)}");
                break;
            case ClrElementType.Boolean:
                Console.WriteLine($"{obj.ReadField<Boolean>(fieldName)}");
                break;
            case ClrElementType.Double:
                Console.WriteLine($"{obj.ReadField<Double>(fieldName)}");
                break;
            case ClrElementType.Float:
                Console.WriteLine($"{obj.ReadField<float>(fieldName)}");
                break;
            case ClrElementType.Object:
                Console.WriteLine($"{obj.ReadObjectField(fieldName).Address:x16}");
                break;
            case ClrElementType.Class:
                var cObj = obj.ReadObjectField(fieldName);
                Console.WriteLine($"{cObj.Address:x16}");
                if (recurse)
                {
                    foreach (var cField in cObj.Type.Fields)
                    {
                        Console.Write(cField.Name);
                        Console.Write(" ");
                        Console.Write(cField);
                        PrintField(cField, cObj, false);
                    }
                }
                break;
            case ClrElementType.String:
                Console.WriteLine($"{obj.ReadStringField(fieldName)}");
                break;
            case ClrElementType.Array:
                //Not sure what to do here
                //Console.WriteLine($"{obj.ReadStringField(fieldName)}");
                break;
        }
    }
}
