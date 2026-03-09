using System;
using System.Collections.Generic;

namespace ObjectGraph;

public sealed class ObjectGraph
{
    private readonly int[] _typeIds;
    private readonly int[] _sizes;
    private readonly int[] _childOffsets;
    private readonly int[] _childCounts;
    private readonly byte[] _childData;
    private readonly int[] _parentOffsets;
    private readonly int[] _parentCounts;
    private readonly byte[] _parentData;

    internal ObjectGraph(
        int rootId,
        int[] typeIds,
        int[] sizes,
        int[] childOffsets,
        int[] childCounts,
        byte[] childData,
        int[] parentOffsets,
        int[] parentCounts,
        byte[] parentData,
        List<TypeInfo> types)
    {
        RootId = rootId;
        _typeIds = typeIds;
        _sizes = sizes;
        _childOffsets = childOffsets;
        _childCounts = childCounts;
        _childData = childData;
        _parentOffsets = parentOffsets;
        _parentCounts = parentCounts;
        _parentData = parentData;
        Types = types;
    }

    public int RootId { get; }
    public int NodeCount => _typeIds.Length;
    public IReadOnlyList<TypeInfo> Types { get; }
    public int GetTypeId(int nodeId) => _typeIds[nodeId];
    public int GetSize(int nodeId) => _sizes[nodeId];
    public int GetChildCount(int nodeId) => _childCounts[nodeId];
    public int GetParentCount(int nodeId) => _parentCounts[nodeId];
    public EdgeEnumerable EnumerateChildren(int nodeId) => new EdgeEnumerable(_childData, _childOffsets[nodeId], _childCounts[nodeId], nodeId);
    public EdgeEnumerable EnumerateParents(int nodeId) => new EdgeEnumerable(_parentData, _parentOffsets[nodeId], _parentCounts[nodeId], nodeId);

    public readonly struct EdgeEnumerable
    {
        private readonly byte[] _data;
        private readonly int _offset;
        private readonly int _count;
        private readonly int _baseNodeId;

        internal EdgeEnumerable(byte[] data, int offset, int count, int baseNodeId)
        {
            _data = data;
            _offset = offset;
            _count = count;
            _baseNodeId = baseNodeId;
        }

        public EdgeEnumerator GetEnumerator() => new EdgeEnumerator(_data, _offset, _count, _baseNodeId);
    }

    public struct EdgeEnumerator
    {
        private readonly byte[] _data;
        private readonly int _baseNodeId;
        private int _offset;
        private int _remaining;

        internal EdgeEnumerator(byte[] data, int offset, int count, int baseNodeId)
        {
            _data = data;
            _baseNodeId = baseNodeId;
            _offset = offset;
            _remaining = count;
            Current = -1;
        }

        public int Current { get; private set; }

        public bool MoveNext()
        {
            if (_remaining == 0)
            {
                return false;
            }

            int delta = ReadCompressedInt(_data, ref _offset);
            Current = _baseNodeId + delta;
            _remaining--;
            return true;
        }
    }

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

    internal static void WriteCompressedInt(System.IO.Stream stream, int value)
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
