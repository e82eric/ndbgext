using System.Text.RegularExpressions;
using DbgEngExtension;
using Microsoft.Diagnostics.Runtime;

namespace ndbgext;

public class QueryCommand : DbgEngCommand
{
    private readonly QueryRunner _queryRunner;
    public QueryCommand(QueryRunner queryRunner, nint pUnknown, bool redirectConsoleOutput = true)
        : base(pUnknown, redirectConsoleOutput)
    {
        _queryRunner = queryRunner;
    }

    internal void Run(string args)
    {
        var selectWhereExpression = @"^-(?<source>mt|addr|array)\s+(?<address>\w+)\s+select\s+(?<fields>[^,]+(?:\s*,\s*[^,]+)*)\s+where\s+(?<predicate>.*)$";
        var selectWhereRegex = new Regex(selectWhereExpression, RegexOptions.IgnoreCase);
        var selectWhereResult = selectWhereRegex.Match(args);

        if (selectWhereResult.Success)
        {
            var sourceType = selectWhereResult.Groups["source"].Value;
            var sourceAddress = selectWhereResult.Groups["address"].Value;
            var fieldsArgs = selectWhereResult.Groups["fields"].Value;
            var fields = fieldsArgs.Split(',').Select(s => s.Trim()).ToArray();
            var predicateStr = selectWhereResult.Groups["predicate"].Value;

            if (Helper.TryParseAddress(sourceAddress, out var address))
            {
                if (QueryExpressionParser.ParseWherePredicate(predicateStr, out var predicate) && predicate != null)
                {
                    foreach (var runtime in Runtimes)
                    {
                        List<ClrObject> whereResults = null;
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
                }
            }

            return;
        }
        
        var argsSplit = args.Split([' '], 4);
        if (argsSplit.Length >= 4 && (argsSplit[0] == "-mt" || argsSplit[0] == "-addr" || argsSplit[0] == "-array") && Helper.TryParseAddress(argsSplit[1], out var methodTableOrAddress))
        {
            if (argsSplit[2] == "select")
            {
                var fields = argsSplit[3].Split(',');

                foreach (var runtime in this.Runtimes)
                {
                    switch (argsSplit[0])
                    {
                        case "-mt":
                            _queryRunner.RunSelect(runtime, methodTableOrAddress, fields);
                            break;
                        case "-addr":
                            _queryRunner.RunSelectForAddress(runtime, methodTableOrAddress, fields);
                            break;
                        case "-array":
                            _queryRunner.RunSelectForArray(runtime, methodTableOrAddress, fields);
                            break;
                    }
                }
            }
            else if (argsSplit[2] == "where")
            {
                var predicateStr = argsSplit[3];
                if (QueryExpressionParser.ParseWherePredicate(predicateStr, out var predicate) && predicate != null)
                {
                    foreach (var runtime in Runtimes)
                    {
                        _queryRunner.RunWhere(runtime, methodTableOrAddress, predicate);
                    }
                }
            }
        }
    }
    
    private class TypeHandlers
    {
        public required Func<string, (bool Success, IComparable? Value)> ParseFunc { get; init; }
        public required Func<ClrRuntime, ulong, IComparable?> ReadFunc { get; init; }
    }

    private class ClrInstanceFieldPath
    {
        public required ClrInstanceField Value { get; init; }
        public ClrInstanceFieldPath? Next { get; set; }
    }

    public class QueryRunner
    {
        private static readonly Dictionary<string, TypeHandlers> TypeNameMap = new()
        {
            {
                "System.Boolean", new TypeHandlers
                {
                    ParseFunc = s =>
                    {
                        var success = bool.TryParse(s, out var result);
                        return (success, result);
                    },
                    ReadFunc = (runtime, address) =>
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
                    ReadFunc = (runtime, address) =>
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
                    ReadFunc = (runtime, address) =>
                    {
                        return runtime.DataTarget.DataReader.Read<DateTime>(address);
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
                    ReadFunc = (runtime, address) =>
                    {
                        return runtime.DataTarget.DataReader.Read<DateTimeOffset>(address);
                    }
                }
            }
        };
        
        private static readonly Dictionary<ClrElementType, TypeHandlers> ElementTypeMap = new()
        {
            { ClrElementType.Int32, new TypeHandlers()
            {
                ParseFunc = s =>
                {
                    var success = Int32.TryParse(s, out var result);
                    return (success, result);
                },
                ReadFunc = (runtime, address) =>
                {
                    return runtime.DataTarget.DataReader.Read<Int32>(address);
                }
            }},
            { ClrElementType.String, new TypeHandlers
            {
                ParseFunc = s =>
                {
                    return (true, s);
                },
                ReadFunc = (runtime, address) =>
                {
                    var clrObject = runtime.Heap.GetObject(address);
                    return clrObject.AsString();
                }
            }},
            { ClrElementType.Class, new TypeHandlers
            {
                ParseFunc = s =>
                {
                    var success = Helper.TryParseAddress(s, out var result);
                    return (success, result);
                },
                ReadFunc = (runtime, address) =>
                {
                    var clrObject = runtime.Heap.GetObject(address);
                    return clrObject.Address;
                }
            }},
        };
        
        private static bool TryParsePredicate(ClrType type, WherePredicate predicate, out IComparable? result)
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
                    Console.WriteLine("Cannot find handler for: {0}", type.Name);
                    return false;
                }
            }

            var parseResult = handlers.ParseFunc(predicate.Value);
            result = parseResult.Value;
            return parseResult.Success;
        }

