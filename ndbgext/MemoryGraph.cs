using Microsoft.Diagnostics.Runtime;

namespace ndbgext;

internal sealed class MemoryGraphCache
{
    private static StreamingMemoryGraph? _graph;

    public static StreamingMemoryGraph? Graph => _graph;

    public static void Set(StreamingMemoryGraph graph)
    {
        _graph = graph;
    }
}

internal sealed class MemoryGraphOptions
{
    public int MaxTypeRoots { get; set; } = 20;
    public int MaxTreeNodes { get; set; } = 2000;
    public int MaxDepth { get; set; } = 50;
    public bool Verbose { get; set; } = true;
    public string? TypeFilter { get; set; }
}

internal static class MemoryGraphRunner
{
    public static int Build(ClrRuntime[] runtimes)
    {
        if (runtimes.Length == 0)
        {
            Console.Error.WriteLine("No CLR runtimes found in target.");
            return 3;
        }

        StreamingMemoryGraph graph = StreamingMemoryGraphBuilder.BuildFromRuntimes(
            runtimes,
            maxObjects: int.MaxValue,
            log: Console.Out);

        MemoryGraphCache.Set(graph);
        PrintStats(graph);
        return 0;
    }

    public static int PrintTypeBytes(StreamingMemoryGraph graph, MemoryGraphOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.TypeFilter))
        {
            Console.Error.WriteLine("Type filter is required.");
            return 2;
        }

        int nodeCount = graph.NodeCount;
        int typeCount = graph.TypeCount;

        int[] nodeTypes = new int[nodeCount];
        int[] nodeSizes = new int[nodeCount];

        for (int i = 0; i < nodeCount; i++)
        {
            Node node = graph.GetNode(i);
            nodeTypes[i] = node.TypeIndex;
            nodeSizes[i] = node.Size;
        }

        int[] parent = new int[nodeCount];
        for (int i = 0; i < nodeCount; i++)
        {
            parent[i] = -1;
        }

        Queue<int> queue = new Queue<int>(Math.Min(nodeCount, 1024));
        int root = graph.RootIndex;
        parent[root] = root;
        queue.Enqueue(root);

        while (queue.Count > 0)
        {
            int current = queue.Dequeue();
            Node node = graph.GetNode(current);
            for (int child = node.GetFirstChildIndex(); child >= 0; child = node.GetNextChildIndex())
            {
                if (parent[child] == -1)
                {
                    parent[child] = current;
                    queue.Enqueue(child);
                }
            }
        }

        int[] childCounts = new int[nodeCount];
        for (int i = 0; i < nodeCount; i++)
        {
            int p = parent[i];
            if (p >= 0 && p != i)
            {
                childCounts[p]++;
            }
        }

        int[] childOffsets = new int[nodeCount + 1];
        for (int i = 0; i < nodeCount; i++)
        {
            childOffsets[i + 1] = childOffsets[i] + childCounts[i];
        }

        int[] treeChildren = new int[childOffsets[nodeCount]];
        int[] writeCursor = (int[])childOffsets.Clone();
        for (int i = 0; i < nodeCount; i++)
        {
            int p = parent[i];
            if (p >= 0 && p != i)
            {
                int cursor = writeCursor[p];
                treeChildren[cursor] = i;
                writeCursor[p] = cursor + 1;
            }
        }

        long[] inclusiveByNode = new long[nodeCount];

        List<(int Node, int State)> stack = new List<(int, int)>(nodeCount * 2);
        stack.Add((root, 0));
        while (stack.Count > 0)
        {
            int last = stack.Count - 1;
            (int node, int state) = stack[last];
            stack.RemoveAt(last);

            if (state == 0)
            {
                stack.Add((node, 1));
                int start = childOffsets[node];
                int end = childOffsets[node + 1];
                for (int i = end - 1; i >= start; i--)
                {
                    stack.Add((treeChildren[i], 0));
                }
            }
            else
            {
                long sum = nodeSizes[node];
                int start = childOffsets[node];
                int end = childOffsets[node + 1];
                for (int i = start; i < end; i++)
                {
                    sum += inclusiveByNode[treeChildren[i]];
                }
                inclusiveByNode[node] = sum;
            }
        }

        PrintTypeTree(
            graph,
            options,
            nodeTypes,
            nodeSizes,
            parent,
            childOffsets,
            treeChildren,
            inclusiveByNode);

        return 0;
    }

    public static int PrintTotalTypeBytes(StreamingMemoryGraph graph)
    {
        int nodeCount = graph.NodeCount;
        int typeCount = graph.TypeCount;

        int[] nodeTypes = new int[nodeCount];
        int[] nodeSizes = new int[nodeCount];

        for (int i = 0; i < nodeCount; i++)
        {
            Node node = graph.GetNode(i);
            nodeTypes[i] = node.TypeIndex;
            nodeSizes[i] = node.Size;
        }

        int[] parent = new int[nodeCount];
        for (int i = 0; i < nodeCount; i++)
        {
            parent[i] = -1;
        }

        Queue<int> queue = new Queue<int>(Math.Min(nodeCount, 1024));
        int root = graph.RootIndex;
        parent[root] = root;
        queue.Enqueue(root);

        while (queue.Count > 0)
        {
            int current = queue.Dequeue();
            Node node = graph.GetNode(current);
            for (int child = node.GetFirstChildIndex(); child >= 0; child = node.GetNextChildIndex())
            {
                if (parent[child] == -1)
                {
                    parent[child] = current;
                    queue.Enqueue(child);
                }
            }
        }

        int[] childCounts = new int[nodeCount];
        for (int i = 0; i < nodeCount; i++)
        {
            int p = parent[i];
            if (p >= 0 && p != i)
            {
                childCounts[p]++;
            }
        }

        int[] childOffsets = new int[nodeCount + 1];
        for (int i = 0; i < nodeCount; i++)
        {
            childOffsets[i + 1] = childOffsets[i] + childCounts[i];
        }

        int[] treeChildren = new int[childOffsets[nodeCount]];
        int[] writeCursor = (int[])childOffsets.Clone();
        for (int i = 0; i < nodeCount; i++)
        {
            int p = parent[i];
            if (p >= 0 && p != i)
            {
                int cursor = writeCursor[p];
                treeChildren[cursor] = i;
                writeCursor[p] = cursor + 1;
            }
        }

        long[] inclusiveByType = new long[typeCount];
        long[] exclusiveByType = new long[typeCount];
        long[] inclusiveByNode = new long[nodeCount];

        List<(int Node, int State)> stack = new List<(int, int)>(nodeCount * 2);
        stack.Add((root, 0));
        while (stack.Count > 0)
        {
            int last = stack.Count - 1;
            (int node, int state) = stack[last];
            stack.RemoveAt(last);

            if (state == 0)
            {
                stack.Add((node, 1));
                int start = childOffsets[node];
                int end = childOffsets[node + 1];
                for (int i = end - 1; i >= start; i--)
                {
                    stack.Add((treeChildren[i], 0));
                }
            }
            else
            {
                long sum = nodeSizes[node];
                int start = childOffsets[node];
                int end = childOffsets[node + 1];
                for (int i = start; i < end; i++)
                {
                    sum += inclusiveByNode[treeChildren[i]];
                }
                inclusiveByNode[node] = sum;
            }
        }

        for (int i = 0; i < nodeCount; i++)
        {
            int typeIdx = nodeTypes[i];
            exclusiveByType[typeIdx] += nodeSizes[i];
            inclusiveByType[typeIdx] += inclusiveByNode[i];
        }

        var ordered = Enumerable.Range(0, typeCount)
            .Select(i => (TypeIndex: i, Inclusive: inclusiveByType[i], Exclusive: exclusiveByType[i]))
            .OrderByDescending(x => x.Inclusive)
            .ThenByDescending(x => x.Exclusive);

        Console.WriteLine();
        Console.WriteLine("Inclusive/Exclusive bytes by type (spanning-tree inclusive):");
        foreach (var entry in ordered)
        {
            TypeInfo info = graph.GetTypeInfo(entry.TypeIndex);
            string name = info.Name ?? "[UNKNOWN]";
            string module = info.ModuleName;
            if (!string.IsNullOrWhiteSpace(module))
            {
                name = Path.GetFileNameWithoutExtension(module) + "!" + name;
            }

            Console.WriteLine("{0:n0}\t{1:n0}\t{2}", entry.Inclusive, entry.Exclusive, name);
        }

        return 0;
    }

    private static void PrintStats(StreamingMemoryGraph graph)
    {
        Console.WriteLine("Nodes: {0:n0}", graph.NodeCount);
        Console.WriteLine("Edges: {0:n0}", graph.TotalNumberOfReferences);
        Console.WriteLine("Types: {0:n0}", graph.TypeCount);
        Console.WriteLine("Total Size (bytes): {0:n0}", graph.TotalSize);
        Console.WriteLine("Stream Size (bytes): {0:n0}", graph.StreamSizeBytes);
    }

    private static void PrintTypeTree(
        StreamingMemoryGraph graph,
        MemoryGraphOptions options,
        int[] nodeTypes,
        int[] nodeSizes,
        int[] parent,
        int[] childOffsets,
        int[] treeChildren,
        long[] inclusiveByNode)
    {
        string filter = options.TypeFilter!.Trim();
        List<int> matchingTypeIndices = FindMatchingTypes(graph, filter);
        if (matchingTypeIndices.Count == 0)
        {
            Console.WriteLine();
            Console.WriteLine("Type filter '{0}' did not match any types.", filter);
            return;
        }

        HashSet<int> matchingTypes = new HashSet<int>(matchingTypeIndices);
        List<int> matchingNodes = new List<int>();
        for (int i = 0; i < nodeTypes.Length; i++)
        {
            if (matchingTypes.Contains(nodeTypes[i]))
            {
                matchingNodes.Add(i);
            }
        }

        if (matchingNodes.Count == 0)
        {
            Console.WriteLine();
            Console.WriteLine("No nodes found for type filter '{0}'.", filter);
            return;
        }

        var orderedRoots = matchingNodes
            .Select(n => (Node: n, Inclusive: inclusiveByNode[n]))
            .OrderByDescending(x => x.Inclusive)
            .Take(options.MaxTypeRoots)
            .ToList();

        Console.WriteLine();
        Console.WriteLine("Type tree for '{0}' (showing top {1} roots, max {2} nodes):", filter, orderedRoots.Count, options.MaxTreeNodes);

        int printed = 0;
        foreach (var root in orderedRoots)
        {
            if (printed >= options.MaxTreeNodes)
            {
                Console.WriteLine("... truncated ...");
                break;
            }

            Console.WriteLine();
            Console.WriteLine("Root node {0} inclusive {1:n0} exclusive {2:n0} type {3}",
                root.Node,
                inclusiveByNode[root.Node],
                nodeSizes[root.Node],
                FormatTypeName(graph, nodeTypes[root.Node]));

            printed += PrintSubtree(graph, root.Node, nodeTypes, nodeSizes, childOffsets, treeChildren, inclusiveByNode, options.MaxTreeNodes - printed, 0, options.MaxDepth);
        }
    }

    private static int PrintSubtree(
        StreamingMemoryGraph graph,
        int root,
        int[] nodeTypes,
        int[] nodeSizes,
        int[] childOffsets,
        int[] treeChildren,
        long[] inclusiveByNode,
        int remaining,
        int depth,
        int maxDepth)
    {
        int printed = 0;
        Stack<(int Node, int Depth)> stack = new Stack<(int, int)>();
        stack.Push((root, depth));

        List<int> sortedChildren = new List<int>();
        while (stack.Count > 0 && printed < remaining)
        {
            (int node, int d) = stack.Pop();
            if (d != depth)
            {
                Console.WriteLine("{0}{1:n0}\t{2:n0}\t{3}",
                    new string(' ', Math.Min(d, 40) * 2),
                    inclusiveByNode[node],
                    nodeSizes[node],
                    FormatTypeName(graph, nodeTypes[node]));
                printed++;
            }

            if (d < maxDepth)
            {
                int start = childOffsets[node];
                int end = childOffsets[node + 1];
                int count = end - start;
                if (count <= 0)
                {
                    continue;
                }

                sortedChildren.Clear();
                for (int i = start; i < end; i++)
                {
                    sortedChildren.Add(treeChildren[i]);
                }

                sortedChildren.Sort((a, b) => inclusiveByNode[b].CompareTo(inclusiveByNode[a]));

                for (int i = sortedChildren.Count - 1; i >= 0; i--)
                {
                    stack.Push((sortedChildren[i], d + 1));
                }
            }
        }

        if (stack.Count > 0)
        {
            Console.WriteLine("... truncated ...");
        }

        return printed;
    }

    private static List<int> FindMatchingTypes(StreamingMemoryGraph graph, string filter)
    {
        List<int> matches = new List<int>();
        for (int i = 0; i < graph.TypeCount; i++)
        {
            string fullName = FormatTypeName(graph, i);
            if (string.Equals(fullName, filter, StringComparison.OrdinalIgnoreCase))
            {
                matches.Add(i);
            }
        }

        if (matches.Count > 0)
        {
            return matches;
        }

        for (int i = 0; i < graph.TypeCount; i++)
        {
            string fullName = FormatTypeName(graph, i);
            if (fullName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                matches.Add(i);
            }
        }

        return matches;
    }

    private static string FormatTypeName(StreamingMemoryGraph graph, int typeIndex)
    {
        TypeInfo info = graph.GetTypeInfo(typeIndex);
        string name = info.Name ?? "[UNKNOWN]";
        string module = info.ModuleName;
        if (!string.IsNullOrWhiteSpace(module))
        {
            name = Path.GetFileNameWithoutExtension(module) + "!" + name;
        }

        return name;
    }
}

