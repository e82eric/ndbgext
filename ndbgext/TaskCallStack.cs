using System.Collections;
using System.Diagnostics;
using System.Text;
using DbgEngExtension;
using ICSharpCode.Decompiler.Util;
using Microsoft.Diagnostics.Runtime;
using Microsoft.Diagnostics.Runtime.Interfaces;
using Microsoft.Extensions.Primitives;

namespace ndbgext;

public class TaskCallStackCommand : DbgEngCommand
{
    private readonly DumpAsyncCommand _tasks;

    public TaskCallStackCommand(DumpAsyncCommand tasks, nint pUnknown, bool redirectConsoleOutput = true)
        : base(pUnknown, redirectConsoleOutput)
    {
        _tasks = tasks;
    }
    
    internal void Run(string args)
    {
        var netCoreDumpAsync = new NetCoreDumpAsyncCommand();
        var argsSplit = args.Split(' ');
        if (argsSplit.Length == 1)
        {
            if (Helper.IsNetCore(Runtimes.First()))
            {
                netCoreDumpAsync.Runtime = Runtimes.First();
                netCoreDumpAsync.Invoke(); 
            }
            else
            {
               _tasks.Execute(Runtimes);    
            }
        }
    }
}

public class DumpAsyncCommand
{
    public void Execute(IList<ClrRuntime> runtimes)
    {
        // There can be multiple CLR runtimes loaded into
        foreach (ClrRuntime runtime in runtimes)
        {
            ClrHeap heap = runtime.Heap;

            var allStateMachines = new List<AsyncStateMachine>();
            var knownStateMachines = new Dictionary<ulong, AsyncStateMachine>();

            GetAllStateMachines(heap, allStateMachines, knownStateMachines);

            ChainStateMachinesBasedOnTaskContinuations(knownStateMachines);
            ChainStateMachinesBasedOnJointableTasks(allStateMachines);
            MarkThreadingBlockTasks(allStateMachines, runtimes);
            MarkUIThreadDependingTasks(allStateMachines);
            FixBrokenDependencies(allStateMachines);
            PrintOutStateMachines(allStateMachines);
            
            IOrderedEnumerable<AsyncStateMachine> rootStateMachines = allStateMachines
                .Where(m => m.Depth > 0)
                .OrderByDescending(m => m.Depth)
                .ThenByDescending(m => m.SwitchToMainThreadTask.Address);
            
            var grouped = rootStateMachines.GroupBy(sm => sm.PrintCallStack());
            var sorted = grouped.Select(g => new
            {
                LogicalCallContext = g.Key,
                Count = g.Count(),
                TaskAddresses = string.Join(' ', g.Select(i => i.Task.Address.ToString("x16")))
            }).ToList();
            sorted.SortBy(s => s.Count);
            
            Console.WriteLine();
            foreach (var group in sorted)
            {
                Console.WriteLine(group.LogicalCallContext);
                Console.WriteLine("Number of logicl call stacks: {0}", group.Count);
                Console.WriteLine($"Tasks: {group.TaskAddresses}");
                Console.WriteLine("--------");
                Console.WriteLine("");
            }
        }
    }

    private static void GetAllStateMachines(ClrHeap heap, List<AsyncStateMachine> allStateMachines,
        Dictionary<ulong, AsyncStateMachine> knownStateMachines)
    {
        foreach (ClrObject obj in Utilities.GetObjectsOfType(heap,
                     "System.Runtime.CompilerServices.AsyncMethodBuilderCore+MoveNextRunner"))
        {
            try
            {
                ClrObject stateMachine = obj.ReadObjectField("m_stateMachine");
                if (!knownStateMachines.ContainsKey(stateMachine.Address))
                {
                    try
                    {
                        var state = stateMachine.ReadField<int>("<>1__state");
                        if (state >= -1)
                        {
                            ClrObject taskField = default(ClrObject);
                            ClrValueType? asyncBuilder = stateMachine.TryGetValueClassField("<>t__builder");
                            if (asyncBuilder.HasValue)
                            {
                                while (asyncBuilder.HasValue)
                                {
                                    taskField = asyncBuilder.TryGetObjectField("m_task");
                                    if (!taskField.IsNull)
                                    {
                                        break;
                                    }

                                    ClrValueType? nextAsyncBuilder = asyncBuilder.TryGetValueClassField("m_builder");
                                    if (nextAsyncBuilder is null)
                                    {
                                        asyncBuilder = asyncBuilder.TryGetValueClassField("_methodBuilder");
                                    }
                                    else
                                    {
                                        asyncBuilder = nextAsyncBuilder;
                                    }
                                }
                            }
                            else
                            {
                                // CLR debugger may not be able to access t__builder, when NGEN assemblies are being used, and the type of the field could be lost.
                                // Our workaround is to pick up the first Task object referenced by the state machine, which seems to be correct.
                                // That function works with the raw data structure (like how GC scans the object, so it doesn't depend on symbols.
                                //
                                // However, one problem of that is we can pick up tasks from other reference fields of the same structure. So, we go through fields which we have symbols
                                // and remember references encounted, and we skip them when we go through GC references.
                                // Note: we can do better by going through other value structures, and extract references from them here, which we can consider when we have a real scenario.
                                var previousReferences = new Dictionary<ulong, int>();
                                if (stateMachine.Type?.GetFieldByName("<>t__builder") is not null)
                                {
                                    foreach (ClrInstanceField field in stateMachine.Type.Fields)
                                    {
                                        if (string.Equals(field.Name, "<>t__builder", StringComparison.Ordinal))
                                        {
                                            break;
                                        }

                                        if (field.IsObjectReference)
                                        {
                                            ClrObject referencedValue =
                                                field.ReadObject(stateMachine.Address, interior: false);
                                            if (!referencedValue.IsNull)
                                            {
                                                if (previousReferences.TryGetValue(referencedValue.Address,
                                                        out int refCount))
                                                {
                                                    previousReferences[referencedValue.Address] = refCount + 1;
                                                }
                                                else
                                                {
                                                    previousReferences[referencedValue.Address] = 1;
                                                }
                                            }
                                        }
                                    }
                                }

                                foreach (ClrObject referencedObject in stateMachine.EnumerateReferences(true))
                                {
                                    if (!referencedObject.IsNull)
                                    {
                                        if (previousReferences.TryGetValue(referencedObject.Address,
                                                out int refCount) && refCount > 0)
                                        {
                                            if (refCount == 1)
                                            {
                                                previousReferences.Remove(referencedObject.Address);
                                            }
                                            else
                                            {
                                                previousReferences[referencedObject.Address] = refCount - 1;
                                            }

                                            continue;
                                        }
                                        else if (previousReferences.Count > 0)
                                        {
                                            continue;
                                        }

                                        if (referencedObject.Type is object &&
                                            (string.Equals(referencedObject.Type.Name, "System.Threading.Tasks.Task",
                                                 StringComparison.Ordinal) ||
                                             string.Equals(referencedObject.Type.BaseType?.Name,
                                                 "System.Threading.Tasks.Task", StringComparison.Ordinal)))
                                        {
                                            taskField = referencedObject;
                                            break;
                                        }
                                    }
                                }
                            }

                            var asyncState = new AsyncStateMachine(state, stateMachine, taskField);
                            allStateMachines.Add(asyncState);
                            knownStateMachines.Add(stateMachine.Address, asyncState);

                            if (stateMachine.Type is object)
                            {
                                foreach (ClrMethod? method in stateMachine.Type.Methods)
                                {
                                    if (method.Name == "MoveNext" && method.NativeCode != ulong.MaxValue)
                                    {
                                        asyncState.CodeAddress = method.NativeCode;
                                    }
                                }
                            }
                        }
                    }
#pragma warning disable CA1031 // Do not catch general exception types
                    catch (Exception ex)
#pragma warning restore CA1031 // Do not catch general exception types
                    {
                        Console.WriteLine(
                            $"Fail to process state machine {stateMachine.Address:x} Type:'{stateMachine.Type?.Name}' Module:'{stateMachine.Type?.Module?.Name}' Error: {ex.Message}");
                    }
                }
            }
#pragma warning disable CA1031 // Do not catch general exception types
            catch (Exception ex)
#pragma warning restore CA1031 // Do not catch general exception types
            {
                Console.WriteLine($"Fail to process AsyncStateMachine Runner {obj.Address:x} Error: {ex.Message}");
            }
        }
    }

