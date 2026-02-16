using System.Collections.Immutable;
using Microsoft.Diagnostics.ExtensionCommands;
using Microsoft.Diagnostics.Runtime;

namespace ClrMdHost;

class Program
{
    static void Main(string[] args)
    {
        
        using (var dataTarget = DataTarget.AttachToProcess(26620, true))
        {
            var command = new InclusiveBytesCommand();
            command.Run(dataTarget.ClrVersions.Select(v => v.CreateRuntime()));
        }


        //string dumpFilePath = @"C:\a\csdecompile.exe_240121_174819.dmp";
        // string dumpFilePath = @"C:\a\FrameLocals.exe_net8_2.dmp";
        //
        // using (DataTarget dataTarget = DataTarget.LoadDump(dumpFilePath))
        // {
        //     var clrRuntime = dataTarget.ClrVersions.Single().CreateRuntime();
        //     var gcRootCommand = new GCRootCommand(
        //         new RootCacheService(clrRuntime),
        //         new StaticVariableService(clrRuntime),
        //         clrRuntime);
        //     
        //     //gcRootCommand.TargetAddress = "0000020b7f014870";
        //     //gcRootCommand.NoStacks = false;
        //     ulong methodTable = 0x00007ff831f0ed60;
        //     //ulong methodTable = 0x00007ffbe28206f8;
        //     gcRootCommand.InvokeMethodTable(methodTable);
        //}
    }
    
    private static ulong GetDistance(ILToNativeMap entry, ulong nativeOffset)
    {
        ulong distance = 0;
        if (nativeOffset < entry.StartAddress)
        {
            distance = entry.StartAddress - nativeOffset;
        }
        else if (nativeOffset > entry.EndAddress)
        {
            distance = nativeOffset - entry.EndAddress;
        }

        return distance;
    }
    
    private static int GetILOffsetForNativeOffset(ClrMethod method, ulong ip)
    {
        ImmutableArray<ILToNativeMap> ilmap = method.ILOffsetMap;
        if (ilmap.IsDefaultOrEmpty)
        {
            return -1;
        }

        (ulong Distance, int Offset) closest = (ulong.MaxValue, -1);
        foreach (ILToNativeMap entry in ilmap)
        {
            ulong distance = GetDistance(entry, ip);
            if (distance == 0)
            {
                return entry.ILOffset;
            }

            if (distance < closest.Distance)
            {
                closest = (distance, entry.ILOffset);
            }
        }

        return closest.Offset;
    }   
}

public sealed class InclusiveBytesCommand
{
    internal void Run(IEnumerable<ClrRuntime> Runtimes)
    {
        int topN = 20;

        foreach (var runtime in Runtimes)
        {
            RunForRuntime(runtime, topN);
        }
    }

