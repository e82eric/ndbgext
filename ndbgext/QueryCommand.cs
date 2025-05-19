using System.Text.RegularExpressions;
using DbgEngExtension;
using Microsoft.Diagnostics.Runtime;
using Microsoft.Diagnostics.Runtime.Interfaces;

namespace ndbgext;

public class QueryCommand : DbgEngCommand
{
    private readonly QueryRunner _queryRunner;
    public QueryCommand(QueryRunner queryRunner, nint pUnknown, bool redirectConsoleOutput = true)
        : base(pUnknown, redirectConsoleOutput)
    {
        _queryRunner = queryRunner;
    }
    
    private static readonly Regex ArgsRx = new(
        @"^\s*(?<debug>-debug\s+)?-(?<source>mt|addr|array)\s+(?<addr>\S+)\s+(?<rest>.+)$",
        RegexOptions.IgnoreCase | RegexOptions.IgnorePatternWhitespace | RegexOptions.Compiled);
    
    private static readonly Regex FieldsRx = new(
        @"^\s*select\s+(?<fields>.*?)(\s+where|$)",
        RegexOptions.IgnoreCase | RegexOptions.IgnorePatternWhitespace | RegexOptions.Compiled);

    private static readonly Regex PredicateRx = new(
        @"^\s*.*?\bwhere\s+(?<predicate>.+)$",
        RegexOptions.IgnoreCase | RegexOptions.IgnorePatternWhitespace | RegexOptions.Compiled);

    internal void Run(string args)
    {
        var argsResult = ArgsRx.Match(args);
        if (!argsResult.Success)
        {
            Console.WriteLine("Usage: [-debug] (-mt|-addr|-array) <address> (select <f1,f2,..> | where <expr>)");
            return;
        }

        var debug= argsResult.Groups["debug"].Success;
        var sourceType= argsResult.Groups["source"].Value;
        var addrStr= argsResult.Groups["addr"].Value;
        var rest= argsResult.Groups["rest"].Value.Trim();
        
        if (!Helper.TryParseAddress(addrStr, out var address))
        {
            Console.WriteLine($"Invalid address: {addrStr}");
            return;
        }
        
        QueryRunner.Debug = debug;
        if (debug)
        {
            Console.WriteLine($"[DEBUG] {sourceType}@0x{address:X} '{rest}'");
        }

        var fieldsResult = FieldsRx.Match(rest);
        var predicateResult = PredicateRx.Match(rest);

        var predicate = predicateResult.Success
                 ? QueryExpressionParser.ParseWherePredicate(
                       predicateResult.Groups["predicate"].Value, out var p) ? p : null
                 : null;

        if (!fieldsResult.Success && predicateResult.Success && predicate != null)
        {
            foreach (var runtime in Runtimes)
            {
                IList<IClrValue>? whereResults = null;
                switch (sourceType)
                {
                    case "mt":
                        whereResults = _queryRunner.CollectObjectsWhereForMethodTable(runtime, address, predicate);
                        break;
                    case "array":
                        whereResults = _queryRunner.CollectObjectsWhereForArray(runtime, address, predicate);
                        break;
                }

                if (whereResults != null)
                {
                    foreach (var item in whereResults)
                    {
                        Console.WriteLine("Address: {0:x8}", item.Address);
                    }
                    Console.WriteLine("Number of matches: {0}", whereResults.Count);
                }
            }
        }
        else if (fieldsResult.Success)
        {
            var fieldsArgs = fieldsResult.Groups["fields"].Value;
            var fields = fieldsArgs.Split(',').Select(s => s.Trim()).ToArray();
            if (predicateResult.Success)
            {
                foreach (var runtime in Runtimes)
                {
                    List<IClrValue>? whereResults = null;
                    switch (sourceType)
                    {
                        case "mt":
                            whereResults = _queryRunner.CollectObjectsWhereForMethodTable(runtime, address, predicate);
                            break;
                        case "array":
                            whereResults = _queryRunner.CollectObjectsWhereForArray(runtime, address, predicate);
                            break;
                    }

                    if (whereResults != null)
                    {
                        _queryRunner.RunSelect(runtime, whereResults, fields);
                    }
                }
                return;
            }
        
            foreach (var runtime in Runtimes)
            {
                switch (sourceType)
                {
                    case "mt":
                    {
                        _queryRunner.RunSelect(runtime, address, fields);
                        break;
                    }
                    case "addr":
                    {
                        _queryRunner.RunSelectForAddress(runtime, address, fields);
                        break;
                    }
                    case "array":
                    {
                        _queryRunner.RunSelectForArray(runtime, address, fields);
                        break;
                    }
                }
            }
        }
    }
    