    private static void ChainStateMachinesBasedOnTaskContinuations(
        Dictionary<ulong, AsyncStateMachine> knownStateMachines)
    {
        foreach (AsyncStateMachine? stateMachine in knownStateMachines.Values)
        {
            ClrObject taskObject = stateMachine.Task;
            try
            {
                while (!taskObject.IsNull)
                {
                    // 3 cases in order to get the _target:
                    // 1. m_continuationObject.m_action._target
                    // 2. m_continuationObject._target
                    // 3. m_continuationObject.m_task.m_stateObject._target
                    ClrObject continuationObject = taskObject.TryGetObjectField("m_continuationObject");
                    if (continuationObject.IsNull)
                    {
                        break;
                    }

                    ChainStateMachineBasedOnTaskContinuations(knownStateMachines, stateMachine, continuationObject);

                    taskObject = continuationObject;
                }
            }
#pragma warning disable CA1031 // Do not catch general exception types
            catch (Exception ex)
#pragma warning restore CA1031 // Do not catch general exception types
            {
                Console.WriteLine(
                    $"Fail to fix continuation of state {stateMachine.StateMachine.Address:x} Error: {ex.Message}");
            }
        }
    }

    private static void ChainStateMachineBasedOnTaskContinuations(
        Dictionary<ulong, AsyncStateMachine> knownStateMachines, AsyncStateMachine stateMachine,
        ClrObject continuationObject)
    {
        ClrObject continuationAction = continuationObject.TryGetObjectField("m_action");

        // case 1
        ClrObject continuationTarget = continuationAction.TryGetObjectField("_target");
        if (continuationTarget.IsNull)
        {
            // case 2
            continuationTarget = continuationObject.TryGetObjectField("_target");
            if (continuationTarget.IsNull)
            {
                // case 3
                continuationTarget = continuationObject.TryGetObjectField("m_task").TryGetObjectField("m_stateObject")
                    .TryGetObjectField("_target");
            }
        }

        while (!continuationTarget.IsNull)
        {
            // now get the continuation from the target
            ClrObject continuationTargetStateMachine = continuationTarget.TryGetObjectField("m_stateMachine");
            if (!continuationTargetStateMachine.IsNull)
            {
                if (knownStateMachines.TryGetValue(continuationTargetStateMachine.Address,
                        out AsyncStateMachine? targetAsyncState) && targetAsyncState != stateMachine)
                {
                    stateMachine.Next = targetAsyncState;
                    stateMachine.DependentCount++;
                    targetAsyncState.Previous = stateMachine;
                }

                break;
            }
            else
            {
                ClrObject nextContinuation = continuationTarget.TryGetObjectField("m_continuation");
                continuationTarget = nextContinuation.TryGetObjectField("_target");
            }
        }

        ClrObject items = continuationObject.TryGetObjectField("_items");
        if (!items.IsNull && items.IsArray && items.ContainsPointers)
        {
            foreach (ClrObject promise in items.EnumerateReferences(true))
            {
                if (!promise.IsNull)
                {
                    ClrObject innerContinuationObject = promise.TryGetObjectField("m_continuationObject");
                    if (!innerContinuationObject.IsNull)
                    {
                        ChainStateMachineBasedOnTaskContinuations(knownStateMachines, stateMachine,
                            innerContinuationObject);
                    }
                    else
                    {
                        ChainStateMachineBasedOnTaskContinuations(knownStateMachines, stateMachine, promise);
                    }
                }
            }
        }
    }

    private static void ChainStateMachinesBasedOnJointableTasks(List<AsyncStateMachine> allStateMachines)
    {
        foreach (AsyncStateMachine? stateMachine in allStateMachines)
        {
            if (stateMachine.Previous is null)
            {
                try
                {
                    ClrObject joinableTask = stateMachine.StateMachine.TryGetObjectField("<>4__this");
                    ClrObject wrappedTask = joinableTask.TryGetObjectField("wrappedTask");
                    if (!wrappedTask.IsNull)
                    {
                        AsyncStateMachine? previousStateMachine = allStateMachines
                            .FirstOrDefault(s => s.Task.Address == wrappedTask.Address);
                        if (previousStateMachine is object && stateMachine != previousStateMachine)
                        {
                            stateMachine.Previous = previousStateMachine;
                            previousStateMachine.Next = stateMachine;
                            previousStateMachine.DependentCount++;
                        }
                    }
                }
#pragma warning disable CA1031 // Do not catch general exception types
                catch (Exception ex)
#pragma warning restore CA1031 // Do not catch general exception types
                {
                    Console.WriteLine(
                        $"Fail to fix continuation of state {stateMachine.StateMachine.Address:x} Error: {ex.Message}");
                }
            }
        }
    }

    private static void MarkUIThreadDependingTasks(List<AsyncStateMachine> allStateMachines)
    {
        foreach (AsyncStateMachine? stateMachine in allStateMachines)
        {
            if (stateMachine.Previous is null && stateMachine.State >= 0)
            {
                try
                {
                    ClrInstanceField? awaitField =
                        stateMachine.StateMachine.Type?.GetFieldByName($"<>u__{stateMachine.State + 1}");
                    if (awaitField is object && awaitField.IsValueType && string.Equals(awaitField.Type?.Name,
                            "Microsoft.VisualStudio.Threading.JoinableTaskFactory+MainThreadAwaiter",
                            StringComparison.Ordinal))
                    {
                        ClrValueType? awaitObject =
                            stateMachine.StateMachine.TryGetValueClassField($"<>u__{stateMachine.State + 1}");
                        if (awaitObject.HasValue)
                        {
                            stateMachine.SwitchToMainThreadTask = awaitObject.TryGetObjectField("job");
                        }
                    }
                }
#pragma warning disable CA1031 // Do not catch general exception types
                catch (Exception)
#pragma warning restore CA1031 // Do not catch general exception types
                {
                }
            }
        }
    }

    private static void FixBrokenDependencies(List<AsyncStateMachine> allStateMachines)
    {
        foreach (AsyncStateMachine? stateMachine in allStateMachines)
        {
            if (stateMachine.Previous is object && stateMachine.Previous.Next != stateMachine)
            {
                // If the previous task actually has two continuations, we end up in a one way dependencies chain, we need fix it in the future.
                stateMachine.AlterPrevious = stateMachine.Previous;
                stateMachine.Previous = null;
            }
        }
    }

    private void MarkThreadingBlockTasks(List<AsyncStateMachine> allStateMachines, IList<ClrRuntime> runtimes)
    {
        foreach (ClrRuntime runtime in runtimes)
        {
            foreach (ClrThread? thread in runtime.Threads)
            {
                ClrStackFrame? stackFrame = thread.EnumerateStackTrace().Take(50).FirstOrDefault(
                    f => f.Method is { } method
                         && string.Equals(f.Method.Name, "CompleteOnCurrentThread", StringComparison.Ordinal)
                         && string.Equals(f.Method.Type?.Name, "Microsoft.VisualStudio.Threading.JoinableTask",
                             StringComparison.Ordinal));

                if (stackFrame is object)
                {
                    var visitedObjects = new HashSet<ulong>();
                    foreach (ClrStackRoot stackRoot in thread.EnumerateStackRoots())
                    {
                        ClrObject stackObject = stackRoot.Object;
                        if (string.Equals(stackObject.Type?.Name, "Microsoft.VisualStudio.Threading.JoinableTask",
                                StringComparison.Ordinal) ||
                            string.Equals(stackObject.Type?.BaseType?.Name,
                                "Microsoft.VisualStudio.Threading.JoinableTask", StringComparison.Ordinal))
                        {
                            if (visitedObjects.Add(stackObject.Address))
                            {
                                ClrObject joinableTaskObject = stackObject.Type is null
                                    ? runtime.Heap.GetObject(stackObject.Address)
                                    : runtime.Heap.GetObject(stackObject.Address, stackObject.Type);
                                int state = joinableTaskObject.ReadField<int>("state");
                                if ((state & 0x10) == 0x10)
                                {
                                    // This flag indicates the JTF is blocking the thread
                                    ClrObject wrappedTask = joinableTaskObject.TryGetObjectField("wrappedTask");
                                    if (!wrappedTask.IsNull)
                                    {
                                        AsyncStateMachine? blockingStateMachine = allStateMachines
                                            .FirstOrDefault(s => s.Task.Address == wrappedTask.Address);
                                        if (blockingStateMachine is object)
                                        {
                                            blockingStateMachine.BlockedThread = thread.OSThreadId;
                                            blockingStateMachine.BlockedJoinableTask = joinableTaskObject;
                                        }
                                    }

                                    break;
                                }
                            }
                        }
                    }
                }
            }
        }
    }

