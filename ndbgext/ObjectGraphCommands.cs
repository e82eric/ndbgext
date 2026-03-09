using DbgEngExtension;
using System.Linq;

namespace ndbgext;

internal sealed class ObjectGraphCache
{
    private static ObjectGraph.ObjectGraph? _graph;
    private static ObjectGraph.DominatorTree? _dominatorTree;
    private static ObjectGraph.RetainedSizeResult? _retainedSizeResult;

    public static ObjectGraph.ObjectGraph? Graph => _graph;
    public static ObjectGraph.DominatorTree? DominatorTree => _dominatorTree;
    public static ObjectGraph.RetainedSizeResult? RetainedSizeResult => _retainedSizeResult;

    public static void Set(
        ObjectGraph.ObjectGraph graph,
        ObjectGraph.DominatorTree dominatorTree,
        ObjectGraph.RetainedSizeResult retainedSizeResult)
    {
        _graph = graph;
        _dominatorTree = dominatorTree;
        _retainedSizeResult = retainedSizeResult;
    }
}

internal static class ObjectGraphCommandFormatting
{
    public static string FormatTypeName(ObjectGraph.TypeInfo type)
    {
        if (!string.IsNullOrWhiteSpace(type.ModuleName))
        {
            return $"{Path.GetFileNameWithoutExtension(type.ModuleName)}!{type.Name}";
        }

        return string.IsNullOrWhiteSpace(type.FullName) ? type.Name : type.FullName;
    }

    public static string FormatTypeName(ObjectGraph.TypeSummary type)
    {
        if (!string.IsNullOrWhiteSpace(type.ModuleName))
        {
            return $"{Path.GetFileNameWithoutExtension(type.ModuleName)}!{type.Name}";
        }

        return string.IsNullOrWhiteSpace(type.FullName) ? type.Name : type.FullName;
    }
}

public sealed class BuildObjectGraphCommand : DbgEngCommand
{
    public BuildObjectGraphCommand(nint pUnknown, bool redirectConsoleOutput = true)
        : base(pUnknown, redirectConsoleOutput)
    {
    }

    internal void Run(string args)
    {
        if (!string.IsNullOrWhiteSpace(args))
        {
            Console.Error.WriteLine("Usage: buildobjectgraph");
            return;
        }

        if (Runtimes.Length == 0)
        {
            Console.Error.WriteLine("No CLR runtimes found in target.");
            return;
        }

        ObjectGraph.ObjectGraph graph = ObjectGraph.DumpObjectGraphBuilder.BuildFromDump(Runtimes, Console.Out);
        ObjectGraph.DominatorTree dominatorTree = ObjectGraph.LengauerTarjanDominator.Compute(graph, graph.RootId);
        ObjectGraph.RetainedSizeResult retainedSize = ObjectGraph.RetainedSizeAnalyzer.Compute(graph, dominatorTree);

        ObjectGraphCache.Set(graph, dominatorTree, retainedSize);

        long edgeCount = 0;
        long totalBytes = 0;
        for (int i = 0; i < graph.Nodes.Count; i++)
        {
            edgeCount += graph.Nodes[i].Children.Count;
            totalBytes += graph.Nodes[i].Size;
        }

        Console.WriteLine("ObjectGraph built.");
        Console.WriteLine("Nodes: {0:n0}", graph.Nodes.Count);
        Console.WriteLine("Edges: {0:n0}", edgeCount);
        Console.WriteLine("Types: {0:n0}", graph.Types.Count);
        Console.WriteLine("Total Size (bytes): {0:n0}", totalBytes);
        Console.WriteLine("Retained summaries: {0:n0}", retainedSize.Types.Count);
    }
}

public sealed class RetainedByteStatCommand : DbgEngCommand
{
    private const long DefaultMinimumRetainedBytes = 1024 * 1024;

    public RetainedByteStatCommand(nint pUnknown, bool redirectConsoleOutput = true)
        : base(pUnknown, redirectConsoleOutput)
    {
    }

    internal void Run(string args)
    {
        if (ObjectGraphCache.Graph == null || ObjectGraphCache.RetainedSizeResult == null)
        {
            Console.Error.WriteLine("No object graph cached. Run buildobjectgraph first.");
            return;
        }

        if (!TryParseMinimumRetainedBytes(args, out long minimumRetainedBytes))
        {
            return;
        }

        var ordered = ObjectGraphCache.RetainedSizeResult.Types
            .Where(type => type.MinimumRetainedBytes >= minimumRetainedBytes)
            .OrderByDescending(type => type.MinimumRetainedBytes)
            .ThenByDescending(type => type.ExclusiveBytes)
            .ThenBy(type => type.FullName, StringComparer.Ordinal);

        Console.WriteLine("{0,16} {1,16} {2}", "Min Retained", "Bytes", "Type");
        foreach (ObjectGraph.TypeSummary type in ordered)
        {
            Console.WriteLine(
                "{0,16:n0} {1,16:n0} {2}",
                type.MinimumRetainedBytes,
                type.ExclusiveBytes,
                ObjectGraphCommandFormatting.FormatTypeName(type));
        }
    }

