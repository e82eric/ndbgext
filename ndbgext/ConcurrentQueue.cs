﻿using System.Text.RegularExpressions;
using DbgEngExtension;
using Microsoft.Diagnostics.Runtime;

namespace ndbgext;

public class ConcurrentQueueCommand : DbgEngCommand
{
    private readonly ConcurrentQueue _queue = new ConcurrentQueue();

    public ConcurrentQueueCommand(nint pUnknown, bool redirectConsoleOutput = true)
        : base(pUnknown, redirectConsoleOutput)
    {
    }

    public ConcurrentQueueCommand(IDisposable dbgeng, bool redirectConsoleOutput = false)
        : base(dbgeng, redirectConsoleOutput)
    {
    }

    internal void Run(string args)
    {
        if (string.IsNullOrEmpty(args))
        {
            Console.WriteLine("Missing address of a ConcurrentQueue");
            return;
        }

        var arguments = args.Split(' ');
        if (arguments[0] == "-list")
        {
            foreach (var runtime in Runtimes)
            {
                _queue.List(runtime);
            }
            return;
        }

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

        foreach (var runtime in this.Runtimes)
        {
            _queue.Show(runtime, reference);
        }
    }
}

public class ConcurrentQueue
{
    public void List(ClrRuntime runtime)
    {
        var pattern = @"^System\.Collections\.Concurrent\.ConcurrentQueue<.*>$";
        var regex = new Regex(pattern);
        var heap = runtime.Heap;

        if (!heap.CanWalkHeap)
        {
            Console.WriteLine("Cannot walk the heap!");
        }
        else
        {
            foreach (ulong obj in heap.EnumerateObjects())
            {
                var type = heap.GetObjectType(obj);

                // If heap corruption, continue past this object.
                if (type == null)
                    continue;

                if (regex.IsMatch(type.Name))
                {
                    var isNetCore = Helper.IsNetCore(runtime);
                    var dictionary = heap.GetObject(obj);
                    var items = isNetCore ? GetQueueItemsCore(dictionary):  GetQueueItemsFramework(dictionary);

                    Console.WriteLine("{0:X} {1} Length: {2}", obj, type.Name, items.Count);
                }
            }
        }
    }
    public void Show(ClrRuntime runtime, ulong address)
    {
        var heap = runtime.Heap;
        var obj = heap.GetObject(address);
        if (!obj.IsValid || obj.IsNull)
        {
            Console.WriteLine("object is not valid or null");
        }
        var isNetCore = Helper.IsNetCore(runtime);
        var items = isNetCore ? GetQueueItemsCore(obj):  GetQueueItemsFramework(obj);
        Console.WriteLine("{0}", obj.Type.Name);
        Console.WriteLine("Number of items: {0}", items.Count);
        foreach (var item in items)
        {
            Console.WriteLine("{0:X} {1} {2}", item.Address, item.TypeName, item.Value);
        }
    }

    public IReadOnlyList<Result> GetQueueItemsCore(ClrObject queueObject)
    {
        var result = new List<Result>();
        var current = queueObject.ReadObjectField("_head");
        while (current.IsValid && !current.IsNull)
        {
            var array = current.ReadObjectField("_slots").AsArray();
            var headAndTail = current.ReadValueTypeField("_headAndTail");
            var start = headAndTail.ReadField<Int32>("Head");
            var end = headAndTail.ReadField<Int32>("Tail");
            for (var i = start; i < end && i < array.Length; i++)
            {
                var itemStruct = array.GetStructValue(i);
                Result itemResult = null;
                try
                {
                    var itemObject = itemStruct.ReadObjectField("Item");
                    itemResult = new Result
                    {
                        Address = itemObject.Address,
                        TypeName = itemStruct.Type.Name
                    };
                    if (itemObject.Type.ElementType == ClrElementType.String)
                    {
                        itemResult.Value = itemObject.AsString();
                    }
                }
                catch (Exception e)
                {
                    var innerItemStruct = itemStruct.ReadValueTypeField("Item");
                    itemResult = new Result
                    {
                        Address = innerItemStruct.Address,
                        TypeName = innerItemStruct.Type.Name
                    };
                    switch (innerItemStruct.Type.ElementType)
                    {
                        case ClrElementType.Int32:
                            itemResult.Value = itemStruct.ReadField<Int32>("Item").ToString();
                            break;
                    }
                }

                if (itemResult != null)
                {
                    result.Add(itemResult);
                }
            }

            current = current.ReadObjectField("_nextSegment");
        }

        return result;
    }

    IReadOnlyList<Result> GetQueueItemsFramework(ClrObject queueObject)
    {
        var result = new List<Result>();
        var currentSegment = queueObject.ReadObjectField("m_head");
        while (currentSegment is { IsNull: false, IsValid: true })
        {
            var arrayField = currentSegment.ReadObjectField("m_array");
            var array = arrayField.AsArray();
            Int32 start = currentSegment.ReadField<Int32>("m_low");
            Int32 end = currentSegment.ReadField<Int32>("m_high");
            for (var i = start; i <= end; i++)
            {
                Result resultItem = null;
                try
                {
                    var item = array.GetObjectValue(i);
                    if (!item.IsNull || item.IsValid)
                    {
                        resultItem = new Result
                        {
                            TypeName = item.Type.Name,
                            Address = item.Address,
                        };
                        if (item.Type.ElementType == ClrElementType.String)
                        {
                            resultItem.Value = item.AsString();
                        }
                    }
                }
                catch (Exception e)
                {
                    var structValue = array.GetStructValue(i);
                    if (structValue.IsValid)
                    {
                        resultItem = new Result()
                        {
                            Address = structValue.Address,
                            TypeName = structValue.Type.Name
                        };
                        switch (structValue.Type.ElementType)
                        {
                            case ClrElementType.Int16:
                                resultItem.Value = array.ReadValues<Int16>(i, 1)[0].ToString();
                                break;
                            case ClrElementType.Int32:
                                resultItem.Value = array.ReadValues<Int32>(i, 1)[0].ToString();
                                break;
                            case ClrElementType.Int64:
                                resultItem.Value = array.ReadValues<Int64>(i, 1)[0].ToString();
                                break;
                            case ClrElementType.Boolean:
                                resultItem.Value = array.ReadValues<Boolean>(i, 1)[0].ToString();
                                break;
                            case ClrElementType.UInt16:
                                resultItem.Value = array.ReadValues<UInt16>(i, 1)[0].ToString();
                                break;
                            case ClrElementType.UInt32:
                                resultItem.Value = array.ReadValues<UInt32>(i, 1)[0].ToString();
                                break;
                            case ClrElementType.UInt64:
                                resultItem.Value = array.ReadValues<UInt64>(i, 1)[0].ToString();
                                break;
                        }
                    }
                }

                if (resultItem != null)
                {
                    ((IList<Result>)result).Add(resultItem);
                }
            }

            currentSegment.TryReadObjectField("m_head", out currentSegment);
        }

        return result;
    }
}

public class Result
{
    public string TypeName { get; set; }
    public ulong Address { get; set; }
    public string Value { get; set; }
}