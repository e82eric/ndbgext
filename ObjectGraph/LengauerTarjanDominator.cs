using System.Collections.Generic;

namespace ObjectGraph;

public static class LengauerTarjanDominator
{
    public static DominatorTree Compute(ObjectGraph graph, int rootId)
    {
        int nodeCount = graph.Nodes.Count;
        var dfsNumberByNode = new int[nodeCount];
        var nodeByDfs = new int[nodeCount + 1];
        var parent = new int[nodeCount + 1];
        var semi = new int[nodeCount + 1];
        var idom = new int[nodeCount + 1];
        var ancestor = new int[nodeCount + 1];
        var label = new int[nodeCount + 1];
        var buckets = new List<int>[nodeCount + 1];
        int dfsCount = DepthFirstSearch(graph, rootId, dfsNumberByNode, nodeByDfs, parent, semi, label);

        for (int i = dfsCount; i >= 2; i--)
        {
            int w = i;
            int wNodeId = nodeByDfs[w];
            for (int predecessorIndex = 0; predecessorIndex < graph.Nodes[wNodeId].Parents.Count; predecessorIndex++)
            {
                int predecessorNodeId = graph.Nodes[wNodeId].Parents[predecessorIndex];
                int predecessorDfs = dfsNumberByNode[predecessorNodeId];
                if (predecessorDfs == 0)
                {
                    continue;
                }

                int u = Eval(predecessorDfs, ancestor, label, semi);
                if (semi[u] < semi[w])
                {
                    semi[w] = semi[u];
                }
            }

            (buckets[semi[w]] ??= new List<int>()).Add(w);
            Link(parent[w], w, ancestor);

            List<int> parentBucket = buckets[parent[w]];
            if (parentBucket == null)
            {
                continue;
            }

            for (int j = 0; j < parentBucket.Count; j++)
            {
                int v = parentBucket[j];
                int u = Eval(v, ancestor, label, semi);
                idom[v] = semi[u] < semi[v] ? u : parent[w];
            }

            parentBucket.Clear();
        }

        var immediateDominator = new int[nodeCount];
        for (int i = 0; i < immediateDominator.Length; i++)
        {
            immediateDominator[i] = -1;
        }

        immediateDominator[rootId] = rootId;
        for (int i = 2; i <= dfsCount; i++)
        {
            if (idom[i] != semi[i])
            {
                idom[i] = idom[idom[i]];
            }

            immediateDominator[nodeByDfs[i]] = nodeByDfs[idom[i]];
        }

        var treeChildren = new List<int>[nodeCount];
        var reachable = new bool[nodeCount];
        for (int i = 1; i <= dfsCount; i++)
        {
            int nodeId = nodeByDfs[i];
            reachable[nodeId] = true;
            if (nodeId == rootId)
            {
                continue;
            }

            int parentNodeId = immediateDominator[nodeId];
            (treeChildren[parentNodeId] ??= new List<int>()).Add(nodeId);
        }

        var dfsIn = new int[nodeCount];
        var dfsOut = new int[nodeCount];
        for (int i = 0; i < nodeCount; i++)
        {
            dfsIn[i] = -1;
            dfsOut[i] = -1;
        }

        var nodeByDfsOrder = new int[dfsCount];
        AssignDominatorTreeIntervals(rootId, treeChildren, dfsIn, dfsOut, nodeByDfsOrder);

        return new DominatorTree(rootId, immediateDominator, treeChildren, dfsIn, dfsOut, nodeByDfsOrder, reachable);
    }

    private static int DepthFirstSearch(
        ObjectGraph graph,
        int rootId,
        int[] dfsNumberByNode,
        int[] nodeByDfs,
        int[] parent,
        int[] semi,
        int[] label)
    {
        int dfsCount = 0;
        var stack = new Stack<TraversalState>();
        stack.Push(new TraversalState(rootId, 0, 0));

        while (stack.Count > 0)
        {
            TraversalState state = stack.Pop();
            if (state.NextChildIndex == 0)
            {
                if (dfsNumberByNode[state.NodeId] != 0)
                {
                    continue;
                }

                dfsCount++;
                dfsNumberByNode[state.NodeId] = dfsCount;
                nodeByDfs[dfsCount] = state.NodeId;
                parent[dfsCount] = state.ParentDfs;
                semi[dfsCount] = dfsCount;
                label[dfsCount] = dfsCount;
            }

            List<int> children = graph.Nodes[state.NodeId].Children;
            if (state.NextChildIndex < children.Count)
            {
                int childNodeId = children[state.NextChildIndex];
                stack.Push(new TraversalState(state.NodeId, state.ParentDfs, state.NextChildIndex + 1));
                if (dfsNumberByNode[childNodeId] == 0)
                {
                    stack.Push(new TraversalState(childNodeId, dfsNumberByNode[state.NodeId], 0));
                }
            }
        }

        return dfsCount;
    }

    private static void AssignDominatorTreeIntervals(
        int rootId,
        List<int>[] treeChildren,
        int[] dfsIn,
        int[] dfsOut,
        int[] nodeByDfsOrder)
    {
        int next = 0;
        var stack = new Stack<TraversalState>();
        stack.Push(new TraversalState(rootId, 0, 0));

        while (stack.Count > 0)
        {
            TraversalState state = stack.Pop();
            if (state.NextChildIndex == 0)
            {
                dfsIn[state.NodeId] = next;
                nodeByDfsOrder[next] = state.NodeId;
                next++;
            }

            List<int> children = treeChildren[state.NodeId];
            if (children != null && state.NextChildIndex < children.Count)
            {
                stack.Push(new TraversalState(state.NodeId, 0, state.NextChildIndex + 1));
                stack.Push(new TraversalState(children[state.NextChildIndex], 0, 0));
            }
            else
            {
                dfsOut[state.NodeId] = next - 1;
            }
        }
    }

    private static void Link(int parent, int child, int[] ancestor)
    {
        ancestor[child] = parent;
    }

    private static int Eval(int vertex, int[] ancestor, int[] label, int[] semi)
    {
        if (ancestor[vertex] == 0)
        {
            return label[vertex];
        }

        Compress(vertex, ancestor, label, semi);
        return label[vertex];
    }

    private static void Compress(int vertex, int[] ancestor, int[] label, int[] semi)
    {
        if (ancestor[ancestor[vertex]] == 0)
        {
            return;
        }

        Compress(ancestor[vertex], ancestor, label, semi);
        if (semi[label[ancestor[vertex]]] < semi[label[vertex]])
        {
            label[vertex] = label[ancestor[vertex]];
        }

        ancestor[vertex] = ancestor[ancestor[vertex]];
    }

    private readonly struct TraversalState
    {
        public TraversalState(int nodeId, int parentDfs, int nextChildIndex)
        {
            NodeId = nodeId;
            ParentDfs = parentDfs;
            NextChildIndex = nextChildIndex;
        }

        public int NodeId { get; }
        public int ParentDfs { get; }
        public int NextChildIndex { get; }
    }
}