internal sealed class StreamingMemoryGraph
{
    private readonly List<TypeInfo> _types;
    private readonly List<int> _typeSizes;
    private readonly List<long> _nodeOffsets;
    private readonly MemoryStream _writeStream;
    private byte[]? _data;

    private long _totalSize;
    private int _totalRefs;

    public StreamingMemoryGraph(int expectedNodeCount)
    {
        _types = new List<TypeInfo>(Math.Max(expectedNodeCount / 100, 256));
        _typeSizes = new List<int>(_types.Capacity);
        _nodeOffsets = new List<long>(expectedNodeCount);
        _writeStream = new MemoryStream(expectedNodeCount * 8);
        RootIndex = -1;
    }

    public int RootIndex { get; set; }
    public int NodeCount => _nodeOffsets.Count;
    public int TypeCount => _types.Count;
    public long TotalSize => _totalSize;
    public int TotalNumberOfReferences => _totalRefs;
    public long StreamSizeBytes => _data != null ? _data.Length : _writeStream.Length;

    public int CreateType(string name, string? moduleName = null, int size = -1)
    {
        int idx = _types.Count;
        _types.Add(new TypeInfo(name, moduleName));
        _typeSizes.Add(size);
        return idx;
    }

    public int CreateNode()
    {
        int idx = _nodeOffsets.Count;
        _nodeOffsets.Add(-1);
        return idx;
    }