    private class TypeHandlers
    {
        public required Func<string, (bool Success, IComparable? Value)> ParseFunc { get; init; }
        public required Func<ClrRuntime, IClrType, ulong, IComparable?> ReadFunc { get; init; }
    }

    private class ClrInstanceFieldPath
    {
        public required IClrType Type { get; init; }
        public IComparable? Result { get; init; }
    }

    public class QueryRunner
    {
        public static bool Debug { private get; set; }

        private static void Log(string template, params object[]? @params)
        {
            if (Debug)
            {
                Console.WriteLine(template, @params);
            }
        }

        private static readonly Dictionary<string, TypeHandlers> TypeNameMap = new()
        {
            {
                "System.String", new TypeHandlers
                {
                    ParseFunc = s =>
                    {
                        return (true, s);
                    },
                    ReadFunc = (runtime, clrType, address) =>
                    {
                        var clrObject = runtime.Heap.GetObject(address);
                        return clrObject.AsString();
                    }
                }
            },
            {
                "System.Boolean", new TypeHandlers
                {
                    ParseFunc = s =>
                    {
                        var success = bool.TryParse(s, out var result);
                        return (success, result);
                    },
                    ReadFunc = (runtime, clrType, address) =>
                    {
                        return runtime.DataTarget.DataReader.Read<Boolean>(address);
                    }
                }
            },
            {
                "System.Guid", new TypeHandlers
                {
                    ParseFunc = s =>
                    {
                        var success = Guid.TryParse(s, out var result);
                        return (success, result);
                    },
                    ReadFunc = (runtime, clrType, address) =>
                    {
                        return runtime.DataTarget.DataReader.Read<Guid>(address);
                    }
                }
            },
            {
                "System.DateTime", new TypeHandlers
                {
                    ParseFunc = s =>
                    {
                        var success = DateTime.TryParse(s, out var result);
                        return (success, result);
                    },
                    ReadFunc = (runtime, clrType, address) =>
                    {
                        var dateTime = runtime.DataTarget.DataReader.Read<DateTime>(address);
                        return dateTime;
                    }
                }
            },
            {
                "System.DateTimeOffset", new TypeHandlers
                {
                    ParseFunc = s =>
                    {
                        var success = DateTimeOffset.TryParse(s, out var result);
                        return (success, result);
                    },
                    ReadFunc = (runtime, clrType, address) =>
                    {
                        return runtime.DataTarget.DataReader.Read<DateTimeOffset>(address);
                    }
                }
            }
        };
        