    private void PrintOutStateMachines(List<AsyncStateMachine> allStateMachines)
    {
        int loopMark = -1;
        foreach (AsyncStateMachine? stateMachine in allStateMachines)
        {
            int depth = 0;
            if (stateMachine.Previous is null)
            {
                AsyncStateMachine? p = stateMachine;
                while (p is object)
                {
                    depth++;
                    if (p.Depth == loopMark)
                    {
                        break;
                    }

                    p.Depth = loopMark;
                    p = p.Next;
                }
            }

            if (stateMachine.AlterPrevious is object)
            {
                depth++;
            }

            stateMachine.Depth = depth;
            loopMark--;
        }
    }

    protected void WriteString(string message)
    {
        Console.WriteLine(message);
    }

    protected void WriteLine(string message)
    {
        this.WriteString(message + "\n");
    }

    protected void WriteObjectAddress(ulong address)
    {
        Console.WriteLine(address.ToString("x"));
    }

    protected void WriteThreadLink(uint threadId)
    {
        Console.WriteLine($"Thread TID:[{threadId:x}]");
    }

    protected void WriteMethodInfo(string name, ulong address)
    {
        Console.WriteLine(name);
    }

    protected void WriteStringWithLink(string message, string linkCommand)
    {
        Console.WriteLine(message);
    }

    private class AsyncStateMachine
    {
        public AsyncStateMachine(int state, ClrObject stateMachine, ClrObject task)
        {
            this.State = state;
            this.StateMachine = stateMachine;
            this.Task = task;
        }

        public int State { get; } // -1 == currently running, 0 = still waiting on first await, 2= before the 3rd await

        public ClrObject StateMachine { get; }

        public ClrObject Task { get; }

        public AsyncStateMachine? Previous { get; set; }

        public AsyncStateMachine? Next { get; set; }

        public int DependentCount { get; set; }

        public int Depth { get; set; }

        public uint? BlockedThread { get; set; }

        public ClrObject BlockedJoinableTask { get; set; }

        public ClrObject SwitchToMainThreadTask { get; set; }

        public AsyncStateMachine? AlterPrevious { get; set; }

        public ulong CodeAddress { get; set; }

        public override string ToString()
        {
            return $"state = {this.State} Depth {this.Depth} StateMachine = {this.StateMachine} Task = {this.Task}";
        }

        public string PrintCallStack()
        {
            var toConcat = new List<string>();
            toConcat.Add($"{"MethodTable", -12} {"InsPtr", -12} State Name");
            var current = this;
            while (current != null)
            {
                if (current.StateMachine.Type != null)
                {
                    var state = current.StateMachine.ReadField<Int32>("<>1__state");

                    toConcat.Add(
                        current.StateMachine.Type.MethodTable.ToString("x8") +
                        " " + current.CodeAddress.ToString("x8") +
                        " " + $"{state, 5}" +
                        " " + current.StateMachine.Type.Name);
                }

                current = current.Next;
            } 
            
            var result = String.Join('\n', toConcat);
            return result;
        }
    }
}
internal static class Utilities
{
    internal static ClrObject TryGetObjectField(this ClrObject clrObject, string fieldName)
    {
        if (!clrObject.IsNull)
        {
            ClrInstanceField? field = clrObject.Type?.GetFieldByName(fieldName);
            if (field is object && field.IsObjectReference)
            {
                return field.ReadObject(clrObject.Address, interior: false);
            }
        }

        return default(ClrObject);
    }

    internal static ClrValueType? TryGetValueClassField(this ClrObject clrObject, string fieldName)
    {
        if (!clrObject.IsNull)
        {
            ClrInstanceField? field = clrObject.Type?.GetFieldByName(fieldName);
            if (field?.Type is object && field.Type.IsValueType)
            {
                // System.Console.WriteLine("{0} {1:x} Field {2} {3} {4} {5}", clrObject.Type.Name, clrObject.Address, fieldName, field.Type.Name, field.Type.IsValueType, field.Type.IsRuntimeType);
                return clrObject.ReadValueTypeField(fieldName);
            }
        }

        return null;
    }

    internal static ClrObject TryGetObjectField(this ClrValueType? clrObject, string fieldName)
    {
        if (clrObject is object)
        {
            ClrInstanceField? field = clrObject.Value.Type?.GetFieldByName(fieldName);
            if (field is object && field.IsObjectReference)
            {
                return clrObject.Value.ReadObjectField(fieldName);
            }
        }

        return default(ClrObject);
    }

    internal static ClrValueType? TryGetValueClassField(this ClrValueType? clrObject, string fieldName)
    {
        if (clrObject.HasValue)
        {
            ClrInstanceField? field = clrObject.Value.Type?.GetFieldByName(fieldName);
            if (field is object && field.IsValueType)
            {
                return clrObject.Value.ReadValueTypeField(fieldName);
            }
        }

        return null;
    }

    internal static IEnumerable<ClrObject> GetObjectsOfType(ClrHeap heap, string typeName)
    {
        return heap.EnumerateObjects()
            .Where(obj => string.Equals(obj.Type?.Name, typeName, StringComparison.Ordinal));
    }
    
    public static ClrObject GetObject(this ClrHeap _, ulong objRef, ClrType type)
    {
        if (type is null)
            throw new ArgumentNullException(nameof(type));

        return new(objRef, type);
    }
}
public sealed class NetCoreDumpAsyncCommand
{
    /// <summary>The name of the command.</summary>
    private const string CommandName = "dumpasync";

    /// <summary>Indent width.</summary>
    private const int TabWidth = 2;

    /// <summary>The command invocation syntax when used in Debugger Markup Language (DML) commands.</summary>
    private const string DmlCommandInvoke = $"!{CommandName}";

    /// <summary>Gets whether to only show stacks that include the object with the specified address.</summary>
    public string? ObjectAddress
    {
        get => _objectAddress?.ToString();
        //set => _objectAddress = ParseAddress(value);
    }
    private ulong? _objectAddress;

    /// <summary>Gets whether to only show stacks that include objects with the specified method table.</summary>
    public string? MethodTableAddress
    {
        get => _methodTableAddress?.ToString();
        //set => _methodTableAddress = ParseAddress(value);
    }
    private ulong? _methodTableAddress;

    /// <summary>Gets whether to only show stacks that include objects whose type includes the specified name in its name.</summary>
    public string? NameSubstring { get; set; }

    /// <summary>Gets whether to include stacks that contain only non-state machine task objects.</summary>
    public bool IncludeTasks { get; set; }

    /// <summary>Gets whether to include completed tasks in stacks.</summary>
    public bool IncludeCompleted { get; set; }

    /// <summary>Gets whether to show state machine fields for every async stack frame that has them.</summary>
    public bool DisplayFields { get; set; }

    /// <summary>Gets whether to summarize all async frames found rather than showing detailed stacks.</summary>
    public bool Summarize { get; set; }

    /// <summary>Gets whether to coalesce stacks and portions of stacks that are the same.</summary>
    public bool CoalesceStacks { get; set; }

    public ClrRuntime Runtime;
        
    private void Write(string val)
    {
        Console.Write(val);
    }

    private void WriteLine(string line)
    {
        Console.WriteLine(line);
    }