    private static bool TryParseMinimumRetainedBytes(string args, out long minimumRetainedBytes)
    {
        minimumRetainedBytes = DefaultMinimumRetainedBytes;
        if (string.IsNullOrWhiteSpace(args))
        {
            return true;
        }

        string[] parts = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < parts.Length; i++)
        {
            string arg = parts[i];
            if (arg.StartsWith("--min-retained-bytes=", StringComparison.OrdinalIgnoreCase))
            {
                return TryParsePositiveLong(arg.Substring("--min-retained-bytes=".Length), out minimumRetainedBytes);
            }

            if (arg.Equals("--min-retained-bytes", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= parts.Length)
                {
                    Console.Error.WriteLine("Missing value for --min-retained-bytes");
                    return false;
                }

                return TryParsePositiveLong(parts[i + 1], out minimumRetainedBytes);
            }

            Console.Error.WriteLine("Usage: retainedbytestat [--min-retained-bytes N]");
            return false;
        }

        return true;
    }

    private static bool TryParsePositiveLong(string value, out long parsed)
    {
        if (!long.TryParse(value, out parsed) || parsed < 0)
        {
            Console.Error.WriteLine("Invalid --min-retained-bytes value: {0}", value);
            return false;
        }

        return true;
    }

}

public sealed class ReferredFromCommand : DbgEngCommand
{
    private const int DefaultTopCount = 10;

    public ReferredFromCommand(nint pUnknown, bool redirectConsoleOutput = true)
        : base(pUnknown, redirectConsoleOutput)
    {
    }

    internal void Run(string args)
    {
        if (ObjectGraphCache.Graph == null || ObjectGraphCache.DominatorTree == null)
        {
            Console.Error.WriteLine("No object graph cached. Run buildobjectgraph first.");
            return;
        }

        if (!TryParseOptions(args, out string? typeFilter, out int topCount))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(typeFilter))
        {
            Console.Error.WriteLine("Usage: referredfrom <TypeName> [--top N]");
            return;
        }

        ObjectGraph.ObjectGraph graph = ObjectGraphCache.Graph;
        ObjectGraph.DominatorTree dominatorTree = ObjectGraphCache.DominatorTree;
        HashSet<int> matchingTypes = FindMatchingTypes(graph, typeFilter);
        if (matchingTypes.Count == 0)
        {
            Console.WriteLine("Type filter '{0}' did not match any types.", typeFilter);
            return;
        }

        var matches = new List<int>();
        for (int i = 0; i < graph.Nodes.Count; i++)
        {
            ObjectGraph.ObjectNode node = graph.Nodes[i];
            if (matchingTypes.Contains(node.TypeId) && dominatorTree.Reachable[i])
            {
                matches.Add(i);
            }
        }

        if (matches.Count == 0)
        {
            Console.WriteLine("No reachable nodes found for type filter '{0}'.", typeFilter);
            return;
        }

        var parentStats = new Dictionary<int, (long ReferencedBytes, long InstanceCount)>();
        foreach (int nodeId in matches)
        {
            ObjectGraph.ObjectNode node = graph.Nodes[nodeId];
            int size = node.Size;
            for (int i = 0; i < node.Parents.Count; i++)
            {
                int parentId = node.Parents[i];
                int parentTypeId = graph.Nodes[parentId].TypeId;
                parentStats.TryGetValue(parentTypeId, out var current);
                current.ReferencedBytes += size;
                current.InstanceCount++;
                parentStats[parentTypeId] = current;
            }
        }

        var ordered = parentStats
            .Select(kvp => (TypeId: kvp.Key, ReferencedBytes: kvp.Value.ReferencedBytes, Count: kvp.Value.InstanceCount))
            .OrderByDescending(entry => entry.ReferencedBytes)
            .ThenByDescending(entry => entry.Count)
            .Take(topCount);

