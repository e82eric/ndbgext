using DbgEngExtension;
using Microsoft.Diagnostics.Runtime;

namespace ndbgext;

public class ThreadPoolCommand : DbgEngCommand
{
    private readonly ThreadPool _threadPool;

    public ThreadPoolCommand(nint pUnknown, ThreadPool threadPool, bool redirectConsoleOutput = true)
        : base(pUnknown, redirectConsoleOutput)
    {
        _threadPool = threadPool;
    }

    internal void Run(string args)
    {
        foreach (var runtime in Runtimes)
        {
            var isNetCore = Helper.IsNetCore(runtime);
            _threadPool.Show(runtime, isNetCore);
        }
    }
}

public class ThreadPool
{
    private readonly ConcurrentQueue _concurrentQueue;

    public ThreadPool(ConcurrentQueue concurrentQueue)
    {
        _concurrentQueue = concurrentQueue;
    }

    public void Show(ClrRuntime runtime, bool isNetCore)
    {
        var heap = runtime.Heap;
        ClrObject threadPoolWorkQueue = default(ClrObject);
        foreach (var obj in heap.EnumerateObjects())
        {
            if (obj.Type.Name == "System.Threading.ThreadPoolWorkQueue")
            {
                threadPoolWorkQueue = heap.GetObject(obj);
            }
        }

        Dictionary<string, WorkInfo> _tasks = new Dictionary<string, WorkInfo>();

        if (!threadPoolWorkQueue.IsNull)
        {
            Console.WriteLine("{0} {1:X}", threadPoolWorkQueue.Type.Name, threadPoolWorkQueue.Address);

            int ctr = 0;
            IReadOnlyList<ThreadPoolItem> results = !isNetCore ? GetThreadPoolItemsFramework(runtime, threadPoolWorkQueue)
                : GetThreadPoolItemsCore(runtime, threadPoolWorkQueue);

            Console.WriteLine("global work item queue________________________________");
            foreach (var result in results)
            {
                Console.WriteLine("{0:X} {1} {2}", result.Address, result.Type, result.MethodName);
                UpdateStats(_tasks, result.Type.ToString(), ref ctr);
            }

            var countByMethod = results
                .GroupBy(r => $"{r.Type}|{r.MethodName}")
                .Select(group => new { MethodName = group.Key, Count = group.Count() })
                .OrderBy(result => result.Count);

            Console.WriteLine();
            Console.WriteLine("method stats________________________________");
            foreach (var countBy in countByMethod)
            {
                Console.WriteLine("{0} {1}", countBy.MethodName, countBy.Count);
            }

            Console.WriteLine();
            Console.WriteLine("total stats________________________________");
            _tasks.OrderBy(t => t.Value.Count);
            foreach (var task in _tasks)
            {
                Console.WriteLine("{0}: {1}", task.Key, task.Value.Count);
            }
        }
    }

    IReadOnlyList<ThreadPoolItem> GetThreadPoolItemsCore(ClrRuntime runtime, ClrObject threadPoolWorkQueue)
    {
        var results = new List<ThreadPoolItem>();
        var workItems = threadPoolWorkQueue.ReadObjectField("workItems");
        Console.WriteLine("workItems: {0} {1:X}", workItems.Type.Name, workItems.Address);
        var items = _concurrentQueue.GetQueueItemsCore(workItems);
        foreach (var item in items)
        {
            var itemObj = runtime.Heap.GetObject(item.Address);
            var threadPoolItem = GetThreadPoolItem(runtime, itemObj);
            results.Add(threadPoolItem);
        }

        return results;
    }

    IReadOnlyList<ThreadPoolItem> GetThreadPoolItemsFramework(ClrRuntime runtime, ClrObject threadPoolWorkQueue)
    {
        var results = new List<ThreadPoolItem>();
        Dictionary<string, WorkInfo> _tasks = new Dictionary<string, WorkInfo>();
        var current = threadPoolWorkQueue.ReadObjectField("queueTail");
        while (!current.IsNull && current.IsValid)
        {
            var nodes = current.ReadObjectField("nodes").AsArray();
            for (var i = 0; i < nodes.Length; i++)
            {
                var node = nodes.GetObjectValue(i);
                if (!node.IsNull && node.IsValid)
                {
                    var threadPoolItem = GetThreadPoolItem(runtime, node);
                    results.Add(threadPoolItem);
                }
            }

            current = current.ReadObjectField("Next");
        }

        return results;
    }

    private void UpdateStats(Dictionary<string, WorkInfo> workInfos, string statName, ref int ctr)
    {
        ctr++;

        WorkInfo wi;
        if (!workInfos.ContainsKey(statName))
        {
            wi = new WorkInfo
            {
                Name = statName,
                Count = 0
            };
            workInfos[statName] = wi;
        }
        else
        {
            wi = workInfos[statName];
        }

        wi.Count++;
    }

    private ThreadPoolItem GetThreadPoolItem(ClrRuntime runtime, ClrObject itemObj)
    {
        ClrObject callback = default(ClrObject);
        var result = new ThreadPoolItem
        {
            Address = itemObj.Address
        };

        if (itemObj.Type.Name == "System.Threading.Tasks.Task")
        {
            callback = itemObj.ReadObjectField("m_action");
            result.Type = ThreadRoot.Task;
        }
        else
        {
            if (!itemObj.TryReadObjectField("_callback", out callback))
            {
                itemObj.TryReadObjectField("callback", out callback);
            }

            if (!callback.IsNull)
            {
                result.Type = ThreadRoot.WorkItem;
            }
            else
            {
                result.MethodName = "[no callback]";
                return result;
            }
        }

        ClrObject target = default(ClrObject);
        if (!callback.TryReadObjectField("_target", out target))
        {
            result.MethodName = "[no callback target]";
            return result;
        }

        if (target.Type == null)
        {
            //Is this going to be in hex?
            result.MethodName = $"[target=0x{(ulong)target}";
            return result;
        }

        var methodPtrVal = callback.ReadField<ulong>("_methodPtr");
        var method = runtime.GetMethodByInstructionPointer(methodPtrVal);
        if (method == null)
        {
            var methodPtrAuxVal = callback.ReadField<ulong>("_methodPtrAux");
            method = runtime.GetMethodByInstructionPointer(methodPtrAuxVal);
        }

        if (method != null)
        {
            // anonymous method
            if (method.Type.Name == target.Type.Name)
            {
                result.MethodName = $"{target.Type.Name}.{method.Name}";
            }
            // method is implemented by an class inherited from targetType
            // ... or a simple delegate indirection to a static/instance method
            else if(target.Type.Name == "System.Threading.WaitCallback"
                || target.Type.Name.StartsWith("System.Action<"))
            {
                result.MethodName = $"{method.Type.Name}.{method.Name}";
            }
            else
            {
                result.MethodName = $"{target.Type.Name}.{method.Type.Name}.{method.Name}";
            }
        }

        return result;
    }
}

public enum ThreadRoot
{
    Task,
    WorkItem
}

class ThreadPoolItem
{
    public ThreadRoot Type { get; set; }
    public ulong Address { get; set; }
    public string MethodName { get; set; }
}
class WorkInfo
{
    public string Name { get; set; }
    public int Count { get; set; }
}