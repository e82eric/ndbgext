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
    Contains,
    NotEquals
}

public class WherePredicate
{
    public string Field { get; init; }
    public string Value { get; init; }
    public WhereOperator Operator { get; init; }
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
        var argsSplit = args.Split(' ');
        if (argsSplit.Length == 4 && argsSplit[0] == "-mt" && Helper.TryParseAddress(argsSplit[1], out var methodTable))
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
                var predicate = argsSplit[3];
                WherePredicate parsedPredicate = null;
                if (predicate.Contains("|==|"))
                {
                    var splitPredicate = predicate.Split("|==|");
                    if (splitPredicate.Length == 2)
                    {
                        parsedPredicate = new WherePredicate
                        {
                            Field = splitPredicate[0], Value = splitPredicate[1], Operator = WhereOperator.Equals
                        };
                    }
                }
                else if (predicate.Contains("|>=|"))
                {
                    var splitPredicate = predicate.Split("|>=|");
                    if (splitPredicate.Length == 2)
                    {
                        parsedPredicate = new WherePredicate
                        {
                            Field = splitPredicate[0], Value = splitPredicate[1], Operator = WhereOperator.GreaterThanOrEqual
                        };
                    }
                }
                else if (predicate.Contains("|<=|"))
                {
                    var splitPredicate = predicate.Split("|<=|");
                    if (splitPredicate.Length == 2)
                    {
                        parsedPredicate = new WherePredicate
                        {
                            Field = splitPredicate[0], Value = splitPredicate[1], Operator = WhereOperator.LessThanOrEqual
                        };
                    }
                }
                else if (predicate.Contains("|>|"))
                {
                    var splitPredicate = predicate.Split("|>|");
                    if (splitPredicate.Length == 2)
                    {
                        parsedPredicate = new WherePredicate
                        {
                            Field = splitPredicate[0], Value = splitPredicate[1], Operator = WhereOperator.GreaterThan
                        };
                    }
                }
                else if (predicate.Contains("|<|"))
                {
                    var splitPredicate = predicate.Split("|<|");
                    if (splitPredicate.Length == 2)
                    {
                        parsedPredicate = new WherePredicate
                        {
                            Field = splitPredicate[0], Value = splitPredicate[1], Operator = WhereOperator.LessThan
                        };
                    }
                }
                else if (predicate.Contains("|??|"))
                {
                    var splitPredicate = predicate.Split("|??|");
                    if (splitPredicate.Length == 2)
                    {
                        parsedPredicate = new WherePredicate
                        {
                            Field = splitPredicate[0], Value = splitPredicate[1], Operator = WhereOperator.Contains
                        };
                    }
                }
                else if (predicate.Contains("|!=|"))
                {
                    var splitPredicate = predicate.Split("|!=|");
                    if (splitPredicate.Length == 2)
                    {
                        parsedPredicate = new WherePredicate
                        {
                            Field = splitPredicate[0], Value = splitPredicate[1], Operator = WhereOperator.NotEquals
                        };
                    }
                }

