using Microsoft.Diagnostics.Runtime;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace ObjectGraph;

public static class DumpObjectGraphBuilder
{
    public static ObjectGraph BuildFromDump(IEnumerable<ClrRuntime> runtimes, TextWriter log = null)
    {
        if (runtimes == null)
        {
            throw new ArgumentNullException(nameof(runtimes));
        }

        log ??= TextWriter.Null;
        ClrRuntime[] runtimeArray = runtimes.Where(runtime => runtime != null).ToArray();
        if (runtimeArray.Length == 0)
        {
            throw new ArgumentException("At least one runtime is required.", nameof(runtimes));
        }

        var addresses = new List<ulong>();
        var typeIds = new List<int>();
        var sizes = new List<int>();
        var types = new List<TypeInfo>();
        var addressToNodeId = new Dictionary<ulong, int>(1_000_000);
        var typeIdsByKey = new Dictionary<TypeKey, int>();
        var sourceEdges = new List<int>(1_000_000);
        var targetEdges = new List<int>(1_000_000);
        var stopwatch = Stopwatch.StartNew();

        int rootTypeId = GetOrCreateSyntheticTypeId("[GC Roots]", typeIdsByKey, types);
        int rootId = CreateNode(0, rootTypeId, 0, addresses, typeIds, sizes, addressToNodeId);
        var segments = runtimeArray.SelectMany(runtime => runtime.Heap.Segments).OrderBy(segment => segment.Start).ToArray();

        log.WriteLine("{0,5:n1}s: Starting object graph build", stopwatch.Elapsed.TotalSeconds);
        long objectCount = 0;
        long objectBytes = 0;
        var uniqueChildren = new HashSet<int>();
        foreach (ClrSegment segment in segments)
        {
            foreach (ClrObject obj in segment.EnumerateObjects())
            {
                if (obj.Type is null)
                {
                    continue;
                }

                int nodeId = GetOrCreateNodeId(obj.Address, addresses, typeIds, sizes, addressToNodeId);
                if (typeIds[nodeId] < 0)
                {
                    int typeId = GetOrCreateTypeId(obj.Type, typeIdsByKey, types);
                    int size = checked((int)obj.Size);
                    typeIds[nodeId] = typeId;
                    sizes[nodeId] = size;
                    types[typeId].ExclusiveBytes += size;
                    types[typeId].ExclusiveCount++;
                    objectCount++;
                    objectBytes += size;
                }

                uniqueChildren.Clear();
                foreach (ulong childAddress in obj.EnumerateReferenceAddresses(carefully: true, considerDependantHandles: true))
                {
                    if (childAddress == 0 || !IsInAnySegment(segments, childAddress))
                    {
                        continue;
                    }

                    int childId = GetOrCreateNodeId(childAddress, addresses, typeIds, sizes, addressToNodeId);
                    if (!uniqueChildren.Add(childId))
                    {
                        continue;
                    }

                    sourceEdges.Add(nodeId);
                    targetEdges.Add(childId);
                }

                if ((objectCount % 1_000_000) == 0)
                {
                    log.WriteLine("{0,5:n1}s: Scanned {1:n0} objects, size {2:n1} MB, types {3:n0}", stopwatch.Elapsed.TotalSeconds, objectCount, objectBytes / 1_000_000.0, types.Count);
                }
            }
        }

        log.WriteLine("{0,5:n1}s: Finished object scan. Objects={1:n0} Size={2:n1} MB Types={3:n0}", stopwatch.Elapsed.TotalSeconds, objectCount, objectBytes / 1_000_000.0, types.Count);

        int unknownTypeId = GetOrCreateSyntheticTypeId("[Unknown]", typeIdsByKey, types);
        for (int i = 0; i < typeIds.Count; i++)
        {
            if (typeIds[i] < 0)
            {
                typeIds[i] = unknownTypeId;
                sizes[i] = 0;
                types[unknownTypeId].ExclusiveCount++;
            }
        }

        log.WriteLine("{0,5:n1}s: Adding synthetic root edges", stopwatch.Elapsed.TotalSeconds);
        AddRootEdges(runtimeArray, rootId, addressToNodeId, sourceEdges, targetEdges, log);

        PackedEdges packedEdges = PackEdges(typeIds.Count, sourceEdges, targetEdges);
        log.WriteLine("{0,5:n1}s: Object graph ready. NodeCount={1:n0} EdgeCount={2:n0}", stopwatch.Elapsed.TotalSeconds, typeIds.Count, sourceEdges.Count);

        return new ObjectGraph(rootId, addresses.ToArray(), typeIds.ToArray(), sizes.ToArray(), packedEdges.ChildStarts, packedEdges.ChildCounts, packedEdges.Children, packedEdges.ParentStarts, packedEdges.ParentCounts, packedEdges.Parents, types);
    }

