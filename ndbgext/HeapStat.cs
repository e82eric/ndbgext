using DbgEngExtension;
using Microsoft.Diagnostics.Runtime;

namespace ndbgext;

public class HeapStatCommand : DbgEngCommand
{
    private readonly HeapStatProvider _provider;

    public HeapStatCommand(HeapStatProvider provider, nint pUnknown, bool redirectConsoleOutput = true)
        : base(pUnknown, redirectConsoleOutput)
    {
        _provider = provider;
    }

    internal void Run(string args)
    {
        foreach (var runtime in Runtimes)
        {
            var items = _provider.Get(runtime);
            Helper.PrintHeapItems(items);
        }
    }
}

public class HeapStatProvider
{
    public IReadOnlyList<HeapItem> Get(ClrRuntime runtime)
    {
        var heap = runtime.Heap;
        var result = new List<HeapItem>();

        foreach (var obj in heap.EnumerateObjects())
        {
            var item = new HeapItem()
            {
                MethodTable = obj.Type.MethodTable,
                Size = obj.Size,
                TypeName = obj.Type.Name
            };
            result.Add(item);
        }

        return result;
    }
}