                if (parsedPredicate != null)
                {
                    foreach (var runtime in this.Runtimes)
                    {
                        _queryRunner.RunWhere(runtime, methodTable, parsedPredicate);
                    
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
                var fieldObj = obj.Type?.Fields.Where(f => f.Name == predicate.Field).FirstOrDefault();
                if (fieldObj != null)
                {
                    var matched = false;
                    if (fieldObj.Type?.Name == "System.Guid")
                    {
                        if (Guid.TryParse(predicate.Value, out var parsedGuid))
                        {
                            switch (predicate.Operator)
                            {
                                case WhereOperator.Equals:
                                    if (obj.ReadField<Guid>(predicate.Field) == parsedGuid)
                                    {
                                        matched = true;
                                    }
                                    
                                    break;
                            }
                        }
                    }
                    else
                    {
                        switch (fieldObj.ElementType)
                        {
                            case ClrElementType.Int32:
                                var val = obj.ReadField<Int32>(predicate.Field);
                                if (int.TryParse(predicate.Value, out var parsedPredicateValue))
                                {
                                    switch (predicate.Operator)
                                    {
                                        case WhereOperator.Equals:
                                            if (val == parsedPredicateValue)
                                            {
                                                matched = true;
                                            }
                                            break;
                                        case WhereOperator.NotEquals:
                                            if (val != parsedPredicateValue)
                                            {
                                                matched = true;
                                            }
                                            break;
                                        case WhereOperator.GreaterThan:
                                            if (val > parsedPredicateValue)
                                            {
                                                matched = true;
                                            }
                                            break;
                                        case WhereOperator.LessThan:
                                            if (val < parsedPredicateValue)
                                            {
                                                matched = true;
                                            }
                                            break;
                                        case WhereOperator.GreaterThanOrEqual:
                                            if (val >= parsedPredicateValue)
                                            {
                                                matched = true;
                                            }
                                            break;
                                        case WhereOperator.LessThanOrEqual:
                                            if (val >= parsedPredicateValue)
                                            {
                                                matched = true;
                                            }
                                            break;
                                    }
                                }

                                break;
                            case ClrElementType.String:
                                var stringVal = obj.ReadStringField(predicate.Field);
                                switch (predicate.Operator)
                                {
                                    case WhereOperator.Equals:
                                        if (stringVal == predicate.Value)
                                        {
                                            matched = true;
                                        }
                                        break;
                                    case WhereOperator.NotEquals:
                                        if (stringVal != predicate.Value)
                                        {
                                            matched = true;
                                        }
                                        break;
                                    case WhereOperator.Contains:
                                        if (stringVal.Contains(predicate.Value))
                                        {
                                            matched = true;
                                        }
                                        break;
                                }
                                break;
                            case ClrElementType.Class:
                                if (Helper.TryParseAddress(predicate.Value, out var parsedAddress))
                                {
                                    switch (predicate.Operator)
                                    {
                                        case WhereOperator.Equals:
                                            if (obj.ReadObjectField(predicate.Field).Address == parsedAddress)
                                            {
                                                matched = true;
                                            }
                                            break;
                                        case WhereOperator.NotEquals:
                                            if (obj.ReadObjectField(predicate.Field).Address != parsedAddress)
                                            {
                                                matched = true;
                                            }
                                            break;
                                    }
                                }
                                break;
                        }
                    }

                    if (matched)
                    {
                        numberOfMatches++;
                        Console.WriteLine("Address: {0:x8}", obj.Address);
                    }
                }
            }
            Console.WriteLine("Number of matches: {0}", numberOfMatches);
        }
        
        public void Run(ClrRuntime runtime, ulong methodTable, IEnumerable<string> fields)
        {
            var heap = runtime.Heap;
            var objs = heap.EnumerateObjects().Where(o => o.Type?.MethodTable == methodTable);
            foreach (var obj in objs)
            {
                Console.WriteLine("Address {0:x8}", obj.Address);
                foreach (var field in fields)
                {
                    var fieldObj = obj.Type?.Fields.Where(f => f.Name == field).FirstOrDefault();
                    if (fieldObj != null)
                    {
                        if (fieldObj.Type?.Name == "System.Guid")
                        {
                            Console.WriteLine("  {0}: {1}", field, obj.ReadField<Guid>(field));
                        }
                        else
                        {
                            switch (fieldObj.ElementType)
                            {
                                case ClrElementType.Int32:
                                    Console.WriteLine("  {0}: {1}", field, obj.ReadField<Int32>(field));
                                    break;
                                case ClrElementType.String:
                                    Console.WriteLine("  {0}: {1}", field, obj.ReadStringField(field));
                                    break;
                                case ClrElementType.Object:
                                case ClrElementType.Class:
                                    Console.WriteLine("  {0}: {1:x8}", field, obj.ReadObjectField(field).Address);
                                    break;
                            }
                        }
                    }
                }
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