    public void SetNode(int nodeIndex, int typeIndex, int sizeInBytes, IList<int> children)
    {
        if (_nodeOffsets[nodeIndex] != -1)
        {
            throw new InvalidOperationException("Node already set: " + nodeIndex);
        }

        if (sizeInBytes < 0)
        {
            sizeInBytes = int.MaxValue;
        }

        long offset = _writeStream.Position;
        _nodeOffsets[nodeIndex] = offset;

        int storedTypeSize = _typeSizes[typeIndex];
        if (storedTypeSize < 0)
        {
            _typeSizes[typeIndex] = sizeInBytes;
            storedTypeSize = sizeInBytes;
        }

        int typeAndSize = typeIndex << 1;
        if (storedTypeSize == sizeInBytes)
        {
            WriteCompressedInt(_writeStream, typeAndSize);
        }
        else
        {
            typeAndSize |= 1;
            WriteCompressedInt(_writeStream, typeAndSize);
            WriteCompressedInt(_writeStream, sizeInBytes);
        }

        WriteCompressedInt(_writeStream, children.Count);
        for (int i = 0; i < children.Count; i++)
        {
            int delta = children[i] - nodeIndex;
            WriteCompressedInt(_writeStream, delta);
        }

        _totalSize += sizeInBytes;
        _totalRefs += children.Count;
    }

