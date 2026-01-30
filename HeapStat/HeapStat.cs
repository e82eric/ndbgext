using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Diagnostics.Runtime;

namespace HeapStat
{
    public static class HeapStat
    {
        sealed class TypeStat
        {
            public ulong MethodTable { get; }
            public string TypeName { get; }
            public long Count { get; set; }
            public long TotalSize { get; set; }

            public TypeStat(ulong mt, string name)
            {
                MethodTable = mt;
                TypeName = name;
            }
        }
        
        public static void Run(ClrRuntime runtime)
        {
            var heap = runtime.Heap;
            if (!heap.CanWalkHeap)
            {
                Console.Error.WriteLine("Cannot walk heap (heap.CanWalkHeap == false).");
                return;
            }

            var stats = new Dictionary<ulong, TypeStat>(capacity: 8192);

            foreach (var obj in heap.EnumerateObjects())
            {
                if (!obj.IsValid)
                {
                    continue;
                }

                var type = obj.Type;
                if (type == null)
                {
                    continue;
                }

                ulong mt = type.MethodTable;
                string name = type.Name ?? "<unknown>";
                ulong size = obj.Size;

                if (!stats.TryGetValue(mt, out var s))
                {
                    s = new TypeStat(mt, name);
                    stats.Add(mt, s);
                }

                s.Count++;
                s.TotalSize += (long)size;
            }

            var ordered = stats.Values
                .OrderByDescending(s => s.TotalSize)
                .ThenByDescending(s => s.Count)
                .ToList();

            Console.WriteLine("{0,16} {1,10} {2,12} {3}",
                "MT", "Count", "TotalSize", "Type");

            foreach (var s in ordered)
            {
                Console.WriteLine("{0,16:X} {1,10:N0} {2,12:N0} {3}",
                    s.MethodTable, s.Count, s.TotalSize, s.TypeName);
            }

            long totalObjects = ordered.Sum(s => s.Count);
            long totalBytes = ordered.Sum(s => s.TotalSize);

            Console.WriteLine();
            Console.WriteLine("Total Objects: {0:N0}", totalObjects);
            Console.WriteLine("Total Bytes:   {0:N0}", totalBytes);
        }
    }
}