        private static bool TryGetFieldValue(ClrRuntime runtime, ClrType type, ulong address, out IComparable? result)
        {
            result = null;
            if (type.Name == null)
            {
                Console.WriteLine("Type Name was null {0}", address);
                return false;
            }

            if (!TypeNameMap.TryGetValue(type.Name, out var handlers))
            {
                if (!ElementTypeMap.TryGetValue(type.ElementType, out handlers))
                {
                    Console.WriteLine("Unable to get field value for {0} {1} {2}", type.Name, type.ElementType, address);
                    return false;
                }
            }

            var readResult = handlers.ReadFunc(runtime, address);
            result = readResult;
            return true;
        }

        private static bool TryGetFieldValueFromFieldPath(
            ClrRuntime runtime,
            ClrObject obj,
            ClrInstanceFieldPath fieldPath,
            out IComparable? result)
        {
            result = null;
            var current = fieldPath;
            var currentObject = obj;
            while (current != null)
            {
                var instanceField = current.Value;
                if (instanceField.Type == null)
                {
                    Console.WriteLine("InstanceField.Type is null {0}", fieldPath.Value.Name);
                    return false;
                }
                
                if (!TryGetAddress(runtime, currentObject, instanceField, out var fieldAddress))
                {
                    Console.WriteLine("Unable to get field path address {0}", fieldPath.Value.Name);
                    return false;
                }

                if (current.Next == null)
                {
                    var success = TryGetFieldValue(runtime, instanceField.Type, fieldAddress, out result);
                    return success;
                }

                currentObject = runtime.Heap.GetObject(fieldAddress);
                current = current.Next;
            }

            return false;
        }
        
        private static bool TryParseFieldPathFromFieldExpression(ClrRuntime runtime, string field, ClrObject obj, out ClrInstanceFieldPath? head)
        {
            head = null;
            ClrInstanceFieldPath? tail = null;
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

                if (head == null)
                {
                    head = new ClrInstanceFieldPath
                    {
                        Value = splitFieldObj
                    };
                    tail = head;
                }
                else
                {
                    tail.Next = new ClrInstanceFieldPath
                    {
                        Value = splitFieldObj
                    };
                    tail = tail.Next;
                }

                if (!TryGetAddress(runtime, currentObj, tail.Value, out var address))
                {
                    return false;
                }

                currentObj = runtime.Heap.GetObject(address);
                current = currentObj.Type;
                if (current == null)
                {
                    current = tail.Value.Type;
                }

                if (i >= splitFields.Length - 1)
                {
                    return true;
                }
            }