        private static readonly Dictionary<ClrElementType, TypeHandlers> ElementTypeMap = new()
        {
            {
                ClrElementType.Int16, new TypeHandlers()
                {
                    ParseFunc = s =>
                    {
                        var success = Int16.TryParse(s, out var result);
                        return (success, result);
                    },
                    ReadFunc = (runtime, clrType, address) =>
                    {
                        var type = runtime.Heap.GetObjectType(address);
                        if (type != null && type.ElementType == ClrElementType.Int16)
                        {
                            address += (ulong)IntPtr.Size;
                        }
                    
                        return runtime.DataTarget.DataReader.Read<Int16>(address);
                    }
                }
            },
            {
                ClrElementType.Int64, new TypeHandlers
                {
                    ParseFunc = s =>
                    {
                        var success = Int64.TryParse(s, out var result);
                        return (success, result);
                    },
                    ReadFunc = (runtime, clrType, address) =>
                    {
                        var type = runtime.Heap.GetObjectType(address);
                        if (type != null && type.ElementType == ClrElementType.Int64)
                        {
                            address += (ulong)IntPtr.Size;
                        }
                    
                        return runtime.DataTarget.DataReader.Read<Int64>(address);
                    }
                }
            },
            {
                ClrElementType.Int32, new TypeHandlers()
                {
                    ParseFunc = s =>
                    {
                        var success = Int32.TryParse(s, out var result);
                        return (success, result);
                    },
                    ReadFunc = (runtime, clrType, address) =>
                    {
                        var type = runtime.Heap.GetObjectType(address);
                        if (type != null && type.ElementType == ClrElementType.Int32)
                        {
                            address += (ulong)IntPtr.Size;
                        }
                    
                        return runtime.DataTarget.DataReader.Read<Int32>(address);
                    }
                }
            },
            {
                ClrElementType.Double, new TypeHandlers()
                {
                    ParseFunc = s =>
                    {
                        var success = Double.TryParse(s, out var result);
                        return (success, result);
                    },
                    ReadFunc = (runtime, clrType, address) =>
                    {
                        var type = runtime.Heap.GetObjectType(address);
                        if (type != null && type.ElementType == ClrElementType.Double)
                        {
                            address += (ulong)IntPtr.Size;
                        }
                    
                        return runtime.DataTarget.DataReader.Read<Double>(address);
                    }
                }
            },
            {
                ClrElementType.Float, new TypeHandlers
                {
                    ParseFunc = s =>
                    {
                        var success = float.TryParse(s, out var result);
                        return (success, result);
                    },
                    ReadFunc = (runtime, clrType, address) =>
                    {
                        var type = runtime.Heap.GetObjectType(address);
                        if (type != null && type.ElementType == ClrElementType.Float)
                        {
                            address += (ulong)IntPtr.Size;
                        }
                    
                        return runtime.DataTarget.DataReader.Read<float>(address);
                    }
                }
            },
            {
                ClrElementType.String, new TypeHandlers
                {
                    ParseFunc = s =>
                    {
                        return (true, s);
                    },
                    ReadFunc = (runtime, clrType, address) =>
                    {
                        var clrObject = runtime.Heap.GetObject(address);
                        return clrObject.AsString();
                    }
                }
            },
            {
                ClrElementType.Class, new TypeHandlers
                {
                    ParseFunc = s =>
                    {
                        var success = Helper.TryParseAddress(s, out var result);
                        return (success, result);
                    },
                    ReadFunc = (runtime, clrType, address) =>
                    {
                        var clrObject = runtime.Heap.GetObject(address);
                        return clrObject.Address;
                    }
                }
            },
            {
                ClrElementType.Struct, new TypeHandlers
                {
                    ParseFunc = s =>
                    {
                        var success = Helper.TryParseAddress(s, out var result);
                        return (success, result);
                    },
                    ReadFunc = (runtime, clrType, address) =>
                    {
                        return address;
                    }
                }
            },
        };
        
        private static bool TryParsePredicate(IClrType type, WherePredicate predicate, out IComparable? result)
        {
            result = null;
            if (type.Name == null)
            {
                return false;
            }

            if (!TypeNameMap.TryGetValue(type.Name, out var handlers))
            {
                if (!ElementTypeMap.TryGetValue(type.ElementType, out handlers))
                {
                    Log("Cannot find handler for: {0}", type.Name);
                    return false;
                }
            }

            var parseResult = handlers.ParseFunc(predicate.Value);
            result = parseResult.Value;
            return parseResult.Success;
        }

        private static bool TryGetFieldValue(ClrRuntime runtime, ulong address, IClrType resultType, out IComparable? result)
        {
            result = null;

            if (resultType.Name is { } name && TypeNameMap.TryGetValue(name, out var h))
            {
                result = h.ReadFunc(runtime, resultType, address);
                return true;
            }
            if (ElementTypeMap.TryGetValue(resultType.ElementType, out h))
            {
                result = h.ReadFunc(runtime, resultType, address);
                return true;
            }
                
            Log("Unable to get field value for {0} {1} {2}", resultType.Name, resultType.ElementType, address);
            return false;
        }

        private static bool TryParseFieldPathFromFieldExpression(ClrRuntime runtime, string field, IClrValue obj, out ClrInstanceFieldPath? tail)
        {
            tail = null;
            if (field == "$this")
            {
                if (TryGetFieldValue(runtime, obj.Address, obj.Type, out var result))
                {
                    tail = new ClrInstanceFieldPath
                    {
                        Type = obj.Type,
                        Result = result
                    };
                }
                return true;
            }
            
            var splitFields = field.Split(".");
            var current = obj.Type;
            var currentObj = obj;
            for (var i = 0; i < splitFields.Length; i++)
            {
                var localI = i;
                var splitFieldObj = current?.Fields.FirstOrDefault(f => f.Name == splitFields[localI]);
                if (splitFieldObj == null)
                {
                    return false;
                }

                if (!TryGetAddress(runtime, currentObj, splitFieldObj, out var address))
                {
                    return false;
                }
                
                if (splitFieldObj is { IsValueType: true, Name: not null })
                {
                    current = splitFieldObj.Type;
                    currentObj = currentObj.ReadValueTypeField(splitFieldObj.Name);
                }
                else
                {
                    currentObj = runtime.Heap.GetObject(address);
                    current = runtime.Heap.GetObjectType(address);
                }

                if (current == null)
                {
                    return false;
                }
                
                if (i >= splitFields.Length - 1)
                {
                    if (TryGetFieldValue(runtime, address, current, out var result))
                    {
                        tail = new ClrInstanceFieldPath
                        {
                            Type = current,
                            Result = result
                        };
                    }
                    return true;
                }
            }

            return false;
        }

