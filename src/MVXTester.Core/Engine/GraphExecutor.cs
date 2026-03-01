using MVXTester.Core.Models;

namespace MVXTester.Core.Engine;

public class GraphExecutor
{
    public TimeSpan LastExecutionTime { get; private set; }

    public void Execute(NodeGraph graph, bool forceAll = false, CancellationToken cancellationToken = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        var (nodes, conns) = graph.Snapshot();
        var order = TopologicalSort(nodes, conns);
        ExecuteNodes(order, forceAll, cancellationToken);

        sw.Stop();
        LastExecutionTime = sw.Elapsed;
    }

    public void ExecuteContinuous(NodeGraph graph, CancellationToken cancellationToken,
        Action? onFrameComplete = null, int targetFps = 30)
    {
        var delay = TimeSpan.FromMilliseconds(1000.0 / targetFps);
        ExecuteContinuousCore(graph, cancellationToken, onFrameComplete, delay);
    }

    private void ExecuteContinuousCore(NodeGraph graph, CancellationToken cancellationToken,
        Action? onFrameComplete, TimeSpan delay)
    {
        // Phase 0: Start background nodes
        var (nodes, conns) = graph.Snapshot();
        var backgroundNodes = nodes.OfType<IBackgroundNode>().ToList();
        foreach (var bg in backgroundNodes)
        {
            try { bg.StartBackground(cancellationToken); } catch { }
        }

        try
        {
        // Initial force execution
        var order = TopologicalSort(nodes, conns);
        ExecuteNodes(order, true, cancellationToken);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        LastExecutionTime = sw.Elapsed;
        onFrameComplete?.Invoke();

        while (!cancellationToken.IsCancellationRequested)
        {
            sw.Restart();

            // Re-sort every frame to pick up newly added/connected nodes
            try
            {
                var (n, c) = graph.Snapshot();
                order = TopologicalSort(n, c);
            }
            catch { continue; } // skip frame if graph is temporarily invalid (e.g. mid-edit cycle)

            foreach (var node in order)
            {
                if (node is IStreamingSource)
                {
                    node.IsDirty = true;
                    graph.MarkDirtyDownstream(node);
                }
            }

            ExecuteNodes(order, false, cancellationToken);

            sw.Stop();
            LastExecutionTime = sw.Elapsed;
            onFrameComplete?.Invoke();

            var remaining = delay - sw.Elapsed;
            if (remaining > TimeSpan.Zero)
            {
                try { Task.Delay(remaining, cancellationToken).Wait(); }
                catch { break; }
            }
        }
        }
        finally
        {
            // Stop background nodes
            foreach (var bg in backgroundNodes)
            {
                try { bg.StopBackground(); } catch { }
            }
        }
    }

    public void ExecuteRuntime(NodeGraph graph, CancellationToken cancellationToken,
        Action? onFrameComplete = null, int pollIntervalMs = 16)
    {
        var nodesSnapshot = graph.Snapshot().Nodes;
        foreach (var n in nodesSnapshot) n.IsRuntimeMode = true;
        try
        {
            ExecuteRuntimeCore(graph, cancellationToken, onFrameComplete, pollIntervalMs);
        }
        finally
        {
            nodesSnapshot = graph.Snapshot().Nodes;
            foreach (var n in nodesSnapshot) n.IsRuntimeMode = false;
        }
    }