    /// <summary>Invokes the command.</summary>
    public void Invoke()
    {
        ClrRuntime runtime = Runtime;
        ClrHeap heap = runtime.Heap;
        if (!heap.CanWalkHeap)
        {
        }

        ClrType? taskType = runtime.BaseClassLibrary.GetTypeByName("System.Threading.Tasks.Task");
        if (taskType is null)
        {
        }

        ClrStaticField? taskCompletionSentinelType = taskType.GetStaticFieldByName("s_taskCompletionSentinel");

        ClrObject taskCompletionSentinel = default;

        if (taskCompletionSentinelType is not null)
        {
            Debug.Assert(taskCompletionSentinelType.IsObjectReference);
            taskCompletionSentinel = taskCompletionSentinelType.ReadObject(runtime.BaseClassLibrary.AppDomain);
        }

        // Enumerate the heap, gathering up all relevant async-related objects.
        Dictionary<ClrObject, AsyncObject> objects = CollectObjects();

        // Render the data according to the options specified.
        if (Summarize)
        {
            RenderStats();
        }
        else if (CoalesceStacks)
        {
            RenderCoalescedStacks();
        }
        else
        {
            var stacks = RenderStacks();
            var grouped = stacks.GroupBy(sm => sm.printedCallStack);
            var sorted = grouped.Select(g => new
            {
                LogicalCallStack = g.Key,
                Count = g.Count(),
                TaskAddresses = string.Join(' ', g.Select(i => i.top.Object.Address.ToString("X")))
            }).ToList();
            sorted.SortBy(s => s.Count);
            
            foreach (var group in sorted)
            {
                Console.WriteLine(group.LogicalCallStack);
                Console.WriteLine("Number of logical call stacks: {0}", group.Count);
                Console.WriteLine($"Tasks: {group.TaskAddresses}");
                Console.WriteLine("--------");
                Console.WriteLine("");
            }
        }
        return;

        // <summary>Group frames and summarize how many of each occurred.</summary>
        void RenderStats()
        {
            // Enumerate all of the "frames", and create a mapping from a rendering of that
            // frame to its associated type and how many times that frame occurs.
            Dictionary<string, (ClrType Type, int Count)> typeCounts = new();
            foreach (KeyValuePair<ClrObject, AsyncObject> pair in objects)
            {
                ClrObject obj = pair.Key;
                if (obj.Type is null)
                {
                    continue;
                }

                string description = Describe(obj);

                if (!typeCounts.TryGetValue(description, out (ClrType Type, int Count) value))
                {
                    value = (obj.Type, 0);
                }

                value.Count++;
                typeCounts[description] = value;
            }

            // Render one line per frame.
            WriteHeaderLine($"{"MT",-16} {"Count",-8} Type");
            foreach (KeyValuePair<string, (ClrType Type, int Count)> entry in typeCounts.OrderByDescending(e => e.Value.Count))
            {
                WriteMethodTable(entry.Value.Type.MethodTable, asyncObject: true);
                WriteLine($" {entry.Value.Count,-8:N0} {entry.Key} {entry.Value.Type.MethodTable,16:x8}");
            }
        }

        // <summary>Group stacks at each frame in order to render a tree of coalesced stacks.</summary>
        void RenderCoalescedStacks()
        {
            // Find all stacks to include.
            List<ClrObject> startingList = new();
            foreach (KeyValuePair<ClrObject, AsyncObject> entry in objects)
            {
                //Console.CancellationToken.ThrowIfCancellationRequested();

                AsyncObject obj = entry.Value;
                if (obj.TopLevel && ShouldIncludeStack(obj))
                {
                    startingList.Add(entry.Key);
                }
            }

            // If we found any, render them.
            if (startingList.Count > 0)
            {
                RenderLevel(startingList, 0);
            }

            // <summary>Renders the next level of frames for coalesced stacks.</summary>
            void RenderLevel(List<ClrObject> frames, int depth)
            {
                //Console.CancellationToken.ThrowIfCancellationRequested();
                List<ClrObject> nextLevel = new();

                // Grouping function.  We want to treat all objects that render the same as the same entity.
                // For async state machines, we include the await state, both because we want it to render
                // and because we want to see state machines at different positions as part of different groups.
                Func<ClrObject, string> groupBy = o => {
                    string description = Describe(o);
                    if (objects.TryGetValue(o, out AsyncObject asyncObject) && asyncObject.IsStateMachine)
                    {
                        description = $"({asyncObject.AwaitState}) {description}";
                    }
                    return description;
                };

                // Group all of the frames, rendering each group as a single line with a count.
                // Then recur for each.
                int stackId = 1;
                foreach (IGrouping<string, ClrObject> group in frames.GroupBy(groupBy).OrderByDescending(g => g.Count()))
                {
                    int count = group.Count();
                    Debug.Assert(count > 0);

                    // For top-level frames, write out a header.
                    if (depth == 0)
                    {
                        WriteHeaderLine($"STACKS {stackId++}");
                    }

                    // Write out the count and frame.
                    Write($"{Tabs(depth)}[{count}] ");
                    WriteMethodTable(group.First().Type?.MethodTable ?? 0, asyncObject: true);
                    WriteLine($" {group.Key}");

                    // Gather up all of the next level of frames.
                    nextLevel.Clear();
                    foreach (ClrObject next in group)
                    {
                        if (objects.TryGetValue(next, out AsyncObject asyncObject))
                        {
                            // Note that the merging of multiple continuations can lead to numbers increasing at a particular
                            // level of the coalesced stacks.  It's not clear there's a better answer.
                            nextLevel.AddRange(asyncObject.Continuations);
                        }
                    }

                    // If we found any, recur.
                    if (nextLevel.Count != 0)
                    {
                        RenderLevel(nextLevel, depth + 1);
                    }

                    if (depth == 0)
                    {
                        WriteLine("");
                    }
                }
            }
        }

        // <summary>Render each stack of frames.</summary>
        IList<(AsyncObject top, string printedCallStack)> RenderStacks()
        {
            Stack<(AsyncObject AsyncObject, int Depth)> stack = new();

            // Find every top-level object (ones that nothing else has as a continuation) and output
            // a stack starting from each.
            int stackId = 1;
            var result = new List<(AsyncObject, string)>();
            foreach (KeyValuePair<ClrObject, AsyncObject> entry in objects)
            {
                //Console.CancellationToken.ThrowIfCancellationRequested();
                AsyncObject top = entry.Value;
                if (!top.TopLevel || !ShouldIncludeStack(top))
                {
                    continue;
                }

                int depth = 0;

                var sb = new StringBuilder();
                //sb.Append($"STACK {stackId++}\n");

                // If the top-level frame is an async method that's paused at an await, it must be waiting on
                // something.  Try to synthesize a frame to represent that thing, just to provide a little more information.
                if (top.IsStateMachine && top.AwaitState >= 0 && !IsCompleted(top.TaskStateFlags) &&
                    top.StateMachine is IClrValue stateMachine &&
                    stateMachine.Type is not null)
                {
                    // Short of parsing the method's IL, we don't have a perfect way to know which awaiter field
                    // corresponds to the current await state, as awaiter fields are shared across all awaits that
                    // use the same awaiter type.  We instead employ a heuristic.  If the await state is 0, the
                    // associated field will be the first one (<>u__1); even if other awaits share it, it's fine
                    // to use.  Similarly, if there's only one awaiter field, we know that must be the one being
                    // used.  In all other situations, we can't know which of the multiple awaiter fields maps
                    // to the await state, so we instead employ a heuristic of looking for one that's non-zero.
                    // The C# compiler zero's out awaiter fields when it's done with them, so if we find an awaiter
                    // field with any non-zero bytes, it must be the one in use.  This can have false negatives,
                    // as it's perfectly valid for an awaiter to be all zero bytes, but it's better than nothing.

                    // if the name is null, we have to assume it's an awaiter

                    Func<IClrInstanceField, bool> hasOneAwaiterField = static f => {
                        return f.Name is null
                               || f.Name.StartsWith("<>u__", StringComparison.Ordinal);
                    };

                    if ((top.AwaitState == 0)
                        || stateMachine.Type.Fields.Count(hasOneAwaiterField) == 1)
                    {
                        if (stateMachine.Type.GetFieldByName("<>u__1") is ClrInstanceField field &&
                            TrySynthesizeAwaiterFrame(field))
                        {
                            depth++;
                        }
                    }
                    else
                    {
                        foreach (ClrInstanceField field in stateMachine.Type.Fields)
                        {
                            // Look for awaiter fields.  This is the naming convention employed by the C# compiler.
                            if (field.Name?.StartsWith("<>u__") == true)
                            {
                                if (field.IsObjectReference)
                                {
                                    if (stateMachine.ReadObjectField(field.Name) is ClrObject { IsNull: false } awaiter)
                                    {
                                        if (TrySynthesizeAwaiterFrame(field))
                                        {
                                            depth++;
                                        }
                                        break;
                                    }
                                }
                                else if (field.IsValueType &&
                                         stateMachine.ReadValueTypeField(field.Name) is ClrValueType { IsValid: true } awaiter &&
                                         awaiter.Type is not null)
                                {
                                    byte[] awaiterBytes = new byte[awaiter.Type.StaticSize - (runtime.DataTarget!.DataReader.PointerSize * 2)];
                                    if (runtime.DataTarget!.DataReader.Read(awaiter.Address, awaiterBytes) == awaiterBytes.Length && !AllZero(awaiterBytes))
                                    {
                                        if (TrySynthesizeAwaiterFrame(field))
                                        {
                                            depth++;
                                        }
                                        break;
                                    }
                                }
                            }
                        }
                    }

                    // <summary>Writes out a frame for the specified awaiter field, if possible.</summary>
                    bool TrySynthesizeAwaiterFrame(ClrInstanceField field)
                    {
                        if (field?.Name is string name)
                        {
                            if (field.IsObjectReference)
                            {
                                IClrValue awaiter = stateMachine.ReadObjectField(name);
                                if (awaiter.Type is not null)
                                {
                                    Write("<< Awaiting: ");
                                    WriteAddress(awaiter.Address, asyncObject: false);
                                    Write(" ");
                                    WriteMethodTable(awaiter.Type.MethodTable, asyncObject: false);
                                    Write(awaiter.Type.Name);
                                    WriteLine(" >>");
                                    return true;
                                }
                            }
                            else if (field.IsValueType)
                            {
                                IClrValue awaiter = stateMachine.ReadValueTypeField(name);
                                if (awaiter.Type is not null)
                                {
                                    Write("<< Awaiting: ");
                                    WriteValueTypeAddress(awaiter.Address, awaiter.Type.MethodTable);
                                    Write(" ");
                                    WriteMethodTable(awaiter.Type.MethodTable, asyncObject: false);
                                    Write($" {awaiter.Type.Name}");
                                    WriteLine(" >>");
                                    return true;
                                }
                            }
                        }

                        return false;
                    }
                }

                // Push the root node onto the stack to start the iteration.  Then as long as there are nodes left
                // on the stack, pop the next, render it, and push any continuations it may have back onto the stack.
                Debug.Assert(stack.Count == 0);
                stack.Push((top, depth));
                
                sb.Append($"{"MethodTable",12} {"InsPtr",12} State\n");
                while (stack.Count > 0)
                {
                    (AsyncObject frame, depth) = stack.Pop();

                    sb.Append($"{frame.StateMachine.Type.MethodTable:x8} {frame.NativeCode:x8}".PadRight(25));
                    sb.Append($" {(frame.IsStateMachine ? $"{frame.AwaitState}" : $"{DescribeTaskFlags(frame.TaskStateFlags)}"), 5}");
                    sb.Append($"{Tabs(depth)}");
                    //WriteAddress(frame.Object.Address, asyncObject: true);
                    sb.Append(" ");
                    //sb.Append(frame.Object.Type?.MethodTable ?? 0, asyncObject: true);
                    sb.Append($" {Describe(frame.Object)}");
                    //sb.Append(frame.NativeCode);
                    sb.Append("\n");

                    if (DisplayFields)
                    {
                        RenderFields(frame.StateMachine ?? frame.Object, depth + 4); // +4 for extra indent for fields
                    }

                    foreach (ClrObject continuation in frame.Continuations)
                    {
                        if (objects.TryGetValue(continuation, out AsyncObject asyncContinuation))
                        {
                            stack.Push((asyncContinuation, depth + 1));
                        }
                        else
                        {
                            string state = TryGetTaskStateFlags(continuation, out int flags) ? DescribeTaskFlags(flags) : "";
                            sb.Append($"{frame.StateMachine.Type.MethodTable:x8} {frame.NativeCode:x8}".PadRight(25));
                            sb.Append($" {state, 5}");
                            sb.Append($"{Tabs(depth + 1)}");
                            //WriteAddress(continuation.Address, asyncObject: true);
                            sb.Append(" ");
                            //WriteMethodTable(continuation.Type?.MethodTable ?? 0, asyncObject: true);
                            sb.Append($" {Describe(continuation)}\n");
                        }
                    }
                }
                result.Add((top, sb.ToString()));

                sb.Append("\n");
            }

            return result;
        }

        // <summary>Determine whether the stack rooted in this object should be rendered.</summary>
        bool ShouldIncludeStack(AsyncObject obj)
        {
            // We want to render the stack for this object once we find any node that should be
            // included based on the criteria specified as arguments _and_ if the include tasks
            // options wasn't specified, once we find any node that's an async state machine.
            // That way, we scope the output down to just stacks that contain something the
            // user is interested in seeing.
            bool sawShouldInclude = false;
            bool sawStateMachine = IncludeTasks;

            Stack<AsyncObject> stack = new();
            stack.Push(obj);
            while (stack.Count > 0)
            {
                obj = stack.Pop();
                sawShouldInclude |= obj.IncludeInOutput;
                sawStateMachine |= obj.IsStateMachine;

                if (sawShouldInclude && sawStateMachine)
                {
                    return true;
                }

                foreach (ClrObject continuation in obj.Continuations)
                {
                    if (objects.TryGetValue(continuation, out AsyncObject asyncContinuation))
                    {
                        stack.Push(asyncContinuation);
                    }
                }
            }

            return false;
        }

        // <summary>Outputs a line of information for each instance field on the object.</summary>
        void RenderFields(IClrValue? obj, int depth)
        {
            if (obj?.Type is not null)
            {
                string depthTab = new(' ', depth * TabWidth);

                WriteHeaderLine($"{depthTab}{"Address",16} {"MT",16} {"Type",-32} {"Value",16} Name");
                foreach (ClrInstanceField field in obj.Type.Fields)
                {
                    if (field.Type is not null)
                    {
                        Write($"{depthTab}");
                        if (field.IsObjectReference)
                        {
                            ClrObject objRef = field.ReadObject(obj.Address, obj.Type.IsValueType);
                            WriteAddress(objRef.Address, asyncObject: false);
                        }
                        else
                        {
                            WriteValueTypeAddress(field.GetAddress(obj.Address, obj.Type.IsValueType), field.Type.MethodTable);
                        }
                        Write(" ");
                        WriteMethodTable(field.Type.MethodTable, asyncObject: false);
                        WriteLine($" {Truncate(field.Type.Name, 32),-32} {Truncate(GetDisplay(obj, field).ToString(), 16),16} {field.Name}");
                    }
                }
            }
        }

        // <summary>Gets a printable description for the specified object.</summary>
        string Describe(ClrObject obj)
        {
            string description = string.Empty;
            if (obj.Type?.Name is not null)
            {
                // Default the description to the type name.
                description = obj.Type.Name;

                if (IsStateMachineBox(obj.Type))
                {
                    // Remove the boilerplate box type from the name.
                    int pos = description.IndexOf("StateMachineBox<", StringComparison.Ordinal);
                    if (pos >= 0)
                    {
                        ReadOnlySpan<char> slice = description.AsSpan(pos + "StateMachineBox<".Length);
                        slice = slice.Slice(0, slice.Length - 1); // remove trailing >
                        description = slice.ToString();
                    }
                }
                else if (TryGetValidObjectField(obj, "m_action", out ClrObject taskDelegate))
                {
                    // If we can figure out what the task's delegate points to, append the method signature.
                    if (TryGetMethodFromDelegate(runtime, taskDelegate, out ClrMethod? method))
                    {
                        description = $"{description} {{{method!.Signature}}}";
                    }
                }
                else if (obj.Address != 0 && taskCompletionSentinel.Address == obj.Address)
                {
                    description = "TaskCompletionSentinel";
                }
            }
            return description;
        }

        // <summary>Determines whether the specified object is of interest to the user based on their criteria provided as command arguments.</summary>
        bool IncludeInOutput(ClrObject obj)
        {
            if (_objectAddress is ulong addr && obj.Address != addr)
            {
                return false;
            }

            if (obj.Type is not null)
            {
                if (_methodTableAddress is ulong mt && obj.Type.MethodTable != mt)
                {
                    return false;
                }

                if (NameSubstring is not null && obj.Type.Name is not null && !obj.Type.Name.Contains(NameSubstring))
                {
                    return false;
                }
            }

            return true;
        }

        // <summary>Finds all of the relevant async-related objects on the heap.</summary>
        Dictionary<ClrObject, AsyncObject> CollectObjects()
        {
            Dictionary<ClrObject, AsyncObject> found = new();

            // Enumerate the heap, looking for all relevant objects.
            foreach (ClrObject obj in heap.EnumerateObjects())
            {
                //Console.CancellationToken.ThrowIfCancellationRequested();

                if (!obj.IsValid || obj.Type is null)
                {
                    Trace.TraceError($"(Skipping invalid object {obj})");
                    continue;
                }

                // Skip objects too small to be state machines or tasks, simply to help with performance.
                if (obj.Size <= 24)
                {
                    continue;
                }

                // We only care about task-related objects (all boxes are tasks).
                if (!IsTask(obj.Type))
                {
                    continue;
                }

                // This is currently working around an issue that result in enumerating segments multiple times in 6.0 runtimes
                // up to 6.0.5. The PR that fixes it is https://github.com/dotnet/runtime/pull/67995, but we have this here for back compat.
                if (found.ContainsKey(obj))
                {
                    continue;
                }

                // If we're only going to render a summary (which only considers objects individually and not
                // as part of chains) and if this object shouldn't be included, we don't need to do anything more.
                if (Summarize &&
                    (!IncludeInOutput(obj) || (!IncludeTasks && !IsStateMachineBox(obj.Type))))
                {
                    continue;
                }

                // If we couldn't get state flags for the task, something's wrong; skip it.
                if (!TryGetTaskStateFlags(obj, out int taskStateFlags))
                {
                    continue;
                }

                // If we're supposed to ignore already completed tasks and this one is completed, skip it.
                if (!IncludeCompleted && IsCompleted(taskStateFlags))
                {
                    continue;
                }

                // Gather up the necessary data for the object and store it.
                AsyncObject result = new()
                {
                    Object = obj,
                    IsStateMachine = IsStateMachineBox(obj.Type),
                    IncludeInOutput = IncludeInOutput(obj),
                    TaskStateFlags = taskStateFlags,
                };

                if (result.IsStateMachine && TryGetStateMachine(obj, out result.StateMachine))
                {
                    bool gotState = TryRead(result.StateMachine!, "<>1__state", out result.AwaitState);
                    Debug.Assert(gotState);

                    if (result.StateMachine?.Type is ClrType stateMachineType)
                    {
                        foreach (ClrMethod method in stateMachineType.Methods)
                        {
                            if (method.NativeCode != ulong.MaxValue)
                            {
                                result.NativeCode = method.NativeCode;
                                if (method.Name == "MoveNext")
                                {
                                    break;
                                }
                            }
                        }
                    }
                }

                if (TryGetContinuation(obj, out ClrObject continuation))
                {
                    AddContinuation(continuation, result.Continuations);
                }

                found.Add(obj, result);
            }

            // Mark off objects that are referenced by others and thus aren't top level
            foreach (KeyValuePair<ClrObject, AsyncObject> entry in found)
            {
                foreach (ClrObject continuation in entry.Value.Continuations)
                {
                    if (found.TryGetValue(continuation, out AsyncObject asyncContinuation))
                    {
                        asyncContinuation.TopLevel = false;
                    }
                }
            }

            return found;
        }

        // <summary>Adds the continuation into the list of continuations.</summary>
        // <remarks>
        // If the continuation is actually a List{object}, enumerate the list to add
        // each of the individual continuations to the continuations list.
        // </remarks>
        void AddContinuation(ClrObject continuation, List<ClrObject> continuations)
        {
            if (continuation.Type is not null)
            {
                if (continuation.Type.Name is not null &&
                    continuation.Type.Name.StartsWith("System.Collections.Generic.List<", StringComparison.Ordinal))
                {
                    if (continuation.Type.GetFieldByName("_items") is ClrInstanceField itemsField)
                    {
                        ClrObject itemsObj = itemsField.ReadObject(continuation.Address, interior: false);
                        if (!itemsObj.IsNull)
                        {
                            ClrArray items = itemsObj.AsArray();
                            if (items.Rank == 1)
                            {
                                for (int i = 0; i < items.Length; i++)
                                {
                                    if (items.GetObjectValue(i) is ClrObject { IsValid: true } c)
                                    {
                                        continuations.Add(ResolveContinuation(c));
                                    }
                                }
                            }
                        }
                    }
                }
                else
                {
                    continuations.Add(continuation);
                }
            }
        }

        // <summary>Tries to get the object contents of a Task's continuations field</summary>
        bool TryGetContinuation(ClrObject obj, out ClrObject continuation)
        {
            if (obj.Type is not null &&
                obj.Type.GetFieldByName("m_continuationObject") is ClrInstanceField continuationObjectField &&
                continuationObjectField.ReadObject(obj.Address, interior: false) is ClrObject { IsValid: true } continuationObject)
            {
                continuation = ResolveContinuation(continuationObject);
                return true;
            }

            continuation = default;
            return false;
        }

        // <summary>Analyzes a continuation object to try to follow to the actual continuation target.</summary>
        ClrObject ResolveContinuation(ClrObject continuation)
        {
            ClrObject tmp;

            // If the continuation is an async method box, there's nothing more to resolve.
            if (IsTask(continuation.Type) && IsStateMachineBox(continuation.Type))
            {
                return continuation;
            }

            // If it's a standard task continuation, get its task field.
            if (TryGetValidObjectField(continuation, "m_task", out tmp))
            {
                return tmp;
            }

            // If it's storing an action wrapper, try to follow to that action's target.
            if (TryGetValidObjectField(continuation, "m_action", out tmp))
            {
                continuation = tmp;
            }

            // If we now have an Action, try to follow through to the delegate's target.
            if (TryGetValidObjectField(continuation, "_target", out tmp))
            {
                continuation = tmp;

                // In some cases, the delegate's target might be a ContinuationWrapper, in which case we want to unwrap that as well.
                if (continuation.Type?.Name == "System.Runtime.CompilerServices.AsyncMethodBuilderCore+ContinuationWrapper" &&
                    TryGetValidObjectField(continuation, "_continuation", out tmp))
                {
                    continuation = tmp;
                    if (TryGetValidObjectField(continuation, "_target", out tmp))
                    {
                        continuation = tmp;
                    }
                }
            }

            // Use whatever we ended with.
            return continuation;
        }

        // <summary>Determines if a type is or is derived from Task.</summary>
        bool IsTask(ClrType? type)
        {
            while (type is not null)
            {
                if (type.MetadataToken == taskType.MetadataToken &&
                    type.Module == taskType.Module)
                {
                    return true;
                }

                type = type.BaseType;
            }

            return false;
        }
    }

