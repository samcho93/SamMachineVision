namespace MVXTester.Core.Models;

public class NodeGraph
{
    private readonly List<INode> _nodes = new();
    private readonly List<IConnection> _connections = new();

    public IReadOnlyList<INode> Nodes => _nodes;
    public IReadOnlyList<IConnection> Connections => _connections;

    public event Action<INode>? NodeAdded;
    public event Action<INode>? NodeRemoved;
    public event Action<IConnection>? ConnectionAdded;
    public event Action<IConnection>? ConnectionRemoved;

    public void AddNode(INode node)
    {
        _nodes.Add(node);
        NodeAdded?.Invoke(node);
    }

    public void RemoveNode(INode node)
    {
        var connectionsToRemove = _connections
            .Where(c => c.Source.Owner == node || c.Target.Owner == node)
            .ToList();

        foreach (var conn in connectionsToRemove)
            RemoveConnection(conn);

        _nodes.Remove(node);
        NodeRemoved?.Invoke(node);
    }

    public IConnection? Connect(IOutputPort source, IInputPort target)
    {
        if (target.IsConnected)
        {
            var existing = _connections.FirstOrDefault(c => c.Target == target);
            if (existing != null)
                RemoveConnection(existing);
        }

        if (WouldCreateCycle(source.Owner, target.Owner))
            return null;

        var connection = Connection.TryConnect(source, target);
        if (connection == null)
            return null;

        _connections.Add(connection);
        MarkDirtyDownstream(target.Owner);
        ConnectionAdded?.Invoke(connection);
        return connection;
    }

    public void RemoveConnection(IConnection connection)
    {
        Connection.Disconnect(connection);
        _connections.Remove(connection);
        MarkDirtyDownstream(connection.Target.Owner);
        ConnectionRemoved?.Invoke(connection);
    }

    public void MarkDirtyDownstream(INode node)
    {
        node.IsDirty = true;
        var downstream = _connections
            .Where(c => c.Source.Owner == node)
            .Select(c => c.Target.Owner)
            .Distinct();

        foreach (var n in downstream)
            MarkDirtyDownstream(n);
    }

    public bool WouldCreateCycle(INode from, INode to)
    {
        if (from == to) return true;

        var visited = new HashSet<INode>();
        var queue = new Queue<INode>();
        queue.Enqueue(from);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!visited.Add(current)) continue;

            var upstreamNodes = _connections
                .Where(c => c.Target.Owner == current)
                .Select(c => c.Source.Owner);

            foreach (var upstream in upstreamNodes)
            {
                if (upstream == to) return true;
                queue.Enqueue(upstream);
            }
        }
        return false;
    }

    public void Clear()
    {
        var nodesToRemove = _nodes.ToList();
        foreach (var node in nodesToRemove)
            RemoveNode(node);
    }
}
