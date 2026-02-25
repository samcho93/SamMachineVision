using System.Windows;
using MVXTester.App.ViewModels;
using MVXTester.App.UndoRedo;

namespace MVXTester.App.Services;

public class PasteResult
{
    public List<NodeViewModel> CreatedNodes { get; } = new();
    public List<(string SrcId, string SrcPort, string TgtId, string TgtPort)> DeferredConnections { get; } = new();
}

public class ClipboardService
{
    private List<NodeSnapshot>? _nodeSnapshots;
    private List<ConnectionSnapshot>? _connectionSnapshots;

    public bool HasData => _nodeSnapshots != null && _nodeSnapshots.Count > 0;

    public void Copy(EditorViewModel editor, List<NodeViewModel> nodes)
    {
        _nodeSnapshots = nodes.Select(NodeSnapshot.FromViewModel).ToList();

        var nodeIds = new HashSet<string>(nodes.Select(n => n.Model.Id));
        _connectionSnapshots = editor.Connections
            .Where(c => nodeIds.Contains(c.Source.Node.Model.Id) && nodeIds.Contains(c.Target.Node.Model.Id))
            .Select(c => new ConnectionSnapshot
            {
                SourceNodeId = c.Source.Node.Model.Id,
                SourcePortName = c.Source.Name,
                TargetNodeId = c.Target.Node.Model.Id,
                TargetPortName = c.Target.Name
            })
            .ToList();
    }

    public PasteResult Paste(EditorViewModel editor)
    {
        var result = new PasteResult();
        if (_nodeSnapshots == null) return result;

        var idMap = new Dictionary<string, string>();
        var offset = new Point(40, 40);

        foreach (var snap in _nodeSnapshots)
        {
            var newPos = new Point(snap.Position.X + offset.X, snap.Position.Y + offset.Y);
            var vm = editor.AddNodeInternal(snap.NodeType, newPos, null);

            foreach (var kvp in snap.Properties)
            {
                var prop = vm.Model.Properties.FirstOrDefault(p => p.Name == kvp.Key);
                prop?.SetValue(kvp.Value);
            }

            idMap[snap.Id] = vm.Model.Id;
            result.CreatedNodes.Add(vm);
        }

        if (_connectionSnapshots != null)
        {
            foreach (var conn in _connectionSnapshots)
            {
                if (idMap.TryGetValue(conn.SourceNodeId, out var newSrcId) &&
                    idMap.TryGetValue(conn.TargetNodeId, out var newTgtId))
                {
                    result.DeferredConnections.Add((newSrcId, conn.SourcePortName, newTgtId, conn.TargetPortName));
                }
            }
        }

        return result;
    }
}