        Console.WriteLine("Top direct parent types for '{0}' (top {1}):", typeFilter, topCount);
        Console.WriteLine("{0,16} {1,14}  {2}", "Bytes", "Count", "ParentType");
        foreach (var entry in ordered)
        {
            Console.WriteLine(
                "{0,16:n0} {1,14:n0}  {2}",
                entry.ReferencedBytes,
                entry.Count,
                ObjectGraphCommandFormatting.FormatTypeName(graph.Types[entry.TypeId]));
        }
    }

    private static bool TryParseOptions(string args, out string? typeFilter, out int topCount)
    {
        typeFilter = null;
        topCount = DefaultTopCount;

        if (string.IsNullOrWhiteSpace(args))
        {
            return true;
        }

        string[] parts = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < parts.Length; i++)
        {
            string arg = parts[i];
            if (TryReadIntArg(arg, parts, ref i, "--top", out int parsedTop))
            {
                topCount = parsedTop;
                continue;
            }

            if (arg.StartsWith("-", StringComparison.OrdinalIgnoreCase) || arg.StartsWith("/", StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine("Usage: referredfrom <TypeName> [--top N]");
                return false;
            }

            if (typeFilter == null)
            {
                typeFilter = arg;
                continue;
            }

            Console.Error.WriteLine("Unexpected argument: {0}", arg);
            return false;
        }

        return true;
    }

    private static bool TryReadIntArg(string arg, string[] parts, ref int i, string name, out int value)
    {
        value = 0;
        if (arg.StartsWith(name + "=", StringComparison.OrdinalIgnoreCase))
        {
            return TryParsePositiveInt(arg.Substring(name.Length + 1), name, out value);
        }

        if (arg.Equals(name, StringComparison.OrdinalIgnoreCase))
        {
            if (i + 1 >= parts.Length)
            {
                Console.Error.WriteLine("Missing value for {0}", name);
                return false;
            }

            i++;
            return TryParsePositiveInt(parts[i], name, out value);
        }

        return false;
    }

    private static bool TryParsePositiveInt(string value, string name, out int parsed)
    {
        parsed = 0;
        if (!int.TryParse(value, out int temp) || temp <= 0)
        {
            Console.Error.WriteLine("Invalid {0} value: {1}", name, value);
            return false;
        }

        parsed = temp;
        return true;
    }

    internal static HashSet<int> FindMatchingTypes(ObjectGraph.ObjectGraph graph, string filter)
    {
        var matches = new HashSet<int>();
        for (int i = 0; i < graph.Types.Count; i++)
        {
            ObjectGraph.TypeInfo type = graph.Types[i];
            string fullName = type.FullName ?? type.Name ?? string.Empty;
            if (string.Equals(fullName, filter, StringComparison.OrdinalIgnoreCase))
            {
                matches.Add(i);
            }
        }

        if (matches.Count > 0)
        {
            return matches;
        }

        for (int i = 0; i < graph.Types.Count; i++)
        {
            ObjectGraph.TypeInfo type = graph.Types[i];
            string fullName = type.FullName ?? type.Name ?? string.Empty;
            if (fullName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                matches.Add(i);
            }
        }

        return matches;
    }
}

public sealed class RefferedToTreeCommand : DbgEngCommand
{
    private const int DefaultLevels = 3;

    public RefferedToTreeCommand(nint pUnknown, bool redirectConsoleOutput = true)
        : base(pUnknown, redirectConsoleOutput)
    {
    }

    internal void Run(string args)
    {
        if (ObjectGraphCache.Graph == null || ObjectGraphCache.DominatorTree == null)
        {
            Console.Error.WriteLine("No object graph cached. Run buildobjectgraph first.");
            return;
        }

        if (!TryParseOptions(args, out string? typeFilter, out int levels))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(typeFilter))
        {
            Console.Error.WriteLine("Usage: refferedtotree <TypeName> [--levels N]");
            return;
        }

        ObjectGraph.ObjectGraph graph = ObjectGraphCache.Graph;
        ObjectGraph.DominatorTree dominatorTree = ObjectGraphCache.DominatorTree;
        HashSet<int> matchingTypes = ReferredFromCommand.FindMatchingTypes(graph, typeFilter);
        if (matchingTypes.Count == 0)
        {
            Console.WriteLine("Type filter '{0}' did not match any types.", typeFilter);
            return;
        }

        List<int> currentLevel = new();
        for (int i = 0; i < graph.Nodes.Count; i++)
        {
            ObjectGraph.ObjectNode node = graph.Nodes[i];
            if (matchingTypes.Contains(node.TypeId) && dominatorTree.Reachable[i])
            {
                currentLevel.Add(i);
            }
        }

        if (currentLevel.Count == 0)
        {
            Console.WriteLine("No reachable nodes found for type filter '{0}'.", typeFilter);
            return;
        }

