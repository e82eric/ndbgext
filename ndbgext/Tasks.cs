using System.Text.RegularExpressions;
using DbgEngExtension;
using Microsoft.Diagnostics.Runtime;

namespace ndbgext;

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
        { " =~ ", WhereOperator.Matches }
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
        var argsSplit = args.Split([' '], 4);
        if (argsSplit.Length >= 4 && (argsSplit[0] == "-mt" || argsSplit[0] == "-addr") && Helper.TryParseAddress(argsSplit[1], out var methodTableOrAddress))
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
                return false;
            }

            if (!TypeNameMap.TryGetValue(type.Name, out var handlers))
            {
                if (!ElementTypeMap.TryGetValue(type.ElementType, out handlers))
                {
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
                    return false;
                }
                
                if (!TryGetAddress(runtime, currentObject, instanceField, out var fieldAddress))
                {
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
            var fieldAddress = obj.Address + (ulong)(field.Offset + IntPtr.Size);
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

        public void RunWhere(ClrRuntime runtime, ulong methodTable, WherePredicate predicate)
        {
            IComparable? fieldValue;
            IComparable? predicateValue;
            var heap = runtime.Heap;
            var objs = heap.EnumerateObjects().Where(o => o.Type?.MethodTable == methodTable);
            var type = runtime.GetTypeByMethodTable(methodTable);
            if (type == null)
            {
                return;
            }

            ClrType? predicateFieldType;
            ClrInstanceFieldPath? instanceFieldPath = null;
            if (predicate.Field == "$this")
            {
                predicateFieldType = type;
            }
            else
            {
                if (!TryParseFieldPathFromFieldExpression(predicate.Field, type, out instanceFieldPath))
                {
                    return;
                }

                if (instanceFieldPath == null || instanceFieldPath.Value.Type == null)
                {
                    return;
                }
                
                predicateFieldType = instanceFieldPath.Value.Type;
            }

            var current = instanceFieldPath;
            while (current?.Next != null)
            {
                current = current.Next;
            }

            if (!TryParsePredicate(current.Value.Type, predicate, out predicateValue))
            {
                return;
            }
            
            var numberOfMatches = 0;
            
            foreach (var obj in objs)
            {
                var matched = false;
                if (predicate.Field == "$this")
                {
                    if (!TryGetFieldValue(runtime, predicateFieldType, obj.Address, out fieldValue))
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
                    Console.WriteLine("Address: {0:x8}", obj.Address);
                }
            }
            Console.WriteLine("Number of matches: {0}", numberOfMatches);
        }

        public void RunSelect(ClrRuntime runtime, IReadOnlyList<ClrObject> objs, ClrType type,
            IReadOnlyList<string> fields)
        {
            var instanceFieldPaths = new List<ClrInstanceFieldPath>();

            foreach (var field in fields)
            {
                if (TryParseFieldPathFromFieldExpression(field, type, out var instanceFieldPath))
                {
                    if (instanceFieldPath != null)
                    {
                        instanceFieldPaths.Add(instanceFieldPath);
                    }
                }
            }
            
            foreach (var obj in objs)
            {
                Console.WriteLine("Address {0:x8}", obj.Address);
                foreach (var field in instanceFieldPaths)
                {
                    if (TryGetFieldValueFromFieldPath(runtime, obj, field, out var toPrint))
                    {
                        if (toPrint is ulong)
                        {
                            toPrint = $"{toPrint:x8}";
                        }
                        Console.WriteLine("  {0}: {1}", field.Value.Name, toPrint);
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
            
            RunSelect(runtime, [clrObject], clrObject.Type, fields);
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
            
            RunSelect(runtime, objs, type, fields);
        }
    }
}

public class TasksCommand : DbgEngCommand
{
    private readonly Tasks _tasks;

    public TasksCommand(Tasks tasks, nint pUnknown, bool redirectConsoleOutput = true)
        : base(pUnknown, redirectConsoleOutput)
    {
        _tasks = tasks;
    }

    internal void Run(string args)
    {
        var argsSplit = args.Split(' ');
        var includeTaskDetails = argsSplit.Length > 0 && argsSplit[0].ToLower() == "-detail";
        var stateFilter = includeTaskDetails && argsSplit.Length > 1 ? argsSplit[1] : string.Empty;

        foreach (var runtime in this.Runtimes)
        {
            _tasks.Run(runtime, includeTaskDetails, stateFilter);
        }
    }
}

public class Tasks
{
    public void Run(ClrRuntime runtime, bool details, string stateFilter)
    {
        var heap = runtime.Heap;

        var result = new List<TasksItem>();
        foreach (var heapObject in heap.EnumerateObjects())
        {
            if (heapObject.Type.Name.Contains("System.Threading.Tasks.Task"))
            {
                if (heapObject.TryReadValueTypeField("m_taskId", out var _))
                {
                    if(heapObject.TryReadValueTypeField("m_stateFlags", out var _))
                    {
                        var stateFlags = heapObject.ReadField<ulong>("m_stateFlags");
                        var item = new TasksItem();
                        item.TaskName = heapObject.Type.Name;
                        item.Address = heapObject.Address;
                        item.TaskState = GetTaskState(stateFlags);
                        item.Method = Helper.GetDelegateMethod(runtime, heapObject);
                        result.Add(item);

                        if (heapObject.TryReadObjectField("m_stateObject", out var stateObject))
                        {
                            if (!stateObject.IsNull && stateObject.IsValid)
                            {
                                item.StateMachine = stateObject.Type.Name;
                            }
                        }
                        
                        if (heapObject.TryReadObjectField("m_continuationObject", out var continuationObject))
                        {
                            if (!continuationObject.IsNull && continuationObject.IsValid)
                            {
                                if (continuationObject.TryReadObjectField("StateMachine", out var stateMachine))
                                {
                                    item.ContinuationStateMachine = stateMachine.Type?.Name;
                                }
                                else if (continuationObject.TryReadObjectField("_target", out var target))
                                {
                                    if (target.TryReadObjectField("m_stateMachine", out var stateMachine2))
                                    {
                                        item.ContinuationStateMachine = stateMachine2.Type?.Name;
                                    }
                                }
                                else
                                {
                                    item.ContinuationStateMachine = continuationObject.Type.Name;
                                }
                            }
                        }
                    }
                }

            }
        }

        Console.WriteLine("---------Task State Stats--------");
        var byState = result.GroupBy(r => r.TaskState);
        foreach (var state in byState)
        {
            Console.WriteLine("{0} {1}", state.Key, state.Count());
        }

        Console.WriteLine();

        Console.WriteLine("---------Method Stats--------");
        var byMethod = result.GroupBy(r => r.Method);
        foreach (var methodItem in byMethod)
        {
            Console.WriteLine("{0} {1}", methodItem.Key, methodItem.Count());

            var byTaskState = methodItem.GroupBy(mi => mi.TaskState);
            foreach (var taskState in byTaskState)
            {
                Console.WriteLine("  {0} {1}", taskState.Key, taskState.Count());
            }
        }

        if (details)
        {
            Console.WriteLine();
            Console.WriteLine("---------Details--------");
            foreach (var item in result)
            {
                if (string.IsNullOrEmpty(stateFilter) || item.TaskState == stateFilter)
                {
                    Console.WriteLine("{0:X} {1}", item.Address, item.TaskState);
                    //Console.WriteLine("  State: {0}", item.TaskState);
                    Console.WriteLine("  Method: {0}", item.Method);
                    Console.WriteLine("  TaskName: {0}", item.TaskName);
                    Console.WriteLine("  StateMachine: {0}", item.StateMachine);
                    Console.WriteLine("  Continuation: {0}", item.ContinuationStateMachine);
                }
            }
        }
    }

    private static string GetTaskState(ulong flag)
    {
        TaskStatus result;

        if ((flag & TASK_STATE_FAULTED) != 0) result = TaskStatus.Faulted;
        else if ((flag & TASK_STATE_CANCELED) != 0) result = TaskStatus.Canceled;
        else if ((flag & TASK_STATE_RAN_TO_COMPLETION) != 0) result = TaskStatus.RanToCompletion;
        else if ((flag & TASK_STATE_WAITING_ON_CHILDREN) != 0) result = TaskStatus.WaitingForChildrenToComplete;
        else if ((flag & TASK_STATE_DELEGATE_INVOKED) != 0) result = TaskStatus.Running;
        else if ((flag & TASK_STATE_STARTED) != 0) result = TaskStatus.WaitingToRun;
        else if ((flag & TASK_STATE_WAITINGFORACTIVATION) != 0) result = TaskStatus.WaitingForActivation;
        else if (flag == 0) result = TaskStatus.Created;
        else return null;

        return result.ToString();
    }

    // from CLR implementation
    internal const int TASK_STATE_STARTED                       =      65536;
    internal const int TASK_STATE_DELEGATE_INVOKED              =     131072;
    internal const int TASK_STATE_DISPOSED                      =     262144;
    internal const int TASK_STATE_EXCEPTIONOBSERVEDBYPARENT     =     524288;
    internal const int TASK_STATE_CANCELLATIONACKNOWLEDGED      =    1048576;
    internal const int TASK_STATE_FAULTED                       =    2097152;
    internal const int TASK_STATE_CANCELED                      =    4194304;
    internal const int TASK_STATE_WAITING_ON_CHILDREN           =    8388608;
    internal const int TASK_STATE_RAN_TO_COMPLETION             =   16777216;
    internal const int TASK_STATE_WAITINGFORACTIVATION          =   33554432;
    internal const int TASK_STATE_COMPLETION_RESERVED           =   67108864;
    internal const int TASK_STATE_THREAD_WAS_ABORTED            =  134217728;
    internal const int TASK_STATE_WAIT_COMPLETION_NOTIFICATION  =  268435456;
    internal const int TASK_STATE_EXECUTIONCONTEXT_IS_NULL      =  536870912;
    internal const int TASK_STATE_TASKSCHEDULED_WAS_FIRED       = 1073741824;

}

class TasksItem
{
    public ulong Address { get; set; }
    public string TaskName { get; set; }
    public string TaskState { get; set; }
    public string Method { get; set; }
    public string? ContinuationStateMachine { get; set; }
    public string? StateMachine { get; set; }
}