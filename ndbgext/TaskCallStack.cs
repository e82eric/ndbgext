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
                    _tasks.Run(runtime, address);
                }
            }
        }
    }
}

public class TaskCallStack
{
    public void Run(ClrRuntime runtime, ulong address)
    {
        Task.Delay()
        var taskObj = runtime.Heap.GetObject(address);
        foreach (var clrObject in runtime.Heap.EnumerateObjects())
        {
            if (clrObject.Type/**/?.Name == "System.Runtime.CompilerServices.AsyncMethodBuilderCore+MoveNextRunner")
            {
                if (clrObject.TryReadObjectField("m_stateMachine", out var stateMachine))
                {
                    if(stateMachine.TryReadValueTypeField("<>t__builder", out var builder))
                    {
                        var innerBuilder= builder.ReadValueTypeField("m_builder");
                        var task = innerBuilder.ReadObjectField("m_task");
                        if (task.Address == address)
                        {
                            Console.WriteLine("Parent...");
                            Console.WriteLine(stateMachine.Type?.Name);
                            Console.WriteLine("  StateMachine: {0:x}", stateMachine.Address);
                            Console.WriteLine("  Task: {0:x}", stateMachine.Address);
                            Console.WriteLine("Parent...");
                        }
                    }
                }
            }
        }
        
        PrintContinuation(taskObj);
    }

    private static void PrintContinuation(ClrObject taskObj)
    {
        if (taskObj.TryReadObjectField("m_continuationObject", out var continuationObject))
        {
            if(continuationObject.TryReadObjectField("_target", out var target))
            {
                if (target.TryReadObjectField("m_stateMachine", out var stateMachine))
                {
                    if(stateMachine.TryReadValueTypeField("<>t__builder", out var builder))
                    {
                        var innerBuilder= builder.ReadValueTypeField("m_builder");
                        var task = innerBuilder.ReadObjectField("m_task");
                        Console.WriteLine(stateMachine.Type?.Name);
                        Console.WriteLine("  StateMachine: {0:x}", stateMachine.Address);
                        Console.WriteLine("  Task: {0:x}", stateMachine.Address);
                        PrintContinuation(task);
                    }
                }
            }
        }
    }
}