    private static int CreateNode(ulong address, int typeId, int size, List<ulong> addresses, List<int> typeIds, List<int> sizes, Dictionary<ulong, int> addressToNodeId)
    {
        int nodeId = addresses.Count;
        addresses.Add(address);
        typeIds.Add(typeId);
        sizes.Add(size);
        addressToNodeId[address] = nodeId;
        return nodeId;
    }

    private static int GetOrCreateNodeId(ulong address, List<ulong> addresses, List<int> typeIds, List<int> sizes, Dictionary<ulong, int> addressToNodeId)
    {
        if (addressToNodeId.TryGetValue(address, out int nodeId))
        {
            return nodeId;
        }

        return CreateNode(address, -1, 0, addresses, typeIds, sizes, addressToNodeId);
    }

    private static bool IsInAnySegment(ClrSegment[] segments, ulong address)
    {
        int lo = 0;
        int hi = segments.Length - 1;
        while (lo <= hi)
        {
            int mid = lo + ((hi - lo) / 2);
            ClrSegment segment = segments[mid];
            if (address < segment.Start)
            {
                hi = mid - 1;
            }
            else if (address >= segment.End)
            {
                lo = mid + 1;
            }
            else
            {
                return true;
            }
        }

        return false;
    }

    private static void AddRootEdges(ClrRuntime[] runtimes, int rootId, Dictionary<ulong, int> addressToNodeId, List<int> sourceEdges, List<int> targetEdges, TextWriter log)
    {
        var rootedNodes = new HashSet<int>();
        try
        {
            foreach (ClrModule module in runtimes.SelectMany(runtime => runtime.EnumerateModules()))
            {
                ClrRuntime runtime = module.AppDomain.Runtime;
                foreach (var item in module.EnumerateTypeDefToMethodTableMap())
                {
                    ClrType type = runtime.GetTypeByMethodTable(item.MethodTable);
                    if (type is null)
                    {
                        continue;
                    }

                    foreach (ClrStaticField field in type.StaticFields.Where(staticField => staticField.IsObjectReference))
                    {
                        foreach (ClrAppDomain domain in runtime.AppDomains)
                        {
                            ClrObject obj = field.ReadObject(domain);
                            AddRootEdge(rootId, obj.Address, addressToNodeId, rootedNodes, sourceEdges, targetEdges);
                        }
                    }
                }
            }

            foreach (ClrRoot root in runtimes.SelectMany(runtime => runtime.Heap.EnumerateRoots()))
            {
                if (!root.Object.IsValid)
                {
                    continue;
                }

                AddRootEdge(rootId, root.Object.Address, addressToNodeId, rootedNodes, sourceEdges, targetEdges);
            }
        }
        catch (Exception ex) when (!(ex is OutOfMemoryException))
        {
            log.WriteLine("[ERROR while processing roots: {0}]", ex.Message);
            log.WriteLine("Continuing with partial root information.");
        }
    }

    private static void AddRootEdge(int rootId, ulong address, Dictionary<ulong, int> addressToNodeId, HashSet<int> rootedNodes, List<int> sourceEdges, List<int> targetEdges)
    {
        if (address == 0 || !addressToNodeId.TryGetValue(address, out int nodeId) || !rootedNodes.Add(nodeId))
        {
            return;
        }

        sourceEdges.Add(rootId);
        targetEdges.Add(nodeId);
    }

