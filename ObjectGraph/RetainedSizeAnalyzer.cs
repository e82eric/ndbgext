using System;
using System.Collections.Generic;

namespace ObjectGraph;

public static class RetainedSizeAnalyzer
{
    public static RetainedSizeResult Compute(ObjectGraph graph, DominatorTree dominatorTree)
    {
        long[] retainedBytesByObject = ComputeRetainedBytesByObject(graph, dominatorTree);
        long[] retainedCountByObject = ComputeRetainedCountsByObject(graph, dominatorTree);
        List<TypeSummary> typeSummaries = CreateTypeSummaries(graph);

        AggregateRetainedByObject(graph, retainedBytesByObject, retainedCountByObject, typeSummaries);
        AggregateMinimumRetainedByType(graph, dominatorTree, typeSummaries);

        return new RetainedSizeResult(retainedBytesByObject, retainedCountByObject, typeSummaries);
    }

    private static long[] ComputeRetainedBytesByObject(ObjectGraph graph, DominatorTree dominatorTree)
    {
        var retained = new long[graph.NodeCount];
        ComputeRetainedMetric(graph, dominatorTree, retained, useSize: true);
        return retained;
    }

    private static long[] ComputeRetainedCountsByObject(ObjectGraph graph, DominatorTree dominatorTree)
    {
        var retained = new long[graph.NodeCount];
        ComputeRetainedMetric(graph, dominatorTree, retained, useSize: false);
        return retained;
    }

    private static void ComputeRetainedMetric(ObjectGraph graph, DominatorTree dominatorTree, long[] retained, bool useSize)
    {
        int rootId = dominatorTree.RootId;
        var stack = new Stack<TraversalState>();
        stack.Push(new TraversalState(rootId, false));

        while (stack.Count > 0)
        {
            TraversalState state = stack.Pop();
            if (!state.PostOrder)
            {
                stack.Push(new TraversalState(state.NodeId, true));
                ReadOnlySpan<int> children = dominatorTree.GetChildren(state.NodeId);
                for (int i = 0; i < children.Length; i++)
                {
                    stack.Push(new TraversalState(children[i], false));
                }
            }
            else
            {
                long value = state.NodeId == rootId ? 0 : (useSize ? graph.GetSize(state.NodeId) : 1);
                ReadOnlySpan<int> children = dominatorTree.GetChildren(state.NodeId);
                for (int i = 0; i < children.Length; i++)
                {
                    value += retained[children[i]];
                }

                retained[state.NodeId] = value;
            }
        }
    }

    private static List<TypeSummary> CreateTypeSummaries(ObjectGraph graph)
    {
        var summaries = new List<TypeSummary>(graph.Types.Count);
        for (int i = 0; i < graph.Types.Count; i++)
        {
            TypeInfo type = graph.Types[i];
            summaries.Add(new TypeSummary(type.Id, type.Name, type.FullName, type.ModuleName, type.IsSynthetic)
            {
                ExclusiveBytes = type.ExclusiveBytes,
                ExclusiveCount = type.ExclusiveCount
            });
        }

        return summaries;
    }

    private static void AggregateRetainedByObject(
        ObjectGraph graph,
        long[] retainedBytesByObject,
        long[] retainedCountByObject,
        List<TypeSummary> typeSummaries)
    {
        for (int i = 0; i < graph.NodeCount; i++)
        {
            TypeSummary summary = typeSummaries[graph.GetTypeId(i)];
            summary.RetainedBytes += retainedBytesByObject[i];
            summary.RetainedCount += retainedCountByObject[i];
        }
    }

    private static void AggregateMinimumRetainedByType(
        ObjectGraph graph,
        DominatorTree dominatorTree,
        List<TypeSummary> typeSummaries)
    {
        int reachableCount = dominatorTree.NodeByDfsOrder.Length;
        var sizePrefix = new long[reachableCount + 1];
        var countPrefix = new long[reachableCount + 1];
        var intervalsByType = new List<Interval>[typeSummaries.Count];

        for (int order = 0; order < reachableCount; order++)
        {
            int nodeId = dominatorTree.NodeByDfsOrder[order];
            long size = nodeId == dominatorTree.RootId ? 0 : graph.GetSize(nodeId);
            long count = nodeId == dominatorTree.RootId ? 0 : 1;
            sizePrefix[order + 1] = sizePrefix[order] + size;
            countPrefix[order + 1] = countPrefix[order] + count;

            int typeId = graph.GetTypeId(nodeId);
            (intervalsByType[typeId] ??= new List<Interval>()).Add(new Interval(dominatorTree.DfsIn[nodeId], dominatorTree.DfsOut[nodeId]));
        }

        for (int typeId = 0; typeId < intervalsByType.Length; typeId++)
        {
            List<Interval> intervals = intervalsByType[typeId];
            if (intervals == null || intervals.Count == 0)
            {
                continue;
            }

            intervals.Sort((left, right) =>
            {
                int startComparison = left.Start.CompareTo(right.Start);
                return startComparison != 0 ? startComparison : left.End.CompareTo(right.End);
            });

            int mergedStart = intervals[0].Start;
            int mergedEnd = intervals[0].End;
            long bytes = 0;
            long count = 0;

            for (int i = 1; i < intervals.Count; i++)
            {
                Interval interval = intervals[i];
                if (interval.Start <= mergedEnd + 1)
                {
                    if (interval.End > mergedEnd)
                    {
                        mergedEnd = interval.End;
                    }
                }
                else
                {
                    bytes += sizePrefix[mergedEnd + 1] - sizePrefix[mergedStart];
                    count += countPrefix[mergedEnd + 1] - countPrefix[mergedStart];
                    mergedStart = interval.Start;
                    mergedEnd = interval.End;
                }
            }

            bytes += sizePrefix[mergedEnd + 1] - sizePrefix[mergedStart];
            count += countPrefix[mergedEnd + 1] - countPrefix[mergedStart];

            typeSummaries[typeId].MinimumRetainedBytes = bytes;
            typeSummaries[typeId].MinimumRetainedCount = count;
        }
    }

    private readonly struct TraversalState
    {
        public TraversalState(int nodeId, bool postOrder)
        {
            NodeId = nodeId;
            PostOrder = postOrder;
        }

        public int NodeId { get; }
        public bool PostOrder { get; }
    }

    private readonly struct Interval
    {
        public Interval(int start, int end)
        {
            Start = start;
            End = end;
        }

        public int Start { get; }
        public int End { get; }
    }
}
