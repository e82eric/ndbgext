using DbgEngExtension;
using Microsoft.Diagnostics.Runtime;

namespace ndbgext;

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
            if (heapObject.Type != null && heapObject.Type.Name != null && heapObject.Type.Name.Contains("System.Threading.Tasks.Task"))
            {
                if (TaskHelper.TryGetTaskItem(runtime, heapObject, out var taskItem))
                {
                    result.Add(taskItem);
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
}

public class TasksItem
{
    public ulong Address { get; set; }
    public required string TaskName { get; set; }
    public required string TaskState { get; set; }
    public required string Method { get; set; }
    public string? ContinuationStateMachine { get; set; }
    public string? StateMachine { get; set; }
}