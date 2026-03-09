using System.Collections.Generic;

namespace ObjectGraph;

public sealed class ObjectGraph
{
    internal ObjectGraph(int rootId, List<ObjectNode> nodes, List<TypeInfo> types)
    {
        RootId = rootId;
        Nodes = nodes;
        Types = types;
    }

    public int RootId { get; }
    public IReadOnlyList<ObjectNode> Nodes { get; }
    public IReadOnlyList<TypeInfo> Types { get; }
}

public sealed class ObjectNode
{
    internal ObjectNode(int id, ulong address, int typeId, int size)
    {
        Id = id;
        Address = address;
        TypeId = typeId;
        Size = size;
        Children = new List<int>();
        Parents = new List<int>();
    }

    public int Id { get; }
    public ulong Address { get; internal set; }
    public int TypeId { get; internal set; }
    public int Size { get; internal set; }
    public List<int> Children { get; }
    public List<int> Parents { get; }
}

public sealed class TypeInfo
{
    internal TypeInfo(int id, string name, string fullName, string moduleName, bool isSynthetic)
    {
        Id = id;
        Name = name;
        FullName = fullName;
        ModuleName = moduleName;
        IsSynthetic = isSynthetic;
    }

    public int Id { get; }
    public string Name { get; }
    public string FullName { get; }
    public string ModuleName { get; }
    public bool IsSynthetic { get; }
    public long ExclusiveBytes { get; internal set; }
    public long ExclusiveCount { get; internal set; }
}

public sealed class DominatorTree
{
    internal DominatorTree(int rootId, int[] immediateDominator, List<int>[] children, int[] dfsIn, int[] dfsOut, int[] nodeByDfsOrder, bool[] reachable)
    {
        RootId = rootId;
        ImmediateDominator = immediateDominator;
        Children = children;
        DfsIn = dfsIn;
        DfsOut = dfsOut;
        NodeByDfsOrder = nodeByDfsOrder;
        Reachable = reachable;
    }

    public int RootId { get; }
    public int[] ImmediateDominator { get; }
    public List<int>[] Children { get; }
    public int[] DfsIn { get; }
    public int[] DfsOut { get; }
    public int[] NodeByDfsOrder { get; }
    public bool[] Reachable { get; }
}

public sealed class TypeSummary
{
    internal TypeSummary(int id, string name, string fullName, string moduleName, bool isSynthetic)
    {
        Id = id;
        Name = name;
        FullName = fullName;
        ModuleName = moduleName;
        IsSynthetic = isSynthetic;
    }

    public int Id { get; }
    public string Name { get; }
    public string FullName { get; }
    public string ModuleName { get; }
    public bool IsSynthetic { get; }
    public long ExclusiveBytes { get; internal set; }
    public long ExclusiveCount { get; internal set; }
    public long RetainedBytes { get; internal set; }
    public long RetainedCount { get; internal set; }
    public long MinimumRetainedBytes { get; internal set; }
    public long MinimumRetainedCount { get; internal set; }
}

public sealed class RetainedSizeResult
{
    internal RetainedSizeResult(long[] retainedBytesByObject, long[] retainedCountByObject, List<TypeSummary> types)
    {
        RetainedBytesByObject = retainedBytesByObject;
        RetainedCountByObject = retainedCountByObject;
        Types = types;
    }

    public long[] RetainedBytesByObject { get; }
    public long[] RetainedCountByObject { get; }
    public IReadOnlyList<TypeSummary> Types { get; }
}