    public void SealForReading()
    {
        if (_data == null)
        {
            _data = _writeStream.ToArray();
        }
    }

    public Node GetNode(int nodeIndex)
    {
        if (_data == null)
        {
            throw new InvalidOperationException("Call SealForReading before reading nodes.");
        }

        return new Node(this, nodeIndex);
    }

    internal long GetNodeOffset(int nodeIndex) => _nodeOffsets[nodeIndex];
    internal byte[] Data => _data ?? Array.Empty<byte>();
    internal int GetTypeSize(int typeIndex) => _typeSizes[typeIndex];
    internal TypeInfo GetTypeInfo(int typeIndex) => _types[typeIndex];

    internal static int ReadCompressedInt(byte[] data, ref int offset)
    {
        int ret = 0;
        byte b = data[offset++];
        ret = b << 25 >> 25;
        if ((b & 0x80) == 0)
        {
            return ret;
        }

        ret <<= 7;
        b = data[offset++];
        ret += (b & 0x7f);
        if ((b & 0x80) == 0)
        {
            return ret;
        }

        ret <<= 7;
        b = data[offset++];
        ret += (b & 0x7f);
        if ((b & 0x80) == 0)
        {
            return ret;
        }

        ret <<= 7;
        b = data[offset++];
        ret += (b & 0x7f);
        if ((b & 0x80) == 0)
        {
            return ret;
        }

        ret <<= 7;
        b = data[offset++];
        ret += b;
        return ret;
    }

