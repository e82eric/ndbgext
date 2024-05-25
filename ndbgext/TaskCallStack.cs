using DbgEngExtension;
using Microsoft.Diagnostics.Runtime;

namespace ndbgext;

public class TaskCallStackCommand : DbgEngCommand
{
    private readonly TaskCallStack _tasks;

    public TaskCallStackCommand(TaskCallStack tasks, nint pUnknown, bool redirectConsoleOutput = true)
        : base(pUnknown, redirectConsoleOutput)
    {
        _tasks = tasks;
    }
    
    internal void Run(string args)
    {
        var argsSplit = args.Split(' ');
        if (argsSplit.Length == 1)
        {
            if (Helper.TryParseAddress(argsSplit[0], out var address))
            {
                foreach (var runtime in Runtimes)
                {
                    _tasks.Run(runtime);
                }
            }
            else
            {
                foreach (var runtime in Runtimes)
                {
                    _tasks.Run(runtime);
                }
            }
        }
        if (argsSplit.Length == 0)
        {
            foreach (var runtime in Runtimes)
            {
                _tasks.Run(runtime);
            }
        }
    }
}

public class TaskCallStack
{
    private readonly List<TaskCallStackItem> _results = new();
    public void Run(ClrRuntime runtime)
    {
        _results.Clear();
        
        foreach (var clrObject in runtime.Heap.EnumerateObjects())
        {
            if (TryBuildItem(clrObject, null, null, out var item))
            {
                AddContinuationIfFound(item!, item, item!);
                
                var alreadyAdded = false;
                foreach (var result in _results)
                {
                    var current = result;
                    while (current != null)
                    {
                        if (current.Task.Address == item.Task.Address)
                        {
                            alreadyAdded = true;
                            break;
                        }
                        current = current.Next;
                    }
                }

                if (!alreadyAdded)
                {
                    _results.Add(item!);
                }
            }
        }

        foreach (var result in _results)
        {
            var current = result;
            while (current != null)
            {
                Console.WriteLine(current.ContinuationStateMachine.Type?.Name);
                current = current.Next;
            }
            Console.WriteLine();
        }

        var grouped = _results.GroupBy(cs => new
        {
            CallStack = cs.GetPrintable()
        });

        foreach (var group in grouped)
        {
            Console.WriteLine("-----");
            Console.WriteLine(group.Key.CallStack);
            Console.WriteLine(string.Join(" ", group.Select(g => g.Task.Address.ToString("X"))));
            Console.WriteLine("-----");
            Console.WriteLine();
        }
    }

    private void AddContinuationIfFound(TaskCallStackItem item, TaskCallStackItem? previousItem, TaskCallStackItem root)
    {
        if (TryBuildItem(item.ContinuationTask, root, previousItem, out var nextItem2))
        {
            AddContinuationIfFound(nextItem2, item, root);
        }
    }
    
    private bool TryBuildItem(ClrObject obj, TaskCallStackItem? root, TaskCallStackItem? previous, out TaskCallStackItem? item)
    {
        item = null;
        if (obj.TryReadObjectField("m_continuationObject", out var continuationObject))
        {
            if(continuationObject.TryReadObjectField("_target", out var target))
            {
                if (target.TryReadObjectField("m_stateMachine", out var stateMachine))
                {
                    if(stateMachine.TryReadValueTypeField("<>t__builder", out var builder))
                    {
                        var innerBuilder= builder.ReadValueTypeField("m_builder");
                        var task = innerBuilder.ReadObjectField("m_task");
                        item = new TaskCallStackItem
                        {
                            Task = obj,
                            ContinuationTask = task,
                            ContinuationStateMachine = stateMachine,
                        };
                        
                        foreach (var result in _results)
                        {
                            if (item.Task.Address == result.Task.Address)
                            {
                                var indexOfCurrent = _results.IndexOf(result);
                                previous.Next = result;
                                _results[indexOfCurrent] = root;
                                return false;
                            }
                            var current = result;
                            while (current != null)
                            {
                                if (current.Task.Address == item.Task.Address)
                                {
                                    return false;
                                }
                                current = current.Next;
                            }
                        }

                        if (previous != null)
                        {
                            previous.Next = item;
                        }

                        return true;
                    }
                }
            }
        }

        return false;
    }
}

public class TaskCallStackItem
{
    public ClrObject Task { get; set; }
    public ClrObject ContinuationTask { get; set; }
    public ClrObject ContinuationStateMachine { get; set; }
    public TaskCallStackItem? Next { get; set; }

    public string GetPrintable()
    {
        var toConcat = new List<string>();
        var current = this;
        while (current != null)
        {
            toConcat.Add(current.ContinuationStateMachine.Type.Name);
            current = current.Next;
        }

        var result = string.Join('\n', toConcat);
        return result;
    }
}