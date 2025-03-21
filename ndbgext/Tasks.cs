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
    public string Field { get; init; }
    public string Value { get; init; }
    public WhereOperator Operator { get; init; }
}

public class QueryExpressionParser
{
    private static readonly Dictionary<string, WhereOperator> Operators = new Dictionary<string, WhereOperator>()
    {
        { " == ", WhereOperator.Equals },
        { " >= ", WhereOperator.GreaterThanOrEqual },
        { " <= ", WhereOperator.LessThanOrEqual },
        { " > ", WhereOperator.GreaterThan },
        { " < ", WhereOperator.LessThan },
        { " != ", WhereOperator.NotEquals },
        { " =~ ", WhereOperator.Matches }
    };
    
    public bool ParseWherePredicate(string expression, out WherePredicate result)
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
    private QueryRunner _queryRunner;
    public QueryCommand(QueryRunner queryRunner, nint pUnknown, bool redirectConsoleOutput = true)
        : base(pUnknown, redirectConsoleOutput)
    {
        _queryRunner = queryRunner;
    }

    internal void Run(string args)
    {
        var argsSplit = args.Split(new char[] { ' ' }, 4);
        if (argsSplit.Length >= 4 && argsSplit[0] == "-mt" && Helper.TryParseAddress(argsSplit[1], out var methodTable))
        {
            if (argsSplit[2] == "select")
            {
                var fields = argsSplit[3].Split(',');

                foreach (var runtime in this.Runtimes)
                {
                    _queryRunner.Run(runtime, methodTable, fields);
                }
            }
            else if (argsSplit[2] == "where")
            {
                var parser = new QueryExpressionParser();
                var predicateStr = argsSplit[3];
                if (parser.ParseWherePredicate(predicateStr, out var predicate))
                {
                    foreach (var runtime in this.Runtimes)
                    {
                        _queryRunner.RunWhere(runtime, methodTable, predicate);
                    }
                }
            }
        }
    }

    public class QueryRunner
    {
        public void RunWhere(ClrRuntime runtime, ulong methodTable, WherePredicate predicate)
        {
            var heap = runtime.Heap;
            var objs = heap.EnumerateObjects().Where(o => o.Type?.MethodTable == methodTable);
            var numberOfMatches = 0;
            foreach (var obj in objs)
            {
                var matched = false;
                if (!TryFindFieldValue(predicate.Field, obj, out var foundMap))
                {
                    continue;
                }

                var typeName = foundMap.instanceField.Type?.Name;
                var fieldName = foundMap.instanceField.Name;
                IComparable fieldValue = null;
                IComparable predicateValue = null;
                if (typeName == "System.Guid")
                {
                    if (Guid.TryParse(predicate.Value, out var parsed))
                    {
                        fieldValue = foundMap.clrObject.ReadField<Guid>(fieldName);
                        predicateValue = parsed;
                    }
                }
                else if (typeName == "System.DateTime")
                {
                    if (DateTime.TryParse(predicate.Value, out var parsed))
                    {
                        fieldValue = foundMap.clrObject.ReadField<DateTime>(fieldName);
                        predicateValue = parsed;
                    }
                }
                else if (typeName == "System.DateTimeOffset")
                {
                    if (DateTimeOffset.TryParse(predicate.Value, out var parsed))
                    {
                        fieldValue = foundMap.clrObject.ReadField<DateTimeOffset>(fieldName);
                        predicateValue = parsed;
                    }
                }
                else
                {
                    switch (foundMap.instanceField.ElementType)
                    {
                        case ClrElementType.Int32:
                            if (int.TryParse(predicate.Value, out var parsedPredicateValue))
                            {
                                fieldValue = foundMap.clrObject.ReadField<Int32>(fieldName);
                                predicateValue = parsedPredicateValue;
                            }
                            break;
                        case ClrElementType.String:
                            fieldValue = foundMap.clrObject.ReadStringField(fieldName);
                            predicateValue = predicate.Value;
                            break;
                        case ClrElementType.Class:
                            if (Helper.TryParseAddress(predicate.Value, out var parsedAddress))
                            {
                                fieldValue = foundMap.clrObject.ReadObjectField(fieldName).Address;
                                predicateValue = parsedAddress;
                            }
                            break;
                    }
                }
                    
                static int CompareObjects(object left, object right)
                {
                    if (left is IComparable comparable)
                    {
                        return comparable.CompareTo(right);
                    }
                    throw new ArgumentException("The type does not implement IComparable", nameof(left));
                }
                    
                switch (predicate.Operator)
                {
                    case WhereOperator.GreaterThan:
                        if (CompareObjects(fieldValue, predicateValue) > 0)
                        {
                            matched = true;
                        }
                        break;
                    case WhereOperator.GreaterThanOrEqual:
                        if (CompareObjects(fieldValue, predicateValue) >= 0)
                        {
                            matched = true;
                        }
                        break;
                    case WhereOperator.LessThan:
                        if (CompareObjects(fieldValue, predicateValue) < 0)
                        {
                            matched = true;
                        }
                        break;
                    case WhereOperator.LessThanOrEqual:
                        if (CompareObjects(fieldValue, predicateValue) <= 0)
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
        
        public void Run(ClrRuntime runtime, ulong methodTable, IReadOnlyList<string> fields)
        {
            var heap = runtime.Heap;
            var objs = heap.EnumerateObjects().Where(o => o.Type?.MethodTable == methodTable);
            foreach (var obj in objs)
            {
                Console.WriteLine("Address {0:x8}", obj.Address);
                foreach (var field in fields)
                {
                    PrintField(field, obj);
                }
            }
        }
        
        private static bool TryFindFieldValue(string field, ClrObject obj, out (ClrInstanceField instanceField, ClrObject clrObject) result)
        {
            result = default;
            ClrObject current = obj;
            var splitFields = field.Split(".");
            for (int i = 0; i < splitFields.Length; i++)
            {
                var splitFieldObj = current.Type?.Fields.Where(f => f.Name == splitFields[i]).FirstOrDefault();
                if (splitFieldObj != null)
                {
                    if (i < splitFields.Length - 1)
                    {
                        current = current.ReadObjectField(splitFields[i]);
                    }
                    else
                    {
                        result = (splitFieldObj, current);
                        return true;
                    }
                }
            }

            return false;
        }

        private static void PrintField(string field, ClrObject obj)
        {
            if (TryFindFieldValue(field, obj, out var foundMap))
            {
                var typeName = foundMap.instanceField.Type?.Name;
                var fieldName = foundMap.instanceField.Name;
                object value = typeName switch
                {
                    "System.Guid" => foundMap.clrObject.ReadField<Guid>(fieldName),
                    "System.DateTime" => foundMap.clrObject.ReadField<DateTime>(fieldName),
                    "System.DateTimeOffset" => foundMap.clrObject.ReadField<DateTimeOffset>(fieldName),
                    _ => foundMap.instanceField.ElementType switch
                    {
                        ClrElementType.Int32 => foundMap.clrObject.ReadField<int>(fieldName),
                        ClrElementType.String => foundMap.clrObject.ReadStringField(fieldName),
                        ClrElementType.Object or ClrElementType.Class => foundMap.clrObject.ReadObjectField(fieldName).Address.ToString("x8"),
                        _ => "Unsupported field type"
                    }
                };

                Console.WriteLine("  {0}: {1}", field, value);
            }
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