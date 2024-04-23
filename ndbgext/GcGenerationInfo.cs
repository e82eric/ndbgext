using System.Data.Common;
using DbgEngExtension;
using Microsoft.Diagnostics.Runtime;

namespace ndbgext;

public class GcGenerationInfoCommand : DbgEngCommand
{
    public GcGenerationInfoCommand(nint pUnknown, bool redirectConsoleOutput = true)
        : base(pUnknown, redirectConsoleOutput)
    {
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
            var generationItems = new GcGenerationInfo().GetGenerationItems(runtime, genNumber);
            var byType = generationItems.GroupBy(gi => gi.TypeName);
            var statsByType = byType.Select(group => new
            {
                TypeName = group.Key, MethodTable = group.FirstOrDefault()?.MethodTable, Count = group.Count(), Size = group.Sum(g => (float)g.Size)
            }).OrderBy(s => s.Size);

            foreach (var typeStat in statsByType)
            {
                Console.WriteLine("{0:X} {1} {2} {3}", typeStat.MethodTable, typeStat.Size, typeStat.Count, typeStat.TypeName);
            }

            Console.WriteLine("Total Objects: {0}, Total Size: {1}", generationItems.Count(),
                generationItems.Sum(i => (float)i.Size));
        }
    }
}

public class GcGenerationInfo
{
    public IReadOnlyList<GenerationItem> GetGenerationItems(ClrRuntime runtime, int genNumber)
    {
        var result = new List<GenerationItem>();
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
                        var item = new GenerationItem
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

public class GenerationItem
{
    public ulong MethodTable { get; set; }
    public string TypeName { get; set; }
    public ulong Size { get; set; }
}