using DbgEngExtension;
using Microsoft.Diagnostics.Runtime;

namespace ndbgext;

public class FindRefCommand : DbgEngCommand
{
    private readonly FindRefProvider _provider;

    public FindRefCommand(FindRefProvider provider, nint pUnknown, bool redirectConsoleOutput = true)
        : base(pUnknown, redirectConsoleOutput)
    {
        _provider = provider;
    }
    
    internal void Run(string args)
    {
        var arguments = args.Split(' ');
        if (arguments.Length == 1)
        {
            if (Helper.TryParseAddress(arguments[0], out var reference))
            {
                foreach (var runtime in Runtimes)
                {
                    var items = _provider.Find(runtime, reference);
                    foreach (var item in items)
                    {
                        if (item.IsArray)
                        {
                            Console.WriteLine("Found in array");
                            Console.WriteLine("Array Address: {0:X}", item.Address);
                            Console.WriteLine("Array Type: {0}", item.TypeName);
                            Console.WriteLine("Array Index: {0}", item.ArrayIndex);
                            Console.WriteLine("Array Item Index: {0:X}", item.ArrayItemAddress);
                            Console.WriteLine();
                        }
                        else
                        {
                            Console.WriteLine("Found on object");
                            Console.WriteLine("Object Address: {0:X}", item.Address);
                            Console.WriteLine("Object Type: {0}", item.TypeName);
                            Console.WriteLine("Object Field Name: {0}", item.FieldName);
                            Console.WriteLine("Object Field Address: {0:X}", item.FieldAddress);
                            Console.WriteLine();
                        }
                    }
                }
            }
        }
        
        Console.WriteLine("usage: [address]");
    }
}

public class FindRefProvider
{
    public IEnumerable<FindRefItem> Find(ClrRuntime runtime, ulong address)
    {
        foreach (var obj in runtime.Heap.EnumerateObjects())
        {
            if (obj.IsArray)
            {
                var objArray = obj.AsArray();
                var isMultiDimensional = objArray.Type.StaticSize > (uint)(3 * IntPtr.Size);
                if (!isMultiDimensional)
                {
                    for (var i = 0; i < objArray.Length; i++)
                    {
                        if (objArray.Type.ComponentType != null && objArray.Type.ComponentType.IsObjectReference)
                        {
                            var iObj = objArray.GetObjectValue(i);
                            if (iObj.Address == address)
                            {
                                yield return new FindRefItem
                                {
                                    Address = objArray.Address,
                                    TypeName = objArray.Type.Name,
                                    IsArray = true,
                                    ArrayIndex = i,
                                    ArrayItemAddress = iObj.Address
                                };
                            }
                        }
                        else if (objArray.Type.ComponentType != null && objArray.Type.ComponentType.ElementType == ClrElementType.Int64)
                        {
                            var iAddr = objArray.GetValue<ulong>(i);
                            if (iAddr == address)
                            {
                                yield return new FindRefItem()
                                {
                                    TypeName = objArray.Type.Name,
                                    ArrayItemAddress = iAddr,
                                    Address = objArray.Address,
                                    IsArray = true,
                                    ArrayIndex = i
                                };
                            }
                        }
                    }
                }
            }
            else
            {
                if (obj.Type != null)
                {
                    foreach (var field in obj.Type.Fields)
                    {
                        if (field.Name != null && obj.TryReadObjectField(field.Name, out var fieldObj))
                        {
                            if (fieldObj.Address == address)
                            {
                                yield return new FindRefItem
                                {
                                    TypeName = obj.Type?.Name,
                                    FieldName = field.Name,
                                    Address = obj.Address,
                                    FieldAddress = fieldObj.Address,
                                };
                            }
                        }
                    }
                }
            }
        }
    }
}

public class FindRefItem
{
    public ulong FieldAddress { get; init; }
    public ulong Address { get; init; }
    public string? TypeName { get; init; }
    public string? FieldName { get; init; }
    public bool IsArray { get; init; }
    public int ArrayIndex { get; init; }
    public ulong ArrayItemAddress { get; init; }
}