    internal static void WriteCompressedInt(Stream stream, int value)
    {
        if (value << 25 >> 25 == value)
        {
            goto oneByte;
        }

        if (value << 18 >> 18 == value)
        {
            goto twoBytes;
        }

        if (value << 11 >> 11 == value)
        {
            goto threeBytes;
        }

        if (value << 4 >> 4 == value)
        {
            goto fourBytes;
        }

        stream.WriteByte((byte)((value >> 28) | 0x80));
    fourBytes:
        stream.WriteByte((byte)((value >> 21) | 0x80));
    threeBytes:
        stream.WriteByte((byte)((value >> 14) | 0x80));
    twoBytes:
        stream.WriteByte((byte)((value >> 7) | 0x80));
    oneByte:
        stream.WriteByte((byte)(value & 0x7F));
    }
}

internal sealed class Node
{
    private readonly StreamingMemoryGraph _graph;
    private readonly int _index;
    private int _cursorOffset;
    private int _childrenLeft;

    public Node(StreamingMemoryGraph graph, int nodeIndex)
    {
        _graph = graph;
        _index = nodeIndex;
    }

    public int Index => _index;

    public int TypeIndex
    {
        get
        {
            int offset = (int)_graph.GetNodeOffset(_index);
            int typeAndSize = StreamingMemoryGraph.ReadCompressedInt(_graph.Data, ref offset);
            return typeAndSize >> 1;
        }
    }