    private void ExecuteRuntimeCore(NodeGraph graph, CancellationToken cancellationToken,
        Action? onFrameComplete, int pollIntervalMs)
    {
        // Phase 1: Initial force execution of all nodes
        var (nodes, conns) = graph.Snapshot();
        var order = TopologicalSort(nodes, conns);
        var sw = System.Diagnostics.Stopwatch.StartNew();

        ExecuteNodes(order, true, cancellationToken);

        sw.Stop();
        LastExecutionTime = sw.Elapsed;
        onFrameComplete?.Invoke();

        // Phase 2: Reactive event loop - only re-execute when nodes become dirty
        while (!cancellationToken.IsCancellationRequested)
        {
            // Re-sort to pick up graph changes (new connections, etc.)
            try
            {
                var (n, c) = graph.Snapshot();
                order = TopologicalSort(n, c);
            }
            catch { continue; }

            // Also mark IStreamingSource nodes dirty so cameras/video work in runtime mode too
            foreach (var node in order)
            {
                if (node is IStreamingSource)
                {
                    node.IsDirty = true;
                    graph.MarkDirtyDownstream(node);
                }
            }

            // Scan for dirty nodes and propagate downstream
            bool anyDirty = false;
            foreach (var node in order)
            {
                if (node.IsDirty)
                {
                    anyDirty = true;
                    graph.MarkDirtyDownstream(node);
                }
            }

            if (anyDirty)
            {
                sw.Restart();

                ExecuteNodes(order, false, cancellationToken);

                sw.Stop();
                LastExecutionTime = sw.Elapsed;
                onFrameComplete?.Invoke();
            }
            else
            {
                // Nothing dirty: sleep briefly to avoid busy-waiting
                try { Task.Delay(pollIntervalMs, cancellationToken).Wait(); }
                catch { return; }
            }
        }
    }

    #region Loop Execution Support

    /// <summary>
    /// Execute nodes in order with ILoopNode support.
    /// When an ILoopNode is encountered, its downstream body nodes are
    /// executed repeatedly instead of once.
    /// </summary>
    private void ExecuteNodes(IReadOnlyList<INode> order, bool forceAll, CancellationToken ct)
    {
        var processedSet = new HashSet<INode>();
        ExecuteNodeList(order, forceAll, processedSet, ct);
    }

    /// <summary>
    /// Execute a list of nodes, handling ILoopNode nodes specially.
    /// </summary>
    private void ExecuteNodeList(IReadOnlyList<INode> order, bool forceAll,
        HashSet<INode> processedSet, CancellationToken ct)
    {
        for (int i = 0; i < order.Count; i++)
        {
            var node = order[i];
            if (processedSet.Contains(node)) continue;
            if (ct.IsCancellationRequested) return;

            if (node is ILoopNode loopNode && (forceAll || node.IsDirty))
            {
                ExecuteLoop(loopNode, order, processedSet, ct);
            }
            else if (forceAll || node.IsDirty)
            {
                try
                {
                    node.Error = null;
                    node.Process();
                    node.IsDirty = false;
                }
                catch (Exception ex)
                {
                    node.Error = ex.Message;
                }
            }
        }
    }

