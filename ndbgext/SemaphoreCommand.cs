using DbgEngExtension;
using Microsoft.Diagnostics.Runtime;

namespace ndbgext;

public class SemaphoreCommand : DbgEngCommand
{
    private SemaphoreCommandRunner _runner;

    public SemaphoreCommand(SemaphoreCommandRunner runner, nint pUnknown, bool redirectConsoleOutput = true)
        : base(pUnknown, redirectConsoleOutput)
    {
        _runner = runner;
    }

    internal void Run(string args)
    {
        foreach (var runtime in Runtimes)
        {
            _runner.Run(runtime); 
        }
    }
}

public class SemaphoreCommandRunner
{
    private const string TASK_ASYNC_HEAD_NAME = "m_asyncHead";
    
    public void Run(ClrRuntime runtime)
    {
        var objs = runtime.Heap.EnumerateObjects()
            .Where(o => o.Type?.Name == "System.Threading.SemaphoreSlim")
            .ToList();

        foreach (var clrObject in objs)
        {
            Console.WriteLine("Address: {0:x8}", clrObject.Address);
            var syncWaitCount = clrObject.ReadField<Int32>("m_waitCount");
            Console.WriteLine("  Sync wait count: {0}", syncWaitCount);
            
            var asyncWaits = new List<ClrObject>();
            var currentAsyncWaitNode = clrObject.ReadObjectField(TASK_ASYNC_HEAD_NAME);
            while (!currentAsyncWaitNode.IsNull)
            {
                asyncWaits.Add(currentAsyncWaitNode);
                currentAsyncWaitNode = currentAsyncWaitNode.ReadObjectField("Next");      
            }
            
            Console.WriteLine("  Async wait count {0}", asyncWaits.Count);
            foreach (var asyncWait in asyncWaits)
            {
                Console.WriteLine("    Address: {0:x8}", asyncWait.Address);
                if (TaskHelper.TryGetTaskItem(runtime, asyncWait, out var task))
                {
                    Console.WriteLine("      Continuation: {0}", task.ContinuationStateMachine);
                }
            }
        }
    }
}