        Console.WriteLine("Referred-to tree for '{0}' ({1:n0} starting nodes, {2} levels):", typeFilter, currentLevel.Count, levels);
        Console.WriteLine("{0,16} {1,12}  {2}", "Bytes", "Refs", "Type");
        Console.WriteLine("{0,16} {1,12}  {2}", "-", "-", typeFilter);
        PrintReferenceTree(graph, dominatorTree, currentLevel, levels, 1, new HashSet<int>(currentLevel));
    }

    private static void PrintReferenceTree(
        ObjectGraph.ObjectGraph graph,
        ObjectGraph.DominatorTree dominatorTree,
        IReadOnlyCollection<int> currentNodes,
        int remainingLevels,
        int depth,
        HashSet<int> pathVisited)
    {
        if (remainingLevels <= 0 || currentNodes.Count == 0)
        {
            return;
        }

        Dictionary<int, ChildAggregate> aggregates = BuildChildAggregates(graph, dominatorTree, currentNodes, pathVisited);
        foreach ((int typeId, ChildAggregate aggregate) in aggregates
                     .OrderByDescending(entry => entry.Value.Bytes)
                     .ThenByDescending(entry => entry.Value.Count)
                     .ThenBy(entry => graph.Types[entry.Key].FullName, StringComparer.Ordinal))
        {
            string indent = new string(' ', depth * 2);
            Console.WriteLine(
                "{0,16:n0} {1,12:n0}  {2}{3}",
                aggregate.Bytes,
                aggregate.Count,
                indent,
                ObjectGraphCommandFormatting.FormatTypeName(graph.Types[typeId]));

            if (remainingLevels == 1 || aggregate.NodeIds.Count == 0)
            {
                continue;
            }

            HashSet<int> nextVisited = new HashSet<int>(pathVisited);
            foreach (int nodeId in aggregate.NodeIds)
            {
                nextVisited.Add(nodeId);
            }

            PrintReferenceTree(graph, dominatorTree, aggregate.NodeIds, remainingLevels - 1, depth + 1, nextVisited);
        }
    }

    private static Dictionary<int, ChildAggregate> BuildChildAggregates(
        ObjectGraph.ObjectGraph graph,
        ObjectGraph.DominatorTree dominatorTree,
        IReadOnlyCollection<int> currentNodes,
        HashSet<int> pathVisited)
    {
        var aggregates = new Dictionary<int, ChildAggregate>();
        foreach (int nodeId in currentNodes)
        {
            ObjectGraph.ObjectNode node = graph.Nodes[nodeId];
            for (int i = 0; i < node.Children.Count; i++)
            {
                int childId = node.Children[i];
                if (!dominatorTree.Reachable[childId] || pathVisited.Contains(childId))
                {
                    continue;
                }

                ObjectGraph.ObjectNode child = graph.Nodes[childId];
                if (!aggregates.TryGetValue(child.TypeId, out ChildAggregate aggregate))
                {
                    aggregate = new ChildAggregate();
                    aggregates.Add(child.TypeId, aggregate);
                }

                aggregate.Bytes += child.Size;
                aggregate.Count++;
                aggregate.NodeIds.Add(childId);
            }
        }

        return aggregates;
    }

    private static bool TryParseOptions(string args, out string? typeFilter, out int levels)
    {
        typeFilter = null;
        levels = DefaultLevels;

        if (string.IsNullOrWhiteSpace(args))
        {
            return true;
        }

        string[] parts = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < parts.Length; i++)
        {
            string arg = parts[i];
            if (TryReadIntArg(arg, parts, ref i, "--levels", out int parsedLevels))
            {
                levels = parsedLevels;
                continue;
            }

            if (arg.StartsWith("-", StringComparison.OrdinalIgnoreCase) || arg.StartsWith("/", StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine("Usage: refferedtotree <TypeName> [--levels N]");
                return false;
            }

            if (typeFilter == null)
            {
                typeFilter = arg;
                continue;
            }

            Console.Error.WriteLine("Unexpected argument: {0}", arg);
            return false;
        }

        return true;
    }

    private static bool TryReadIntArg(string arg, string[] parts, ref int i, string name, out int value)
    {
        value = 0;
        if (arg.StartsWith(name + "=", StringComparison.OrdinalIgnoreCase))
        {
            return TryParsePositiveInt(arg.Substring(name.Length + 1), name, out value);
        }

        if (arg.Equals(name, StringComparison.OrdinalIgnoreCase))
        {
            if (i + 1 >= parts.Length)
            {
                Console.Error.WriteLine("Missing value for {0}", name);
                return false;
            }

            i++;
            return TryParsePositiveInt(parts[i], name, out value);
        }

        return false;
    }

    private static bool TryParsePositiveInt(string value, string name, out int parsed)
    {
        parsed = 0;
        if (!int.TryParse(value, out int temp) || temp <= 0)
        {
            Console.Error.WriteLine("Invalid {0} value: {1}", name, value);
            return false;
        }

        parsed = temp;
        return true;
    }

    private sealed class ChildAggregate
    {
        public long Bytes { get; set; }
        public long Count { get; set; }
        public HashSet<int> NodeIds { get; } = new();
    }
}