        private static bool TryGetAddress(ClrRuntime runtime, IClrValue obj, IClrInstanceField field, out ulong result)
        {
            result = 0;
            var fieldAddress = obj.Address + (ulong)field.Offset;
            if (obj.Type is { IsObjectReference: true })
            {
                fieldAddress += (ulong)IntPtr.Size;
            }

            if (field.IsObjectReference)
            {
                if (!runtime.DataTarget.DataReader.ReadPointer(fieldAddress, out var fieldPtr))
                {
                    return false;
                }
                fieldAddress = fieldPtr;
            }

            result = fieldAddress;
            return true;
        }

        private List<IClrValue> CollectObjectsWhere(ClrRuntime runtime, IReadOnlyList<IClrValue> objs, WherePredicate predicate)
        {
            var result = new List<IClrValue>();

            foreach (var obj in objs)
            {
                var matched = false;
                if (!TryParseFieldPathFromFieldExpression(runtime, predicate.Field, obj, out var tailInstanceFieldPath))
                {
                    continue;
                }
                if (tailInstanceFieldPath == null)
                {
                    continue;
                }

                var fieldValue = tailInstanceFieldPath.Result;
                var actualType = tailInstanceFieldPath.Type;

                if (fieldValue == null)
                {
                    continue;
                }

                if (!TryParsePredicate(actualType, predicate, out var predicateValue))
                {
                    return result;
                }

                if (predicateValue == null)
                {
                    continue;
                }
                    
                switch (predicate.Operator)
                {
                    case WhereOperator.GreaterThan:
                        if (fieldValue.CompareTo(predicateValue) > 0)
                        {
                            matched = true;
                        }
                        break;
                    case WhereOperator.GreaterThanOrEqual:
                        if (fieldValue.CompareTo(predicateValue) >= 0)
                        {
                            matched = true;
                        }
                        break;
                    case WhereOperator.LessThan:
                        if (fieldValue.CompareTo(predicateValue) < 0)
                        {
                            matched = true;
                        }
                        break;
                    case WhereOperator.LessThanOrEqual:
                        if (fieldValue.CompareTo(predicateValue) <= 0)
                        {
                            matched = true;
                        }
                        break;
                    case WhereOperator.Equals:
                        if (Equals(fieldValue, predicateValue))
                        {
                            matched = true;
                        }
                        break;
                    case WhereOperator.Matches:
                        if (fieldValue is string && predicateValue is string)
                        {
                            var regex = new Regex((string)predicateValue);
                            if(regex.IsMatch((string)fieldValue))
                            {
                                matched = true;
                            }
                        }
                        break;
                }
                    
                if (matched)
                {
                    result.Add(obj);
                }
            }

            return result;
        }
        
        public List<IClrValue> CollectObjectsWhereForMethodTable(ClrRuntime runtime, ulong methodTable, WherePredicate predicate)
        {
            var heap = runtime.Heap;
            var objs = heap.EnumerateObjects().Where(o => o.Type?.MethodTable == methodTable).Cast<IClrValue>().ToList();
            return CollectObjectsWhere(runtime, objs, predicate);
        }
        
        public List<IClrValue> CollectObjectsWhereForArray(ClrRuntime runtime, ulong address, WherePredicate predicate)
        {
            var heap = runtime.Heap;
            var obj = heap.GetObject(address);
            var array = obj.AsArray();
            var items = new List<IClrValue>();
            for (int i = 0; i < array.GetLength(0); i++)
            {
                if (array.Type != null && array.Type.ComponentType != null && array.Type.ComponentType.IsObjectReference)
                {
                    var item = array.GetObjectValue(i);
                    items.Add(item);
                }
                else
                {
                    var item = array.GetStructValue(i);
                    items.Add(item);
                }
            }
            return CollectObjectsWhere(runtime, items, predicate);
        }

