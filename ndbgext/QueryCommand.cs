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

        var predicates = predicateResult.Success ? QueryExpressionParser.ParseWherePredicate(
                       predicateResult.Groups["predicate"].Value) : null;

        if (!fieldsResult.Success && predicateResult.Success && predicates != null)
        {
            foreach (var runtime in Runtimes)
            {
                IList<IClrValue>? whereResults = null;
                switch (sourceType)
                {
                    case "mt":
                        whereResults = _queryRunner.CollectObjectsWhereForMethodTable(runtime, address, predicates);
                        break;
                    case "array":
                        whereResults = _queryRunner.CollectObjectsWhereForArray(runtime, address, predicates);
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
                            whereResults = _queryRunner.CollectObjectsWhereForMethodTable(runtime, address, predicates);
                            break;
                        case "array":
                            whereResults = _queryRunner.CollectObjectsWhereForArray(runtime, address, predicates);
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
        public string FieldName { get; set; }
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
        
        private static bool TryParseFieldPathFromFieldExpression(
            ClrRuntime runtime,
            string field,
            IClrValue obj,
            out IReadOnlyList<ClrInstanceFieldPath>? tail)
        {
            tail = null;

            if (field == "*")
            {
                var list = GetFieldsValuesForStar(runtime, obj.Type, obj, []);
                tail = list;
                return true;
            }

            if (field == "$this")
            {
                if (obj.Type == null)
                {
                    return false;
                }
                
                if (TryGetFieldValue(runtime, obj.Address, obj.Type, out var result))
                {
                    tail = new[]
                    {
                        new ClrInstanceFieldPath
                        {
                            Type   = obj.Type,
                            Result = result
                        }
                    };
                }
                return true;
            }

            var splitFields = field.Split('.');
            var current     = obj.Type;
            var currentObj  = obj;

            for (var i = 0; i < splitFields.Length; i++)
            {
                var segment = splitFields[i];

                if (segment == "*" && i == splitFields.Length - 1)
                {
                    var list = GetFieldsValuesForStar(runtime, current, currentObj, splitFields[..^1]);
                    tail = list;
                    return true;
                }

                var fld = current?.Fields.FirstOrDefault(f => f.Name == segment);
                if (fld == null)
                {
                    return false;
                }

                if (!TryGetAddress(runtime, currentObj, fld, out var nextAddr))
                {
                    return false;
                }

                if (fld.IsValueType)
                {
                    if (fld.Name == null)
                    {
                        return false;
                    }
                    current = fld.Type;
                    currentObj = currentObj.ReadValueTypeField(fld.Name);
                }
                else
                {
                    currentObj = runtime.Heap.GetObject(nextAddr);
                    current = runtime.Heap.GetObjectType(nextAddr);
                }

                if (current == null)
                {
                    return false;
                }

                if (i == splitFields.Length - 1)
                {
                    if (TryGetFieldValue(runtime, nextAddr, current, out var result))
                    {
                        tail =
                        [
                            new ClrInstanceFieldPath
                            {
                                FieldName = fld.Name,
                                Type = current,
                                Result = result
                            }
                        ];
                    }
                    return true;
                }
            }

            return false;
        }

        private static List<ClrInstanceFieldPath> GetFieldsValuesForStar(ClrRuntime runtime, IClrType? current, IClrValue currentObj, IReadOnlyList<string> fieldPrefix)
        {
            var list = new List<ClrInstanceFieldPath>();

            foreach (var f in current.Fields)
            {
                if (!TryGetAddress(runtime, currentObj, f, out var addr))
                {
                    continue;
                }

                IClrType? actualType = runtime.Heap.GetObjectType(addr);
                actualType = actualType != null ? actualType : f.Type;
                if (actualType != null)
                {
                    if (TryGetFieldValue(runtime, addr, actualType, out var value))
                    {
                        list.Add(new ClrInstanceFieldPath
                        {
                            FieldName = string.Join(".", fieldPrefix.Append(f.Name)),
                            Type      = f.Type,
                            Result    = value
                        });
                    }
                }
            }

            return list;
        }

        private static bool TryParseFieldPathFromFieldExpression(ClrRuntime runtime, string field, IClrValue obj, out ClrInstanceFieldPath? tail)
        {
            tail = null;
            if (TryParseFieldPathFromFieldExpression(runtime, field, obj, out IReadOnlyList<ClrInstanceFieldPath>? fields))
            {
                if (fields != null && fields.Count == 1)
                {
                    tail = fields.Single();
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

        private List<IClrValue> CollectObjectsWhere(
            ClrRuntime runtime,
            IReadOnlyList<IClrValue> objs,
            IReadOnlyList<WherePredicate> predicates)
        {
            List<IClrValue> result = new List<IClrValue>();

            foreach (IClrValue obj in objs)
            {
                bool matchesAll = true;

                foreach (WherePredicate predicate in predicates)
                {
                    if (!TryParseFieldPathFromFieldExpression(runtime, predicate.Field, obj,
                            out ClrInstanceFieldPath? tailInstanceFieldPath) ||
                        tailInstanceFieldPath == null)
                    {
                        matchesAll = false;
                        break;
                    }

                    var fieldValue = tailInstanceFieldPath.Result;
                    var actualType = tailInstanceFieldPath.Type;

                    if (fieldValue == null ||
                        !TryParsePredicate(actualType, predicate, out IComparable? predicateValue) ||
                        predicateValue == null)
                    {
                        matchesAll = false;
                        break;
                    }

                    bool thisPredicateMatches;
                    switch (predicate.Operator)
                    {
                        case WhereOperator.GreaterThan:
                            thisPredicateMatches = fieldValue is { } c1 && c1.CompareTo(predicateValue) > 0;
                            break;
                        case WhereOperator.GreaterThanOrEqual:
                            thisPredicateMatches = fieldValue is { } c2 && c2.CompareTo(predicateValue) >= 0;
                            break;
                        case WhereOperator.LessThan:
                            thisPredicateMatches = fieldValue is { } c3 && c3.CompareTo(predicateValue) < 0;
                            break;
                        case WhereOperator.LessThanOrEqual:
                            thisPredicateMatches = fieldValue is { } c4 && c4.CompareTo(predicateValue) <= 0;
                            break;
                        case WhereOperator.Equals:
                            thisPredicateMatches = Equals(fieldValue, predicateValue);
                            break;
                        case WhereOperator.Matches when fieldValue is string s1:
                            Regex rx = new Regex(s1);
                            thisPredicateMatches = rx.IsMatch(s1);
                            break;
                        default:
                            thisPredicateMatches = false;
                            break;
                    }

                    if (!thisPredicateMatches)
                    {
                        matchesAll = false;
                        break;
                    }
                }

                if (matchesAll)
                    result.Add(obj);
            }

            return result;
        }
        
        public List<IClrValue> CollectObjectsWhereForMethodTable(
            ClrRuntime runtime,
            ulong methodTable,
            IReadOnlyList<WherePredicate> predicate)
        {
            var heap = runtime.Heap;
            var objs = heap.EnumerateObjects().Where(o => o.Type?.MethodTable == methodTable).Cast<IClrValue>().ToList();
            return CollectObjectsWhere(runtime, objs, predicate);
        }
        
        public List<IClrValue> CollectObjectsWhereForArray(
            ClrRuntime runtime,
            ulong address,
            IReadOnlyList<WherePredicate> predicate)
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
                    if (TryParseFieldPathFromFieldExpression(runtime, field, obj, out IReadOnlyList<ClrInstanceFieldPath> tailInstanceFieldPaths))
                    {
                        foreach (var tailInstanceFieldPath in tailInstanceFieldPaths)
                        {
                            var toPrint = tailInstanceFieldPath.Result;
                            if (toPrint is ulong)
                            {
                                toPrint = $"{toPrint:x8}";
                            }
                            Console.WriteLine("  {0}: {1}", tailInstanceFieldPath.FieldName, toPrint);
                        }
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
        { " >= ", WhereOperator.GreaterThanOrEqual },
        { " <= ", WhereOperator.LessThanOrEqual },
        { " > ", WhereOperator.GreaterThan },
        { " < ", WhereOperator.LessThan },
        { " != ", WhereOperator.NotEquals },
        { " =~ ", WhereOperator.Matches },
    };
    
    private static readonly Regex AndSplitter = new(
        @"\band\b(?=(?:[^']*'(?:[^'\\]|\\.|'')*')*[^']*$)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static IReadOnlyList<WherePredicate> ParseWherePredicate(string expression)
    {
        var result = new List<WherePredicate>();

        var predicateStrs = AndSplitter
            .Split(expression)
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .ToArray();

        foreach (var predicateStr in predicateStrs)
        {
            int  operatorIndex = -1;
            int  opStrLen      = -1;
            WhereOperator? op  = null;

            foreach (var opString in Operators.Keys)
            {
                var idx = predicateStr.IndexOf(opString, StringComparison.Ordinal);
                if (idx >= 0)
                {
                    operatorIndex = idx;
                    op = Operators[opString];
                    opStrLen = opString.Length;
                    break;
                }
            }

            if (op is not null)
            {
                var field = predicateStr[..operatorIndex].Trim();
                var raw = predicateStr[(operatorIndex + opStrLen)..].Trim();

                if (raw.Length >= 2 && raw[0] == '\'' && raw[^1] == '\'')
                {
                    var value = raw[1..^1].Replace("''", "'").Replace("\\'", "'");

                    result.Add(new WherePredicate
                    {
                        Field = field,
                        Operator = op.Value,
                        Value = value
                    });
                }
                else
                {
                    throw new FormatException($"Value \"{raw}\" must be in single quotes.");
                }
            }
        }

        return result;
    }
}