    private static int ParseTopN(string args, int defaultValue)
    {
        if (string.IsNullOrWhiteSpace(args))
        {
            return defaultValue;
        }

        var parts = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i].Equals("-n", StringComparison.OrdinalIgnoreCase) && i + 1 < parts.Length)
            {
                if (int.TryParse(parts[i + 1], out int n) && n > 0)
                {
                    return n;
                }
            }
        }

        return defaultValue;
    }

    private static void RunForRuntime(ClrRuntime runtime, int topN)
    {
        var heap = runtime.Heap;
        if (!heap.CanWalkHeap)
        {
            Console.Error.WriteLine("Cannot walk heap (heap.CanWalkHeap == false).");
            return;
        }

        var root = new Node(".NET Roots", isObject: false);
        var bucketMap = new Dictionary<string, Node>(StringComparer.Ordinal);
        var nodesByAddress = new Dictionary<ulong, Node>(capacity: 1024 * 1024);
        var visited = new HashSet<ulong>();

        AddStaticRoots(runtime, root, bucketMap, nodesByAddress, visited);
        AddClrRoots(runtime, root, bucketMap, nodesByAddress, visited);

        ComputeInclusive(root);

        var typeStats = new Dictionary<string, TypeStat>(StringComparer.Ordinal);
        AggregateByType(root, typeStats);

        var ordered = typeStats.Values
            .OrderByDescending(s => s.InclusiveBytes)
            .ThenByDescending(s => s.ExclusiveBytes)
            .Take(topN)
            .ToList();

        Console.WriteLine("{0,16} {1,16} {2,10} {3}",
            "InclusiveBytes", "ExclusiveBytes", "Count", "Type");

        foreach (var s in ordered)
        {
            Console.WriteLine("{0,16:N0} {1,16:N0} {2,10:N0} {3}",
                s.InclusiveBytes, s.ExclusiveBytes, s.ExclusiveCount, s.TypeName);
        }
    }

    private static void AddStaticRoots(
        ClrRuntime runtime,
        Node root,
        Dictionary<string, Node> bucketMap,
        Dictionary<ulong, Node> nodesByAddress,
        HashSet<ulong> visited)
    {
        var bucket = GetBucket(root, bucketMap, "Static Vars");

        foreach (var module in runtime.EnumerateModules())
        {
            foreach ((ulong mt, _) in module.EnumerateTypeDefToMethodTableMap())
            {
                ClrType? type = runtime.GetTypeByMethodTable(mt);
                if (type == null)
                {
                    continue;
                }

                foreach (var stat in type.StaticFields)
                {
                    if (!stat.IsObjectReference)
                    {
                        continue;
                    }

                    foreach (var domain in runtime.AppDomains)
                    {
                        TryAddStaticRoot(runtime, stat, domain, bucket, nodesByAddress, visited);
                    }

                    if (runtime.SharedDomain != null)
                    {
                        TryAddStaticRoot(runtime, stat, runtime.SharedDomain, bucket, nodesByAddress, visited);
                    }
                }
            }
        }
    }

    private static void TryAddStaticRoot(
        ClrRuntime runtime,
        ClrStaticField stat,
        ClrAppDomain domain,
        Node bucket,
        Dictionary<ulong, Node> nodesByAddress,
        HashSet<ulong> visited)
    {
        ClrObject obj = stat.ReadObject(domain);
        if (!obj.IsValid || obj.IsNull || obj.Type == null)
        {
            return;
        }

        if (!obj.Type.ContainsPointers && obj.Size <= 0x1000)
        {
            return;
        }

        AddRootObject(runtime, obj, bucket, nodesByAddress, visited);
    }

    private static void AddClrRoots(
        ClrRuntime runtime,
        Node root,
        Dictionary<string, Node> bucketMap,
        Dictionary<ulong, Node> nodesByAddress,
        HashSet<ulong> visited)
    {
        foreach (ClrRoot rootRecord in runtime.Heap.EnumerateRoots())
        {
            ClrObject obj = rootRecord.Object;
            if (!obj.IsValid || obj.IsNull || obj.Type == null)
            {
                continue;
            }

            string bucketName = GetBucketName(rootRecord);
            var bucket = GetBucket(root, bucketMap, bucketName);
            AddRootObject(runtime, obj, bucket, nodesByAddress, visited);
        }
    }

    private static string GetBucketName(ClrRoot root)
    {
        return root.RootKind switch
        {
            ClrRootKind.Stack => "Local Vars",
            ClrRootKind.RefCountedHandle => "COM/WinRT",
            _ => GetRootTitle(root.RootKind)
        };
    }

    private static string GetRootTitle(ClrRootKind kind)
    {
        return kind switch
        {
            ClrRootKind.FinalizerQueue => "Finalizer Queue",
            ClrRootKind.StrongHandle => "Strong Handle",
            ClrRootKind.PinnedHandle => "Pinned Handle",
            ClrRootKind.AsyncPinnedHandle => "AsyncPinnedHandle",
            ClrRootKind.SizedRefHandle => "SizedRefHandle",
            ClrRootKind.Stack => "Local Vars",
            ClrRootKind.RefCountedHandle => "COM/WinRT",
            _ => kind.ToString()
        };
    }

    private static Node GetBucket(Node root, Dictionary<string, Node> bucketMap, string name)
    {
        if (!bucketMap.TryGetValue(name, out var bucket))
        {
            bucket = new Node(name, isObject: false);
            root.Children.Add(bucket);
            bucketMap.Add(name, bucket);
        }

        return bucket;
    }

    private static void AddRootObject(
        ClrRuntime runtime,
        ClrObject obj,
        Node bucket,
        Dictionary<ulong, Node> nodesByAddress,
        HashSet<ulong> visited)
    {
        if (!visited.Add(obj.Address))
        {
            return;
        }

        var node = GetOrCreateNode(obj, nodesByAddress);
        bucket.Children.Add(node);

        var stack = new Stack<Node>();
        stack.Push(node);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            ClrObject currentObj = runtime.Heap.GetObject(current.Address);
            if (!currentObj.IsValid || currentObj.IsNull)
            {
                continue;
            }

            foreach (ClrObject childObj in currentObj.EnumerateReferences(true))
            {
                if (!childObj.IsValid || childObj.IsNull || childObj.Type == null)
                {
                    continue;
                }

                if (!visited.Add(childObj.Address))
                {
                    continue;
                }

                var childNode = GetOrCreateNode(childObj, nodesByAddress);
                current.Children.Add(childNode);
                stack.Push(childNode);
            }
        }
    }

    private static Node GetOrCreateNode(ClrObject obj, Dictionary<ulong, Node> nodesByAddress)
    {
        if (nodesByAddress.TryGetValue(obj.Address, out var node))
        {
            return node;
        }

        string typeName = obj.Type?.Name ?? "<unknown>";
        node = new Node(typeName, isObject: true)
        {
            Address = obj.Address,
            Size = (long)obj.Size,
            TypeName = typeName
        };
        nodesByAddress.Add(obj.Address, node);
        return node;
    }

    private static void ComputeInclusive(Node root)
    {
        var stack = new Stack<(Node Node, int State)>();
        stack.Push((root, 0));

        while (stack.Count > 0)
        {
            var (node, state) = stack.Pop();
            if (state == 0)
            {
                stack.Push((node, 1));
                for (int i = 0; i < node.Children.Count; i++)
                {
                    stack.Push((node.Children[i], 0));
                }
            }
            else
            {
                long bytes = node.Size;
                long count = node.IsObject ? 1 : 0;

                for (int i = 0; i < node.Children.Count; i++)
                {
                    var child = node.Children[i];
                    bytes += child.InclusiveBytes;
                    count += child.InclusiveCount;
                }

                node.InclusiveBytes = bytes;
                node.InclusiveCount = count;
            }
        }
    }

    private static void AggregateByType(Node root, Dictionary<string, TypeStat> stats)
    {
        var stack = new Stack<Node>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            var node = stack.Pop();
            for (int i = 0; i < node.Children.Count; i++)
            {
                stack.Push(node.Children[i]);
            }

            if (!node.IsObject)
            {
                continue;
            }

            string typeName = node.TypeName ?? "<unknown>";
            if (!stats.TryGetValue(typeName, out var stat))
            {
                stat = new TypeStat(typeName);
                stats.Add(typeName, stat);
            }

            stat.ExclusiveBytes += node.Size;
            stat.ExclusiveCount += 1;
            stat.InclusiveBytes += node.InclusiveBytes;
            stat.InclusiveCount += node.InclusiveCount;
        }
    }

    private sealed class Node
    {
        public string Name { get; }
        public bool IsObject { get; }
        public ulong Address { get; set; }
        public long Size { get; set; }
        public string? TypeName { get; set; }
        public long InclusiveBytes { get; set; }
        public long InclusiveCount { get; set; }
        public List<Node> Children { get; } = new();

        public Node(string name, bool isObject)
        {
            Name = name;
            IsObject = isObject;
        }
    }

    private sealed class TypeStat
    {
        public string TypeName { get; }
        public long ExclusiveBytes { get; set; }
        public long ExclusiveCount { get; set; }
        public long InclusiveBytes { get; set; }
        public long InclusiveCount { get; set; }

        public TypeStat(string typeName)
        {
            TypeName = typeName;
        }
    }
}