        public void RunSelect(ClrRuntime runtime, IReadOnlyList<IClrValue> objs, IReadOnlyList<string> fields)
        {
            foreach (var obj in objs)
            {
                Console.WriteLine("Address {0:x8}", obj.Address);
                foreach (var field in fields)
                {
                    if (TryParseFieldPathFromFieldExpression(runtime, field, obj, out var tailInstanceFieldPath))
                    {
                        var toPrint = tailInstanceFieldPath.Result;
                        if (toPrint is ulong)
                        {
                            toPrint = $"{toPrint:x8}";
                        }
                        Console.WriteLine("  {0}: {1}", field, toPrint);
                    }
                    else
                    {
                        Log("Unable to parse select field expression {0}", field);
                    }
                }
            }
        }
        
        public void RunSelectForAddress(ClrRuntime runtime, ulong address, IReadOnlyList<string> fields)
        {
            var clrObject = runtime.Heap.GetObject(address);
            if (!clrObject.IsValid || clrObject.IsNull || clrObject.Type == null)
            {
                return;
            }
            
            RunSelect(runtime, [clrObject], fields);
        }

        public void RunSelectForArray(ClrRuntime runtime, ulong address, IReadOnlyList<string> fields)
        {
            var clrObject = runtime.Heap.GetObject(address);
            var array = clrObject.AsArray();
            var items = new List<IClrValue>();
            for (var i = 0; i < array.GetLength(0); i++)
            {
                if (array.Type.ComponentType != null && array.Type.ComponentType.IsObjectReference)
                {
                    var item = array.GetObjectValue(i);
                    if (!item.IsNull)
                    {
                        items.Add(item);
                    }
                }
                else
                {
                    var item = array.GetStructValue(i);
                    if (item.IsValid)
                    {
                        items.Add(item);
                    }
                }
            }
            if (!items.Any())
            {
                return;
            }
            
            RunSelect(runtime, items, fields);
        }
        
        public void RunSelect(ClrRuntime runtime, ulong methodTable, IReadOnlyList<string> fields)
        {
            var objs = runtime.Heap.EnumerateObjects()
                .Where(o => o.Type?.MethodTable == methodTable)
                .Cast<IClrValue>().ToList();
            RunSelect(runtime, objs, fields);
        }
    }
}

public enum WhereOperator
{
    Equals,
    GreaterThan,
    LessThan,
    GreaterThanOrEqual,
    LessThanOrEqual,
    Matches,
    NotEquals
}

public class WherePredicate
{
    public required string Field { get; init; }
    public required string Value { get; init; }
    public WhereOperator Operator { get; init; }
}

public class QueryExpressionParser
{
    private static readonly Dictionary<string, WhereOperator> Operators = new()
    {
        { " == ", WhereOperator.Equals },
        { "==", WhereOperator.Equals },
        { " >= ", WhereOperator.GreaterThanOrEqual },
        { ">=", WhereOperator.GreaterThanOrEqual },
        { " <= ", WhereOperator.LessThanOrEqual },
        { "<=", WhereOperator.LessThanOrEqual },
        { " > ", WhereOperator.GreaterThan },
        { ">", WhereOperator.GreaterThan },
        { " < ", WhereOperator.LessThan },
        { "<", WhereOperator.LessThan },
        { " != ", WhereOperator.NotEquals },
        { "!=", WhereOperator.NotEquals },
        { " =~ ", WhereOperator.Matches },
        { "=~", WhereOperator.Matches }
    };
    
    public static bool ParseWherePredicate(string expression, out WherePredicate? result)
    {
        result = null;
        var operatorIndex = -1;
        var opStrLen = -1;
        WhereOperator? op = null;

        foreach (var opString in Operators.Keys)
        {
            int index = expression.IndexOf(opString, StringComparison.Ordinal);
            if (index >= 0)
            {
                operatorIndex = index;
                op = Operators[opString];
                opStrLen = opString.Length;
                break;
            }            
        }

        if (op != null)
        {
            var field = expression.Substring(0, operatorIndex).Trim();
            var value = expression.Substring(
                operatorIndex + opStrLen,
                expression.Length - operatorIndex - opStrLen).Trim();
            result = new WherePredicate()
            {
                Field = field,
                Operator = op.Value,
                Value = value
            };
            return true;
        }
        
        return false;
    }
}
