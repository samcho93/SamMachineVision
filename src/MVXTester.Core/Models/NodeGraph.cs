namespace MVXTester.Core.Models;

public class NodeGraph
{
    private readonly List<INode> _nodes = new();
    private readonly List<IConnection> _connections = new();
    private readonly object _syncLock = new();

    public IReadOnlyList<INode> Nodes => _nodes;
    public IReadOnlyList<IConnection> Connections => _connections;

    public event Action<INode>? NodeAdded;
    public event Action<INode>? NodeRemoved;
    public event Action<IConnection>? ConnectionAdded;
    public event Action<IConnection>? ConnectionRemoved;

    /// <summary>
    /// 노드·연결 리스트의 스레드 안전 스냅샷을 반환합니다.
    /// 백그라운드 실행 스레드에서 사용합니다.
    /// </summary>
    public (INode[] Nodes, IConnection[] Connections) Snapshot()
    {
        lock (_syncLock)
        {
            return (_nodes.ToArray(), _connections.ToArray());
        }
    }

    public void AddNode(INode node)
    {
        lock (_syncLock)
        {
            _nodes.Add(node);
        }
        NodeAdded?.Invoke(node);
    }

    public void RemoveNode(INode node)
    {
        List<IConnection> connectionsToRemove;
        lock (_syncLock)
        {
            connectionsToRemove = _connections
                .Where(c => c.Source.Owner == node || c.Target.Owner == node)
                .ToList();
        }

        foreach (var conn in connectionsToRemove)
            RemoveConnection(conn);

        lock (_syncLock)
        {
            _nodes.Remove(node);
        }
        NodeRemoved?.Invoke(node);
    }

    public IConnection? Connect(IOutputPort source, IInputPort target)
    {
        if (target.IsConnected)
        {
            IConnection? existing;
            lock (_syncLock)
            {
                existing = _connections.FirstOrDefault(c => c.Target == target);
            }
            if (existing != null)
                RemoveConnection(existing);
        }

        if (WouldCreateCycle(source.Owner, target.Owner))
            return null;

        var connection = Connection.TryConnect(source, target);
        if (connection == null)
            return null;

        lock (_syncLock)
        {
            _connections.Add(connection);
        }
        MarkDirtyDownstream(target.Owner);
        ConnectionAdded?.Invoke(connection);
        return connection;
    }

    public void RemoveConnection(IConnection connection)
    {
        Connection.Disconnect(connection);
        lock (_syncLock)
        {
            _connections.Remove(connection);
        }
        MarkDirtyDownstream(connection.Target.Owner);
        ConnectionRemoved?.Invoke(connection);
    }

    public void MarkDirtyDownstream(INode node)
    {
        node.IsDirty = true;
        IConnection[] snapshot;
        lock (_syncLock)
        {
            snapshot = _connections.ToArray();
        }
        var downstream = snapshot
            .Where(c => c.Source.Owner == node)
            .Select(c => c.Target.Owner)
            .Distinct()
            .ToList();

        foreach (var n in downstream)
            MarkDirtyDownstream(n);
    }

    public bool WouldCreateCycle(INode from, INode to)
    {
        if (from == to) return true;

        var visited = new HashSet<INode>();
        var queue = new Queue<INode>();
        queue.Enqueue(from);
        IConnection[] connsSnapshot;
        lock (_syncLock)
        {
            connsSnapshot = _connections.ToArray();
        }

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!visited.Add(current)) continue;

            var upstreamNodes = connsSnapshot
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
        List<INode> nodesToRemove;
        lock (_syncLock)
        {
            nodesToRemove = _nodes.ToList();
        }
        foreach (var node in nodesToRemove)
            RemoveNode(node);
    }
}
