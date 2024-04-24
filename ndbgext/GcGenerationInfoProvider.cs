using DbgEngExtension;
using Microsoft.Diagnostics.Runtime;

namespace ndbgext;

public class GcGenerationInfoCommand : DbgEngCommand
{
    private readonly GcGenerationInfoProvider _provider;

    public GcGenerationInfoCommand(GcGenerationInfoProvider provider, nint pUnknown, bool redirectConsoleOutput = true)
        : base(pUnknown, redirectConsoleOutput)
    {
        _provider = provider;
    }

    internal void Run(string args)
    {
        var split = args.Split(' ');
        var genNumber = 0;
        if (split.Length > 0)
        {
            switch (split[0].ToLower())
            {
                case "gen0":
                    genNumber = 0;
                    break;
                case "gen1":
                    genNumber = 1;
                    break;
                case "gen2":
                    genNumber = 2;
                    break;
                default:
                    Console.WriteLine("Unknown generation: {0}", split[0].ToLower());
                    return;
            }
        }
        else
        {
            Console.WriteLine("Specify a generation: (gen0|gen1|gen2)");
            return;
        }

        foreach (var runtime in Runtimes)
        {
            Console.WriteLine("Server Mode: {0}", runtime.Heap.IsServer);
            Console.WriteLine("Number of heaps: {0}", runtime.Heap.LogicalHeapCount);
            
            var generationItems = _provider.GetGenerationItems(runtime, genNumber);
            Helper.PrintHeapItems(generationItems);
        }
    }
}

public class GcGenerationInfoProvider
{
    public IReadOnlyList<HeapItem> GetGenerationItems(ClrRuntime runtime, int genNumber)
    {
        var result = new List<HeapItem>();
        var heap = runtime.Heap;
        foreach (var segment in heap.Segments)
        {
            ulong address = segment.FirstObjectAddress;
            while (address < segment.End && address > 0)
            {
                var gen = segment.GetGeneration(address);
                if (gen == genNumber)
                {
                    var type = segment.Heap.GetObject(address);
                    if (!type.IsFree)
                    {
                        var item = new HeapItem
                        {
                            TypeName = type.Type.Name,
                            Size = type.Size,
                            MethodTable = type.Type.MethodTable
                        };
                        result.Add(item);
                    }
                }
                address = segment.GetNextObjectAddress(address);
            }
        }

        return result;
    }
}