    public int Size
    {
        get
        {
            int offset = (int)_graph.GetNodeOffset(_index);
            int typeAndSize = StreamingMemoryGraph.ReadCompressedInt(_graph.Data, ref offset);
            if ((typeAndSize & 1) != 0)
            {
                return StreamingMemoryGraph.ReadCompressedInt(_graph.Data, ref offset);
            }

            int typeIndex = typeAndSize >> 1;
            return _graph.GetTypeSize(typeIndex);
        }
    }

    public int ChildCount
    {
        get
        {
            int offset = (int)_graph.GetNodeOffset(_index);
            int typeAndSize = StreamingMemoryGraph.ReadCompressedInt(_graph.Data, ref offset);
            if ((typeAndSize & 1) != 0)
            {
                _ = StreamingMemoryGraph.ReadCompressedInt(_graph.Data, ref offset);
            }

            return StreamingMemoryGraph.ReadCompressedInt(_graph.Data, ref offset);
        }
    }

    public void ResetChildrenEnumeration()
    {
        int offset = (int)_graph.GetNodeOffset(_index);
        int typeAndSize = StreamingMemoryGraph.ReadCompressedInt(_graph.Data, ref offset);
        if ((typeAndSize & 1) != 0)
        {
            _ = StreamingMemoryGraph.ReadCompressedInt(_graph.Data, ref offset);
        }

        _childrenLeft = StreamingMemoryGraph.ReadCompressedInt(_graph.Data, ref offset);
        _cursorOffset = offset;
    }

    public int GetFirstChildIndex()
    {
        ResetChildrenEnumeration();
        return GetNextChildIndex();
    }

    public int GetNextChildIndex()
    {
        if (_childrenLeft == 0)
        {
            return -1;
        }

        int offset = _cursorOffset;
        int delta = StreamingMemoryGraph.ReadCompressedInt(_graph.Data, ref offset);
        _cursorOffset = offset;
        _childrenLeft--;
        return _index + delta;
    }
}

internal readonly struct TypeInfo
{
    public TypeInfo(string name, string? moduleName)
    {
        Name = name;
        ModuleName = moduleName;
    }

    public string Name { get; }
    public string? ModuleName { get; }
}

internal sealed class StreamingMemoryGraphBuilder
{
    private readonly Dictionary<ulong, int> _nodeIndexByAddress;
    private readonly Dictionary<ClrType, int> _typeIndexByClrType;
    private readonly List<int> _allNodeIndices;
    private readonly List<int> _children;
    private readonly TextWriter _log;
    private readonly int _maxObjects;
    private readonly StreamingMemoryGraph _graph;

    private StreamingMemoryGraphBuilder(int expectedNodes, int maxObjects, TextWriter log)
    {
        _nodeIndexByAddress = new Dictionary<ulong, int>(expectedNodes);
        _typeIndexByClrType = new Dictionary<ClrType, int>();
        _allNodeIndices = new List<int>(expectedNodes);
        _children = new List<int>(128);
        _log = log;
        _maxObjects = maxObjects;

        _graph = new StreamingMemoryGraph(expectedNodes);
        _graph.RootIndex = _graph.CreateNode();
        _graph.CreateType("[ROOT]", null, 0);
    }

    public static StreamingMemoryGraph BuildFromRuntimes(IEnumerable<ClrRuntime> runtimes, int maxObjects, TextWriter log)
    {
        StreamingMemoryGraphBuilder builder = new StreamingMemoryGraphBuilder(expectedNodes: 1024, maxObjects: maxObjects, log: log);

        foreach (ClrRuntime runtime in runtimes)
        {
            builder.IndexObjects(runtime);
        }

        foreach (ClrRuntime runtime in runtimes)
        {
            builder.SetObjectNodes(runtime);
        }

        builder.SetRootNode(runtimes);

        builder._graph.SealForReading();
        return builder._graph;
    }