    /// <summary>Writes out a header line.  If DML is supported, this will be bolded.</summary>
    private void WriteHeaderLine(string text)
    {
        //if (Console.SupportsDml)
        //{
        //    Console.WriteDml($"<b>{text}</b>{Environment.NewLine}");
        //}
        //else
        //{
        WriteLine(text);
        //}
    }

    /// <summary>Writes out a method table address.  If DML is supported, this will be linked.</summary>
    /// <param name="mt">The method table address.</param>
    /// <param name="asyncObject">
    /// true if this is an async-related object; otherwise, false.  If true and if DML is supported,
    /// a link to dumpasync will be generated.  If false and if DML is supported, a link to dumpmt
    /// will be generated.
    /// </param>
    private void WriteMethodTable(ulong mt, bool asyncObject)
    {
        string completed = IncludeCompleted ? "--completed" : "";
        string tasks = IncludeTasks ? "--tasks" : "";

        //switch ((Console.SupportsDml, asyncObject, IntPtr.Size))
        //{
        //    case (false, _, 4):
                Console.Write($"{mt,16:x8}");
        //        break;

        //    case (false, _, 8):
        //        Console.Write($"{mt:x16}");
        //        break;

        //    case (true, true, 4):
        //        Console.WriteDml($"<exec cmd=\"{DmlCommandInvoke} --methodtable 0x{mt:x8} {tasks} {completed}\">{mt,16:x8}</exec>");
        //        break;

        //    case (true, true, 8):
        //        Console.WriteDml($"<exec cmd=\"{DmlCommandInvoke} --methodtable 0x{mt:x16} {tasks} {completed}\">{mt:x16}</exec>");
        //        break;

        //    case (true, false, 4):
        //        Console.WriteDml($"<exec cmd=\"!DumpMT /d 0x{mt:x8}\">{mt,16:x8}</exec>");
        //        break;

        //    case (true, false, 8):
        //        Console.WriteDml($"<exec cmd=\"!DumpMT /d 0x{mt:x16}\">{mt:x16}</exec>");
        //        break;
        //}
    }