            return false;
        }
        
        private static bool TryParseFieldPathFromFieldExpression(string field, ClrType type, out ClrInstanceFieldPath? head)
        {
            head = null;
            ClrInstanceFieldPath? tail = null;
            var splitFields = field.Split(".");
            var current = type;
            for (var i = 0; i < splitFields.Length; i++)
            {
                var localI = i;
                var splitFieldObj = current?.Fields.FirstOrDefault(f => f.Name == splitFields[localI]);
                if (splitFieldObj == null)
                {
                    return false;
                }

                if (head == null)
                {
                    head = new ClrInstanceFieldPath
                    {
                        Value = splitFieldObj
                    };
                    tail = head;
                }
                else
                {
                    tail.Next = new ClrInstanceFieldPath
                    {
                        Value = splitFieldObj
                    };
                    tail = tail.Next;
                }
                current = tail.Value.Type;

                if (i >= splitFields.Length - 1)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryGetAddress(ClrRuntime runtime, ClrObject obj, ClrInstanceField field, out ulong result)
        {
            result = 0;
            
            var fieldAddress = obj.Type == null || obj.Type.IsValueType ? obj.Address + (ulong)field.Offset : obj.Address + (ulong)(field.Offset + IntPtr.Size);
            if (!field.IsValueType)
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
        
        public List<ClrObject> CollectObjectsWhere(ClrRuntime runtime, IReadOnlyList<ClrObject> objs, WherePredicate predicate)
        {
            var result = new List<ClrObject>();
            IComparable? fieldValue;
            IComparable? predicateValue;
            
            var numberOfMatches = 0;
            
            foreach (var obj in objs)
            {
                if (!TryParseFieldPathFromFieldExpression(runtime, predicate.Field, obj, out var instanceFieldPath))
                {
                    continue;
                }
                
                var current = instanceFieldPath;
                while (current?.Next != null)
                {
                    current = current.Next;
                }
                
                if (!TryParsePredicate(current.Value.Type, predicate, out predicateValue))
                {
                    return result;
                }
                
                var matched = false;
                if (predicate.Field == "$this")
                {
                    if (!TryGetFieldValue(runtime, obj.Type, obj.Address, out fieldValue))
                    {
                        continue;
                    }
                }
                else
                {
                    if (instanceFieldPath == null)
                    {
                        continue;
                    }
                    if (!TryGetFieldValueFromFieldPath(runtime, obj, instanceFieldPath, out fieldValue))
                    {
                        continue;
                    }
                }

                if (fieldValue == null)
                {
                    continue;
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
                    numberOfMatches++;
                    result.Add(obj);
                }
            }

            return result;
        }
        
        public List<ClrObject> CollectObjectsWhereForMethodTable(ClrRuntime runtime, ulong methodTable, WherePredicate predicate)
        {
            var heap = runtime.Heap;
            var objs = heap.EnumerateObjects().Where(o => o.Type?.MethodTable == methodTable).ToList();
            return CollectObjectsWhere(runtime, objs, predicate);
        }
        
        public List<ClrObject> CollectObjectsWhereForArray(ClrRuntime runtime, ulong address, WherePredicate predicate)
        {
            var heap = runtime.Heap;
            var obj = heap.GetObject(address);
            var array = obj.AsArray();
            var items = new List<ClrObject>();
            for (int i = 0; i < array.GetLength(0); i++)
            {
                var item = array.GetObjectValue(i);
                items.Add(item);
            }
            return CollectObjectsWhere(runtime, items, predicate);
        }

        public void RunWhere(ClrRuntime runtime, ulong methodTable, WherePredicate predicate)
        {
            var items = CollectObjectsWhereForMethodTable(runtime, methodTable, predicate);
            foreach (var item in items)
            {
                Console.WriteLine("Address: {0:x8}", item.Address);
            }
            
            Console.WriteLine("Number of matches: {0}", items.Count);
        }

        public void RunSelect(ClrRuntime runtime, IReadOnlyList<ClrObject> objs, IReadOnlyList<string> fields)
        {
            foreach (var obj in objs)
            {
                Console.WriteLine("Address {0:x8}", obj.Address);
                foreach (var field in fields)
                {
                    if (TryParseFieldPathFromFieldExpression(runtime, field, obj, out var instanceFieldPath))
                    {
                        if (TryGetFieldValueFromFieldPath(runtime, obj, instanceFieldPath, out var toPrint))
                        {
                            if (toPrint is ulong)
                            {
                                toPrint = $"{toPrint:x8}";
                            }
                            Console.WriteLine("  {0}: {1}", field, toPrint);
                        }
                        else
                        {
                            Console.WriteLine("Unable to get field value from field path {0}", field);
                        }
                    }
                    else
                    {
                        Console.WriteLine("Unable to parse select field expression {0}", field);
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
            var items = new List<ClrObject>();
            for (var i = 0; i < array.GetLength(0); i++)
            {
                var item = array.GetObjectValue(i);
                items.Add(item);
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
                .ToList();
            var type = runtime.Heap.GetTypeByMethodTable(methodTable);
            if (type == null)
            {
                return;
            }
            
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