    private void IndexObjects(ClrRuntime runtime)
    {
        if (!runtime.Heap.CanWalkHeap)
        {
            _log.WriteLine("Heap not walkable for runtime {0}", runtime.ClrInfo.Version);
            return;
        }

        int objectCount = 0;
        foreach (ClrSegment segment in runtime.Heap.Segments.OrderBy(s => s.Start))
        {
            foreach (ClrObject obj in segment.EnumerateObjects())
            {
                if (obj.Type is null || !obj.IsValid)
                {
                    continue;
                }

                ulong address = obj.Address;
                if (_nodeIndexByAddress.ContainsKey(address))
                {
                    continue;
                }

                if (_nodeIndexByAddress.Count >= _maxObjects)
                {
                    _log.WriteLine("Reached max objects limit ({0:n0}).", _maxObjects);
                    return;
                }

                int nodeIndex = _graph.CreateNode();
                _nodeIndexByAddress[address] = nodeIndex;
                _allNodeIndices.Add(nodeIndex);

                objectCount++;
                if ((objectCount & 0xFFFFF) == 0)
                {
                    _log.WriteLine("Indexed {0:n0} objects...", objectCount);
                }
            }
        }

        _log.WriteLine("Indexed {0:n0} objects for runtime {1}.", objectCount, runtime.ClrInfo.Version);
    }

    private void SetObjectNodes(ClrRuntime runtime)
    {
        if (!runtime.Heap.CanWalkHeap)
        {
            return;
        }

        foreach (ClrSegment segment in runtime.Heap.Segments.OrderBy(s => s.Start))
        {
            foreach (ClrObject obj in segment.EnumerateObjects())
            {
                if (obj.Type is null || !obj.IsValid)
                {
                    continue;
                }

                if (!_nodeIndexByAddress.TryGetValue(obj.Address, out int nodeIndex))
                {
                    continue;
                }

                _children.Clear();
                foreach (ulong child in obj.EnumerateReferenceAddresses(carefully: true, considerDependantHandles: true))
                {
                    if (_nodeIndexByAddress.TryGetValue(child, out int childIndex))
                    {
                        _children.Add(childIndex);
                    }
                }

                int typeIndex = GetTypeIndex(obj.Type);
                int size = obj.Size > int.MaxValue ? int.MaxValue : (int)obj.Size;
                _graph.SetNode(nodeIndex, typeIndex, size, _children);
            }
        }
    }

    private void SetRootNode(IEnumerable<ClrRuntime> runtimes)
    {
        _children.Clear();
        foreach (ClrRuntime runtime in runtimes)
        {
            foreach (ClrRoot root in runtime.Heap.EnumerateRoots())
            {
                if (!root.Object.IsValid)
                {
                    continue;
                }

                if (_nodeIndexByAddress.TryGetValue(root.Object.Address, out int childIndex))
                {
                    _children.Add(childIndex);
                }
            }
        }

        _graph.SetNode(_graph.RootIndex, 0, 0, _children);
    }

    private int GetTypeIndex(ClrType type)
    {
        if (type == null)
        {
            return AddType("[UNKNOWN]", null, -1);
        }

        if (_typeIndexByClrType.TryGetValue(type, out int idx))
        {
            return idx;
        }

        string name = type.Name ?? "[UNKNOWN]";
        string? moduleName = type.Module?.Name;
        idx = AddType(name, moduleName, -1);
        _typeIndexByClrType[type] = idx;
        return idx;
    }

    private int AddType(string name, string? moduleName, int size)
    {
        return _graph.CreateType(name, moduleName, size);
    }
}
