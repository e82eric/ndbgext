using System;
using System.Collections.Generic;

namespace ObjectGraph;

public sealed class ObjectGraph
{
    private readonly ulong[] _addresses;
    private readonly int[] _typeIds;
    private readonly int[] _sizes;
    private readonly int[] _childStarts;
    private readonly int[] _childCounts;
    private readonly int[] _children;
    private readonly int[] _parentStarts;
    private readonly int[] _parentCounts;
    private readonly int[] _parents;

    internal ObjectGraph(
        int rootId,
        ulong[] addresses,
        int[] typeIds,
        int[] sizes,
        int[] childStarts,
        int[] childCounts,
        int[] children,
        int[] parentStarts,
        int[] parentCounts,
        int[] parents,
        List<TypeInfo> types)
    {
        RootId = rootId;
        _addresses = addresses;
        _typeIds = typeIds;
        _sizes = sizes;
        _childStarts = childStarts;
        _childCounts = childCounts;
        _children = children;
        _parentStarts = parentStarts;
        _parentCounts = parentCounts;
        _parents = parents;
        Types = types;
    }

    public int RootId { get; }
    public int NodeCount => _typeIds.Length;
    public IReadOnlyList<TypeInfo> Types { get; }
    public ulong GetAddress(int nodeId) => _addresses[nodeId];
    public int GetTypeId(int nodeId) => _typeIds[nodeId];
    public int GetSize(int nodeId) => _sizes[nodeId];
    public int GetChildCount(int nodeId) => _childCounts[nodeId];
    public int GetParentCount(int nodeId) => _parentCounts[nodeId];
    public ReadOnlySpan<int> GetChildren(int nodeId) => new ReadOnlySpan<int>(_children, _childStarts[nodeId], _childCounts[nodeId]);
    public ReadOnlySpan<int> GetParents(int nodeId) => new ReadOnlySpan<int>(_parents, _parentStarts[nodeId], _parentCounts[nodeId]);
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
    private readonly int[] _childStarts;
    private readonly int[] _childCounts;
    private readonly int[] _children;

    internal DominatorTree(
        int rootId,
        int[] immediateDominator,
        int[] childStarts,
        int[] childCounts,
        int[] children,
        int[] dfsIn,
        int[] dfsOut,
        int[] nodeByDfsOrder,
        bool[] reachable)
    {
        RootId = rootId;
        ImmediateDominator = immediateDominator;
        _childStarts = childStarts;
        _childCounts = childCounts;
        _children = children;
        DfsIn = dfsIn;
        DfsOut = dfsOut;
        NodeByDfsOrder = nodeByDfsOrder;
        Reachable = reachable;
    }

    public int RootId { get; }
    public int[] ImmediateDominator { get; }
    public int[] DfsIn { get; }
    public int[] DfsOut { get; }
    public int[] NodeByDfsOrder { get; }
    public bool[] Reachable { get; }
    public ReadOnlySpan<int> GetChildren(int nodeId) => new ReadOnlySpan<int>(_children, _childStarts[nodeId], _childCounts[nodeId]);
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