    private static PackedEdges PackEdges(int nodeCount, List<int> sourceEdges, List<int> targetEdges)
    {
        int edgeCount = sourceEdges.Count;
        int[] childCounts = new int[nodeCount];
        int[] parentCounts = new int[nodeCount];
        for (int i = 0; i < edgeCount; i++)
        {
            childCounts[sourceEdges[i]]++;
            parentCounts[targetEdges[i]]++;
        }

        int[] childStarts = PrefixSum(childCounts);
        int[] parentStarts = PrefixSum(parentCounts);
        int[] children = new int[edgeCount];
        int[] parents = new int[edgeCount];
        int[] childCursor = (int[])childStarts.Clone();
        int[] parentCursor = (int[])parentStarts.Clone();

        for (int i = 0; i < edgeCount; i++)
        {
            int source = sourceEdges[i];
            int target = targetEdges[i];
            children[childCursor[source]++] = target;
            parents[parentCursor[target]++] = source;
        }

        return new PackedEdges(childStarts, childCounts, children, parentStarts, parentCounts, parents);
    }

    private static int[] PrefixSum(int[] counts)
    {
        int[] starts = new int[counts.Length];
        int next = 0;
        for (int i = 0; i < counts.Length; i++)
        {
            starts[i] = next;
            next += counts[i];
        }

        return starts;
    }

    private static int GetOrCreateTypeId(ClrType type, Dictionary<TypeKey, int> typeIdsByKey, List<TypeInfo> types)
    {
        string typeName = type.Name ?? string.Empty;
        string moduleName = type.Module?.Name;
        string fullName = moduleName == null ? typeName : $"{moduleName}!{typeName}";
        var key = new TypeKey(fullName, typeName, moduleName, false);
        if (!typeIdsByKey.TryGetValue(key, out int typeId))
        {
            typeId = types.Count;
            typeIdsByKey.Add(key, typeId);
            types.Add(new TypeInfo(typeId, key.Name, key.FullName, key.ModuleName, false));
        }

        return typeId;
    }

    private static int GetOrCreateSyntheticTypeId(string fullName, Dictionary<TypeKey, int> typeIdsByKey, List<TypeInfo> types)
    {
        var key = new TypeKey(fullName, fullName, null, true);
        if (!typeIdsByKey.TryGetValue(key, out int typeId))
        {
            typeId = types.Count;
            typeIdsByKey.Add(key, typeId);
            types.Add(new TypeInfo(typeId, key.Name, key.FullName, key.ModuleName, true));
        }

        return typeId;
    }

    private readonly struct PackedEdges
    {
        public PackedEdges(int[] childStarts, int[] childCounts, int[] children, int[] parentStarts, int[] parentCounts, int[] parents)
        {
            ChildStarts = childStarts;
            ChildCounts = childCounts;
            Children = children;
            ParentStarts = parentStarts;
            ParentCounts = parentCounts;
            Parents = parents;
        }

        public int[] ChildStarts { get; }
        public int[] ChildCounts { get; }
        public int[] Children { get; }
        public int[] ParentStarts { get; }
        public int[] ParentCounts { get; }
        public int[] Parents { get; }
    }

    private readonly struct TypeKey : IEquatable<TypeKey>
    {
        public TypeKey(string fullName, string name, string moduleName, bool isSynthetic)
        {
            FullName = fullName ?? string.Empty;
            Name = name ?? string.Empty;
            ModuleName = moduleName;
            IsSynthetic = isSynthetic;
        }

        public string FullName { get; }
        public string Name { get; }
        public string ModuleName { get; }
        public bool IsSynthetic { get; }

        public bool Equals(TypeKey other) =>
            IsSynthetic == other.IsSynthetic &&
            string.Equals(FullName, other.FullName, StringComparison.Ordinal) &&
            string.Equals(ModuleName, other.ModuleName, StringComparison.Ordinal);

        public override bool Equals(object obj) => obj is TypeKey other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = (hash * 31) + (FullName?.GetHashCode() ?? 0);
                hash = (hash * 31) + (ModuleName?.GetHashCode() ?? 0);
                hash = (hash * 31) + IsSynthetic.GetHashCode();
                return hash;
            }
        }
    }
}