    /// <summary>Writes out an object address.  If DML is supported, this will be linked.</summary>
    /// <param name="addr">The object address.</param>
    /// <param name="asyncObject">
    /// true if this is an async-related object; otherwise, false.  If true and if DML is supported,
    /// a link to dumpasync will be generated.  If false and if DML is supported, a link to dumpobj
    /// will be generated.
    /// </param>
    private void WriteAddress(ulong addr, bool asyncObject)
    {
        string completed = IncludeCompleted ? "--completed" : "";
        string tasks = IncludeTasks ? "--tasks" : "";

        //switch ((Console.SupportsDml, asyncObject, IntPtr.Size))
        //{
        //    case (false, _, 4):
        //        Console.Write($"{addr,16:x8}");
        //        break;

        //    case (false, _, 8):
        //        Console.Write($"{addr:x16}");
        //        break;

        //    case (true, true, 4):
        //        Console.WriteDml($"<exec cmd=\"{DmlCommandInvoke} --address 0x{addr:x8} {tasks} {completed} --fields\">{addr,16:x8}</exec>");
        //        break;

        //    case (true, true, 8):
        //        Console.WriteDml($"<exec cmd=\"{DmlCommandInvoke} --address 0x{addr:x16} {tasks} {completed} --fields\">{addr:x16}</exec>");
        //        break;

        //    case (true, false, 4):
        //        Console.WriteDml($"<exec cmd=\"!DumpObj /d 0x{addr:x8}\">{addr,16:x8}</exec>");
        //        break;

        //    case (true, false, 8):
        //        Console.WriteDml($"<exec cmd=\"!DumpObj /d 0x{addr:x16}\">{addr:x16}</exec>");
        //        break;
        //}
    }