    /// <summary>
    /// Execute a loop node: initialize, iterate body, finalize.
    /// Supports nested loops (body may contain other ILoopNode nodes).
    /// </summary>
    private void ExecuteLoop(ILoopNode loopNode, IReadOnlyList<INode> fullOrder,
        HashSet<INode> outerProcessedSet, CancellationToken ct)
    {
        var loopAsNode = (INode)loopNode;

        // Find body nodes (downstream, stopping at collectors)
        var bodyNodes = FindLoopBody(loopAsNode, fullOrder);

        // Find collectors and break signals in the body
        var collectors = new List<ILoopCollector>();
        var breakSignals = new List<IBreakSignal>();
        foreach (var bn in bodyNodes)
        {
            if (bn is ILoopCollector coll) collectors.Add(coll);
            if (bn is IBreakSignal brk) breakSignals.Add(brk);
        }

        // Clear collectors before loop
        foreach (var c in collectors) c.ClearCollection();

        // Initialize loop
        loopAsNode.Error = null;
        try
        {
            loopNode.InitializeLoop();
        }
        catch (Exception ex)
        {
            loopAsNode.Error = ex.Message;
            loopAsNode.IsDirty = false;
            outerProcessedSet.Add(loopAsNode);
            foreach (var bn in bodyNodes) outerProcessedSet.Add(bn);
            return;
        }

        // Iterate
        int iteration = 0;
        while (iteration < loopNode.MaxIterations)
        {
            if (ct.IsCancellationRequested) return;

            bool hasNext;
            try
            {
                hasNext = loopNode.MoveNext();
            }
            catch (Exception ex)
            {
                loopAsNode.Error = ex.Message;
                break;
            }

            if (!hasNext) break;

            // Reset break signals for this iteration
            foreach (var bs in breakSignals) bs.ResetBreak();

            // Execute body nodes (supports nested loops via recursion)
            var bodyProcessed = new HashSet<INode>();
            ExecuteNodeList(bodyNodes, true, bodyProcessed, ct);

            // Collect iteration results
            foreach (var c in collectors) c.CollectIteration();

            // Check break signals
            if (breakSignals.Any(bs => bs.ShouldBreak))
                break;

            iteration++;
        }

        // Finalize
        try
        {
            loopNode.EndLoop();
        }
        catch (Exception ex)
        {
            loopAsNode.Error = ex.Message;
        }

        foreach (var c in collectors) c.FinalizeCollection();

        // Mark all body nodes as processed so they're skipped in the outer traversal
        loopAsNode.IsDirty = false;
        outerProcessedSet.Add(loopAsNode);
        foreach (var bn in bodyNodes)
        {
            bn.IsDirty = false;
            outerProcessedSet.Add(bn);
        }
    }

    /// <summary>
    /// Find loop body nodes: all nodes downstream of the loop node,
    /// stopping traversal at ILoopCollector nodes (collectors are included in the body).
    /// Returns nodes in the same order as they appear in the full order list.
    /// </summary>
    private static List<INode> FindLoopBody(INode loopNode, IReadOnlyList<INode> order)
    {
        var body = new HashSet<INode>();
        var visited = new HashSet<INode>();
        var queue = new Queue<INode>();

        // Start BFS from loop node's direct downstream connections
        foreach (var output in loopNode.Outputs)
        {
            foreach (var conn in output.Connections)
            {
                var target = conn.Target.Owner;
                if (visited.Add(target))
                {
                    body.Add(target);
                    queue.Enqueue(target);
                }
            }
        }

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();

            // Stop traversal at collectors (they are a boundary)
            if (current is ILoopCollector) continue;

            foreach (var output in current.Outputs)
            {
                foreach (var conn in output.Connections)
                {
                    var target = conn.Target.Owner;
                    if (visited.Add(target))
                    {
                        body.Add(target);
                        queue.Enqueue(target);
                    }
                }
            }
        }

        // Return body nodes in topological order (same order as the full order list)
        var orderSet = new HashSet<INode>(order);
        return order.Where(n => body.Contains(n) && orderSet.Contains(n)).ToList();
    }

    #endregion

    public static List<INode> TopologicalSort(IReadOnlyList<INode> nodes, IReadOnlyList<IConnection> connections)
    {
        var inDegree = new Dictionary<INode, int>();
        var adjacency = new Dictionary<INode, List<INode>>();

        foreach (var node in nodes)
        {
            inDegree[node] = 0;
            adjacency[node] = new List<INode>();
        }

        foreach (var conn in connections)
        {
            var src = conn.Source.Owner;
            var tgt = conn.Target.Owner;
            if (adjacency.ContainsKey(src) && inDegree.ContainsKey(tgt))
            {
                adjacency[src].Add(tgt);
                inDegree[tgt]++;
            }
        }

        var queue = new Queue<INode>(inDegree.Where(kv => kv.Value == 0).Select(kv => kv.Key));
        var result = new List<INode>();

        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            result.Add(node);
            foreach (var neighbor in adjacency[node])
            {
                inDegree[neighbor]--;
                if (inDegree[neighbor] == 0)
                    queue.Enqueue(neighbor);
            }
        }

        if (result.Count != nodes.Count)
            throw new InvalidOperationException("Graph contains a cycle.");

        return result;
    }
}
