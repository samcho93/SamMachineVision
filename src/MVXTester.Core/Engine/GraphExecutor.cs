using MVXTester.Core.Models;

namespace MVXTester.Core.Engine;

public class GraphExecutor
{
    public TimeSpan LastExecutionTime { get; private set; }

    public void Execute(NodeGraph graph, bool forceAll = false, CancellationToken cancellationToken = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        var order = TopologicalSort(graph.Nodes, graph.Connections);
        foreach (var node in order)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (forceAll || node.IsDirty)
            {
                try
                {
                    node.Error = null;
                    node.Process();
                    node.IsDirty = false;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    node.Error = ex.Message;
                }
            }
        }

        sw.Stop();
        LastExecutionTime = sw.Elapsed;
    }

    public void ExecuteContinuous(NodeGraph graph, CancellationToken cancellationToken,
        Action? onFrameComplete = null, int targetFps = 30)
    {
        var order = TopologicalSort(graph.Nodes, graph.Connections);
        var delay = TimeSpan.FromMilliseconds(1000.0 / targetFps);

        // Initial force execution
        foreach (var node in order)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                node.Error = null;
                node.Process();
                node.IsDirty = false;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { node.Error = ex.Message; }
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        LastExecutionTime = sw.Elapsed;
        onFrameComplete?.Invoke();

        while (!cancellationToken.IsCancellationRequested)
        {
            sw.Restart();

            foreach (var node in order)
            {
                if (node is IStreamingSource)
                {
                    node.IsDirty = true;
                    graph.MarkDirtyDownstream(node);
                }
            }

            foreach (var node in order)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (node.IsDirty)
                {
                    try
                    {
                        node.Error = null;
                        node.Process();
                        node.IsDirty = false;
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex) { node.Error = ex.Message; }
                }
            }

            sw.Stop();
            LastExecutionTime = sw.Elapsed;
            onFrameComplete?.Invoke();

            var remaining = delay - sw.Elapsed;
            if (remaining > TimeSpan.Zero)
                Thread.Sleep(remaining);
        }
    }

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