    /// <summary>Writes out a value type address.  If DML is supported, this will be linked.</summary>
    /// <param name="addr">The value type's address.</param>
    /// <param name="mt">The value type's method table address.</param>
    private void WriteValueTypeAddress(ulong addr, ulong mt)
    {
        //switch ((Console.SupportsDml, IntPtr.Size))
        //{
        //    case (false, 4):
        //        Console.Write($"{addr,16:x8}");
        //        break;

        //    case (false, 8):
        //        Console.Write($"{addr:x16}");
        //        break;

        //    case (true, 4):
        //        Console.WriteDml($"<exec cmd=\"!DumpVC /d 0x{mt:x8} 0x{addr:x8}\">{addr,16:x8}</exec>");
        //        break;

        //    case (true, 8):
        //        Console.WriteDml($"<exec cmd=\"!DumpVC /d 0x{mt:x16} 0x{addr:x16}\">{addr:x16}</exec>");
        //        break;
        //}
    }

    /// <summary>Writes out a link that should open the source code for an address, if available.</summary>
    /// <remarks>If DML is not supported, this is a nop.</remarks>
    private void WriteCodeLink(ulong address)
    {
        //if (address != 0 && address != ulong.MaxValue &&
        //    Console.SupportsDml)
        //{
        //    Console.WriteDml($" <link cmd=\".open -a 0x{address:x}\" alt=\"Source link\">@ {address:x}</link>");
        //}
    }

    /// <summary>Gets whether the specified type is an AsyncStateMachineBox{T}.</summary>
    private static bool IsStateMachineBox(ClrType? type)
    {
        // Ideally we would compare the metadata token and module for the generic template for the type,
        // but that information isn't fully available via ClrMd, nor can it currently find DebugFinalizableAsyncStateMachineBox
        // due to various limitations.  So we're left with string comparison.
        const string Prefix = "System.Runtime.CompilerServices.AsyncTaskMethodBuilder<";
        return
            type?.Name is string name &&
            name.StartsWith(Prefix, StringComparison.Ordinal) &&
            name.IndexOf("AsyncStateMachineBox", Prefix.Length, StringComparison.Ordinal) >= 0;
    }

    /// <summary>Tries to get the compiler-generated state machine instance from a state machine box.</summary>
    private static bool TryGetStateMachine(ClrObject obj, out IClrValue? stateMachine)
    {
        // AsyncStateMachineBox<T> has a StateMachine field storing the compiler-generated instance.
        if (obj.Type?.GetFieldByName("StateMachine") is ClrInstanceField field)
        {
            if (field.IsValueType)
            {
                if (obj.ReadValueTypeField("StateMachine") is ClrValueType { IsValid: true } t)
                {
                    stateMachine = t;
                    return true;
                }
            }
            else if (field.ReadObject(obj.Address, interior: false) is ClrObject { IsValid: true } t)
            {
                stateMachine = t;
                return true;
            }
        }

        stateMachine = null;
        return false;
    }

    /// <summary>Extract from the specified field of the specified object something that can be ToString'd.</summary>
    private static object GetDisplay(IClrValue obj, ClrInstanceField field)
    {
        if (field.Name is string fieldName)
        {
            switch (field.ElementType)
            {
                case ClrElementType.Boolean:
                    return obj.ReadField<bool>(fieldName) ? "true" : "false";

                case ClrElementType.Char:
                    char c = obj.ReadField<char>(fieldName);
                    return c >= 32 && c < 127 ? $"'{c}'" : $"'\\u{(int)c:X4}'";

                case ClrElementType.Int8:
                    return obj.ReadField<sbyte>(fieldName);

                case ClrElementType.UInt8:
                    return obj.ReadField<byte>(fieldName);

                case ClrElementType.Int16:
                    return obj.ReadField<short>(fieldName);

                case ClrElementType.UInt16:
                    return obj.ReadField<ushort>(fieldName);

                case ClrElementType.Int32:
                    return obj.ReadField<int>(fieldName);

                case ClrElementType.UInt32:
                    return obj.ReadField<uint>(fieldName);

                case ClrElementType.Int64:
                    return obj.ReadField<long>(fieldName);

                case ClrElementType.UInt64:
                    return obj.ReadField<ulong>(fieldName);

                case ClrElementType.Float:
                    return obj.ReadField<float>(fieldName);

                case ClrElementType.Double:
                    return obj.ReadField<double>(fieldName);

                case ClrElementType.String:
                    return $"\"{obj.ReadStringField(fieldName)}\"";

                case ClrElementType.Pointer:
                case ClrElementType.NativeInt:
                case ClrElementType.NativeUInt:
                case ClrElementType.FunctionPointer:
                    return obj.ReadField<ulong>(fieldName).ToString(IntPtr.Size == 8 ? "x16" : "x8");

                case ClrElementType.SZArray:
                    IClrValue arrayObj = obj.ReadObjectField(fieldName);
                    if (!arrayObj.IsNull)
                    {
                        IClrArray arrayObjAsArray = arrayObj.AsArray();
                        return $"{arrayObj.Type?.ComponentType?.ToString() ?? "unknown"}[{arrayObjAsArray.Length}]";
                    }
                    return "null";

                case ClrElementType.Struct:
                    return field.GetAddress(obj.Address).ToString(IntPtr.Size == 8 ? "x16" : "x8");

                case ClrElementType.Array:
                case ClrElementType.Object:
                case ClrElementType.Class:
                    IClrValue classObj = obj.ReadObjectField(fieldName);
                    return classObj.IsNull ? "null" : classObj.Address.ToString(IntPtr.Size == 8 ? "x16" : "x8");

                case ClrElementType.Var:
                    return "(var)";

                case ClrElementType.GenericInstantiation:
                    return "(generic instantiation)";

                case ClrElementType.MVar:
                    return "(mvar)";

                case ClrElementType.Void:
                    return "(void)";
            }
        }

