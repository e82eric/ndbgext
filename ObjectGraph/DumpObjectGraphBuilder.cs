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

        var nodes = new List<ObjectNode>();
        var types = new List<TypeInfo>();
        var addressToNodeId = new Dictionary<ulong, int>(1_000_000);
        var typeIdsByKey = new Dictionary<TypeKey, int>();
        var nodeInitialized = new List<bool>();
        var stopwatch = Stopwatch.StartNew();

        int rootTypeId = GetOrCreateSyntheticTypeId("[GC Roots]", typeIdsByKey, types);
        int rootId = nodes.Count;
        nodes.Add(new ObjectNode(rootId, 0, rootTypeId, 0));
        nodeInitialized.Add(true);

        var segments = runtimeArray.SelectMany(runtime => runtime.Heap.Segments).OrderBy(segment => segment.Start).ToArray();

        log.WriteLine("{0,5:n1}s: Starting object graph build", stopwatch.Elapsed.TotalSeconds);
        long objectCount = 0;
        long objectBytes = 0;
        var uniqueChildren = new HashSet<int>();
        long edgeCount = 0;
        foreach (ClrSegment segment in segments)
        {
            foreach (ClrObject obj in segment.EnumerateObjects())
            {
                if (obj.Type is null)
                {
                    continue;
                }

                int nodeId = GetOrCreateNodeId(obj.Address, nodes, addressToNodeId, nodeInitialized);
                if (!nodeInitialized[nodeId])
                {
                    int typeId = GetOrCreateTypeId(obj.Type, typeIdsByKey, types);
                    int size = checked((int)obj.Size);
                    nodes[nodeId].Address = obj.Address;
                    nodes[nodeId].TypeId = typeId;
                    nodes[nodeId].Size = size;
                    nodeInitialized[nodeId] = true;
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

                    int childId = GetOrCreateNodeId(childAddress, nodes, addressToNodeId, nodeInitialized);
                    if (!uniqueChildren.Add(childId))
                    {
                        continue;
                    }

                    nodes[nodeId].Children.Add(childId);
                    nodes[childId].Parents.Add(nodeId);
                    edgeCount++;
                }

                if ((objectCount % 1_000_000) == 0)
                {
                    log.WriteLine(
                        "{0,5:n1}s: Scanned {1:n0} objects, size {2:n1} MB, types {3:n0}",
                        stopwatch.Elapsed.TotalSeconds,
                        objectCount,
                        objectBytes / 1_000_000.0,
                        types.Count);
                }
            }
        }

        log.WriteLine(
            "{0,5:n1}s: Finished object scan. Objects={1:n0} Size={2:n1} MB Types={3:n0}",
            stopwatch.Elapsed.TotalSeconds,
            objectCount,
            objectBytes / 1_000_000.0,
            types.Count);

        int unknownTypeId = GetOrCreateSyntheticTypeId("[Unknown]", typeIdsByKey, types);
        for (int i = 0; i < nodeInitialized.Count; i++)
        {
            if (!nodeInitialized[i])
            {
                nodes[i].TypeId = unknownTypeId;
                nodes[i].Size = 0;
                types[unknownTypeId].ExclusiveCount++;
            }
        }

        log.WriteLine("{0,5:n1}s: Adding synthetic root edges", stopwatch.Elapsed.TotalSeconds);
        AddRootEdges(runtimeArray, rootId, nodes, addressToNodeId, log);
        log.WriteLine(
            "{0,5:n1}s: Object graph ready. NodeCount={1:n0} EdgeCount={2:n0}",
            stopwatch.Elapsed.TotalSeconds,
            nodes.Count,
            edgeCount + nodes[rootId].Children.Count);

        return new ObjectGraph(rootId, nodes, types);
    }

    private static int GetOrCreateNodeId(
        ulong address,
        List<ObjectNode> nodes,
        Dictionary<ulong, int> addressToNodeId,
        List<bool> nodeInitialized)
    {
        if (addressToNodeId.TryGetValue(address, out int nodeId))
        {
            return nodeId;
        }

        nodeId = nodes.Count;
        addressToNodeId[address] = nodeId;
        nodes.Add(new ObjectNode(nodeId, address, -1, 0));
        nodeInitialized.Add(false);
        return nodeId;
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

    private static void AddRootEdges(
        ClrRuntime[] runtimes,
        int rootId,
        List<ObjectNode> nodes,
        Dictionary<ulong, int> addressToNodeId,
        TextWriter log)
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
                            AddRootEdge(rootId, obj.Address, nodes, addressToNodeId, rootedNodes);
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

                AddRootEdge(rootId, root.Object.Address, nodes, addressToNodeId, rootedNodes);
            }
        }
        catch (Exception ex) when (!(ex is OutOfMemoryException))
        {
            log.WriteLine("[ERROR while processing roots: {0}]", ex.Message);
            log.WriteLine("Continuing with partial root information.");
        }
    }

    private static void AddRootEdge(
        int rootId,
        ulong address,
        List<ObjectNode> nodes,
        Dictionary<ulong, int> addressToNodeId,
        HashSet<int> rootedNodes)
    {
        if (address == 0 || !addressToNodeId.TryGetValue(address, out int nodeId) || !rootedNodes.Add(nodeId))
        {
            return;
        }

        nodes[rootId].Children.Add(nodeId);
        nodes[nodeId].Parents.Add(rootId);
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
