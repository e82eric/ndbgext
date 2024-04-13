using DbgEngExtension;
using Microsoft.Diagnostics.Runtime;

namespace ndbgext;

public class ConcurrentDictionary : DbgEngCommand
{
    class Entry
    {
        public Entry()
        {
            Key = new EntryNode();
            Value = new EntryNode();
        }
        public ulong Address { get; set; }
        public EntryNode Key { get; }
        public EntryNode Value { get; }
    }

    class EntryNode
    {
        public ulong Address { get; set; }
        public string? Value { get; set; }
        public string? TypeFullName { get; set; }
    }
    static readonly Dictionary<ClrElementType, Func<ClrObject, string, string>> _objectTypeFunctions = new Dictionary<ClrElementType, Func<ClrObject, string, string>>
    {
        {ClrElementType.Int16, (type, fieldName) => type.ReadField<Int16>(fieldName).ToString() },
        {ClrElementType.Int32, (type, fieldName) => type.ReadField<Int32>(fieldName).ToString() },
        {ClrElementType.Int64, (type, fieldName) => type.ReadField<Int64>(fieldName).ToString() },
        {ClrElementType.UInt16, (type, fieldName) => type.ReadField<UInt16>(fieldName).ToString() },
        {ClrElementType.UInt32, (type, fieldName) => type.ReadField<UInt32>(fieldName).ToString() },
        {ClrElementType.UInt64, (type, fieldName) => type.ReadField<UInt64>(fieldName).ToString() },
        {ClrElementType.Boolean, (type, fieldName) => type.ReadField<Boolean>(fieldName).ToString() },
        {ClrElementType.Double, (type, fieldName) => type.ReadField<Double>(fieldName).ToString() },
    };

    public ConcurrentDictionary(nint pUnknown, bool redirectConsoleOutput = true)
        : base(pUnknown, redirectConsoleOutput)
    {
    }

    public ConcurrentDictionary(IDisposable dbgeng, bool redirectConsoleOutput = false)
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
        Show(reference);
    }

    private void Show(ulong objAddr)
    {
        foreach (ClrRuntime runtime in Runtimes)
        {
            bool isNetCore = false;
            foreach (ClrModule module in runtime.EnumerateModules())
            {
                if (string.IsNullOrEmpty(module.AssemblyName))
                    continue;

                var name = module.AssemblyName.ToLower();
                if (name.Contains("corelib"))
                {
                    isNetCore = true;
                    break;
                }
            }

            ClrHeap heap = runtime.Heap;

            ClrObject obj = heap.GetObject(objAddr);

            if (!obj.IsValid)
            {
                Console.WriteLine("Not a valid object");
                return;
            }

            if (obj.Type.Name.StartsWith("System.Collections.Concurrent.ConcurrentDictionary<"))
            {
                var entries = GetEntries(obj, isNetCore);
                foreach (var entry in entries)
                {
                    Console.WriteLine("Node: {0:X}", entry.Address);
                    PrintNode(entry.Key, "Key");
                    PrintNode(entry.Value, "Value");
                    Console.WriteLine("------");
                }
            }
            else
            {
                Console.WriteLine("Not a concurrent dictionary");
            }
        }

        void PrintNode(EntryNode node, string prefix)
        {
            Console.WriteLine("  {0}", prefix);
            Console.WriteLine("    Type: {0}", node.TypeFullName);
            Console.WriteLine("    Address: {0:X}", node.Address);
            if (node.Value != null)
            {
                Console.WriteLine("    Value: {0}", node.Value);
            }
        }

        IReadOnlyList<Entry> GetEntries(ClrObject dictionaryObject, bool isNetCore)
        {
            var result = new List<Entry>();
            var tables = dictionaryObject.ReadObjectField(isNetCore ? "_tables" : "m_tables");
            var buckets = tables.ReadObjectField(isNetCore ? "_buckets" : "m_buckets");
            if (buckets is { IsArray: true, IsValid: true })
            {
                var bucketsArray = buckets.AsArray();
                for (int i = 0; i < bucketsArray.Length; i++)
                {
                    if (isNetCore)
                    {
                        FillBucketItemForNetCore(bucketsArray, i, result);
                    }
                    else
                    {
                        FillBucketItemForFramework(bucketsArray, i, result);
                    }
                }
            }

            return result;
        }

        void FillBucketItemForFramework(ClrArray bucketsArray, int i, IList<Entry> dest)
        {
            var val = bucketsArray.GetObjectValue(i);
            if (val.IsValid)
            {
                var entry = new Entry();
                entry.Address = val.Address;
                FillEntryNodeForObjectType(val, entry.Key, "m_key");
                FillEntryNodeForObjectType(val, entry.Value, "m_value");
                dest.Add(entry);
            }
        }

        void FillBucketItemForNetCore(ClrArray bucketsArray, int i, IList<Entry> dest)
        {
            var val = bucketsArray.GetStructValue(i);
            if (val.IsValid)
            {
                var node = val.ReadObjectField("_node");
                if (!node.IsNull)
                {
                    var entry = new Entry();
                    entry.Address = val.Address;
                    FillEntryNodeForObjectType(node, entry.Key, "_key");
                    FillEntryNodeForObjectType(node, entry.Value, "_value");
                    dest.Add(entry);
                }
            }
        }

        void FillEntryNodeForObjectType(ClrObject source, EntryNode dest, string fieldName)
        {
            var field = source.Type?.Fields.Where(f => f.Name == fieldName).FirstOrDefault();
            if (field.IsValueType)
            {
                var valueTypeField = source.ReadValueTypeField(fieldName);
                dest.Address = valueTypeField.Address;
                dest.TypeFullName = valueTypeField.Type.Name;
                if (_objectTypeFunctions.TryGetValue(valueTypeField.Type.ElementType, out var func))
                {
                    dest.Value = func(source, fieldName);
                }
            }
            else
            {
                var objectField = source.ReadObjectField(fieldName);
                dest.TypeFullName = objectField.Type.Name;
                dest.Address = objectField.Address;
                if (objectField.Type.ElementType == ClrElementType.String)
                {
                    var stringField = source.Type.GetFieldByName(fieldName);
                    dest.Value = stringField.ReadString(source, false);
                }
            }
        }
    }
}