        return "(unknown)";
    }

    /// <summary>Tries to get a ClrMethod for the method wrapped by a delegate object.</summary>
    private static bool TryGetMethodFromDelegate(ClrRuntime runtime, ClrObject delegateObject, out ClrMethod? method)
    {
        ClrInstanceField? methodPtrField = delegateObject.Type?.GetFieldByName("_methodPtr");
        ClrInstanceField? methodPtrAuxField = delegateObject.Type?.GetFieldByName("_methodPtrAux");

        if (methodPtrField is not null && methodPtrAuxField is not null)
        {
            ulong methodPtr = methodPtrField.Read<UIntPtr>(delegateObject.Address, interior: false).ToUInt64();
            if (methodPtr != 0)
            {
                method = runtime.GetMethodByInstructionPointer(methodPtr);
                if (method is null)
                {
                    methodPtr = methodPtrAuxField.Read<UIntPtr>(delegateObject.Address, interior: false).ToUInt64();
                    if (methodPtr != 0)
                    {
                        method = runtime.GetMethodByInstructionPointer(methodPtr);
                    }
                }

                return method is not null;
            }
        }

        method = null;
        return false;
    }

    /// <summary>Creates an indenting string.</summary>
    /// <param name="count">The number of tabs.</param>
    private static string Tabs(int count) => new(' ', count * TabWidth);

    /// <summary>Shortens a string to a maximum length by eliding part of the string with ...</summary>
    private static string? Truncate(string? value, int maxLength)
    {
        if (value is not null && value.Length > maxLength)
        {
            value = $"...{value.Substring(value.Length - maxLength + 3)}";
        }

        return value;
    }

    /// <summary>Tries to get the state flags from a task.</summary>
    private static bool TryGetTaskStateFlags(ClrObject obj, out int flags)
    {
        if (obj.Type?.GetFieldByName("m_stateFlags") is ClrInstanceField field)
        {
            flags = field.Read<int>(obj.Address, interior: false);
            return true;
        }

        flags = 0;
        return false;
    }

    /// <summary>Tries to read the specified value from the field of an entity.</summary>
    private static bool TryRead<T>(IClrValue entity, string fieldName, out T result) where T : unmanaged
    {
        if (entity.Type?.GetFieldByName(fieldName) is not null)
        {
            result = entity.ReadField<T>(fieldName);
            return true;
        }

        result = default;
        return false;
    }

    /// <summary>Tries to read an object from a field of another object.</summary>
    private static bool TryGetValidObjectField(ClrObject obj, string fieldName, out ClrObject result)
    {
        if (obj.Type?.GetFieldByName(fieldName) is ClrInstanceField field &&
            field.ReadObject(obj.Address, interior: false) is { IsValid: true } validObject)
        {
            result = validObject;
            return true;
        }

        result = default;
        return false;
    }

    /// <summary>Gets whether a task has completed, based on its state flags.</summary>
    private static bool IsCompleted(int taskStateFlags)
    {
        const int TASK_STATE_COMPLETED_MASK = 0x1600000;
        return (taskStateFlags & TASK_STATE_COMPLETED_MASK) != 0;
    }

    /// <summary>Determines whether a span contains all zeros.</summary>
    private static bool AllZero(ReadOnlySpan<byte> bytes)
    {
        for (int i = 0; i < bytes.Length; i++)
        {
            if (bytes[i] != 0)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Gets a string representing interesting aspects of the specified task state flags.</summary>
    /// <remarks>
    /// The goal of this method isn't to detail every flag value (there are a lot).
    /// Rather, we only want to render flags that are likely to be valuable, e.g.
    /// we don't render WaitingForActivation, as that's the expected state for any
    /// task that's showing up in a stack.
    /// </remarks>
    private static string DescribeTaskFlags(int stateFlags)
    {
        if (stateFlags != 0)
        {
            StringBuilder? sb = null;
            void Append(string s)
            {
                sb ??= new StringBuilder();
                if (sb.Length != 0)
                {
                    sb.Append('|');
                }

                sb.Append(s);
            }

            if ((stateFlags & 0x10000) != 0) { Append("Started"); }
            if ((stateFlags & 0x20000) != 0) { Append("DelegateInvoked"); }
            if ((stateFlags & 0x40000) != 0) { Append("Disposed"); }
            if ((stateFlags & 0x80000) != 0) { Append("ExceptionObservedByParent"); }
            if ((stateFlags & 0x100000) != 0) { Append("CancellationAcknowledged"); }
            if ((stateFlags & 0x200000) != 0) { Append("Faulted"); }
            if ((stateFlags & 0x400000) != 0) { Append("Canceled"); }
            if ((stateFlags & 0x800000) != 0) { Append("WaitingOnChildren"); }
            if ((stateFlags & 0x1000000) != 0) { Append("RanToCompletion"); }
            if ((stateFlags & 0x4000000) != 0) { Append("CompletionReserved"); }

            if (sb is not null)
            {
                return sb.ToString();
            }
        }

        return " ";
    }

    /// <summary>Gets detailed help for the command.</summary>
    //[HelpInvoke]
    public static string GetDetailedHelp() =>
        @"Displays information about async ""stacks"" on the garbage-collected heap. Stacks
are synthesized by finding all task objects (including async state machine box
objects) on the GC heap and chaining them together based on continuations.

Examples:
   Summarize all async frames associated with a specific method table address:        dumpasync --stats --methodtable 0x00007ffbcfbe0970
   Show all stacks coalesced by common frames:                                        dumpasync --coalesce
   Show each stack that includes ""ReadAsync"":                                       dumpasync --type ReadAsync
   Show each stack that includes an object at a specific address, and include fields: dumpasync --address 0x000001264adce778 --fields
";

    /// <summary>Represents an async object to be used as a frame in an async "stack".</summary>
    private sealed class AsyncObject
    {
        /// <summary>The actual object on the heap.</summary>
        public ClrObject Object;
        /// <summary>true if <see cref="Object"/> is an AsyncStateMachineBox.</summary>
        public bool IsStateMachine;
        /// <summary>A compiler-generated state machine extracted from the object, if one exists.</summary>
        public IClrValue? StateMachine;
        /// <summary>The state of the state machine, if the object contains a state machine.</summary>
        public int AwaitState;
        /// <summary>The <see cref="Object"/>'s Task state flags, if it's a task.</summary>
        public int TaskStateFlags;
        /// <summary>Whether this object meets the user-specified criteria for inclusion.</summary>
        public bool IncludeInOutput;
        /// <summary>true if this is a top-level instance that nothing else continues to.</summary>
        /// <remarks>This starts off as true and then is flipped to false when we find a continuation to this object.</remarks>
        public bool TopLevel = true;
        /// <summary>The address of the native code for a method on the object (typically MoveNext for a state machine).</summary>
        public ulong NativeCode;
        /// <summary>This object's continuations.</summary>
        public readonly List<ClrObject> Continuations = new();
    }
}