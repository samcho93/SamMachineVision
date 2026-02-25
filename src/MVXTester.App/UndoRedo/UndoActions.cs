using System.Windows;
using MVXTester.Core.UndoRedo;
using MVXTester.App.ViewModels;

namespace MVXTester.App.UndoRedo;

public class AddNodeAction : IUndoableAction
{
    private readonly EditorViewModel _editor;
    private readonly Type _nodeType;
    private readonly Point _position;
    private readonly Dictionary<string, object?>? _properties;
    private readonly string? _existingNodeId;

    public string Description => "Add Node";

    public AddNodeAction(EditorViewModel editor, Type nodeType, Point position,
        Dictionary<string, object?>? properties = null, string? existingNodeId = null)
    {
        _editor = editor;
        _nodeType = nodeType;
        _position = position;
        _properties = properties;
        _existingNodeId = existingNodeId;
    }

    public void Execute()
    {
        var vm = _editor.AddNodeInternal(_nodeType, _position, _existingNodeId);
        if (_properties != null)
        {
            foreach (var kvp in _properties)
            {
                var prop = vm.Model.Properties.FirstOrDefault(p => p.Name == kvp.Key);
                prop?.SetValue(kvp.Value);
            }
        }
    }

    public void Undo()
    {
        var vm = _editor.FindNodeById(_existingNodeId!);
        if (vm != null)
            _editor.RemoveNodeInternal(vm);
    }
}

public class DeleteNodesAction : IUndoableAction
{
    private readonly EditorViewModel _editor;
    private readonly List<NodeSnapshot> _nodeSnapshots;
    private readonly List<ConnectionSnapshot> _connectionSnapshots;

    public string Description => _nodeSnapshots.Count == 1 ? "Delete Node" : $"Delete {_nodeSnapshots.Count} Nodes";

    public DeleteNodesAction(EditorViewModel editor, List<NodeSnapshot> nodeSnapshots,
        List<ConnectionSnapshot> connectionSnapshots)
    {
        _editor = editor;
        _nodeSnapshots = nodeSnapshots;
        _connectionSnapshots = connectionSnapshots;
    }

    public void Execute()
    {
        foreach (var snap in _nodeSnapshots)
        {
            var vm = _editor.FindNodeById(snap.Id);
            if (vm != null)
                _editor.RemoveNodeInternal(vm);
        }
    }

    public void Undo()
    {
        foreach (var snap in _nodeSnapshots)
        {
            var vm = _editor.AddNodeInternal(snap.NodeType, snap.Position, snap.Id);
            foreach (var kvp in snap.Properties)
            {
                var prop = vm.Model.Properties.FirstOrDefault(p => p.Name == kvp.Key);
                prop?.SetValue(kvp.Value);
            }
        }

        foreach (var conn in _connectionSnapshots)
        {
            _editor.TryConnectByIds(conn.SourceNodeId, conn.SourcePortName,
                conn.TargetNodeId, conn.TargetPortName);
        }
    }
}

public class AddNodesGroupAction : IUndoableAction
{
    private readonly EditorViewModel _editor;
    private readonly List<NodeSnapshot> _nodeSnapshots;
    private readonly List<ConnectionSnapshot> _connectionSnapshots;

    public string Description { get; }

    public AddNodesGroupAction(EditorViewModel editor, List<NodeSnapshot> nodeSnapshots,
        List<ConnectionSnapshot> connectionSnapshots, string description = "Add Nodes")
    {
        _editor = editor;
        _nodeSnapshots = nodeSnapshots;
        _connectionSnapshots = connectionSnapshots;
        Description = description;
    }

    public void Execute()
    {
        foreach (var snap in _nodeSnapshots)
        {
            var vm = _editor.AddNodeInternal(snap.NodeType, snap.Position, snap.Id);
            foreach (var kvp in snap.Properties)
            {
                var prop = vm.Model.Properties.FirstOrDefault(p => p.Name == kvp.Key);
                prop?.SetValue(kvp.Value);
            }
        }
        foreach (var conn in _connectionSnapshots)
        {
            _editor.TryConnectByIds(conn.SourceNodeId, conn.SourcePortName,
                conn.TargetNodeId, conn.TargetPortName);
        }
    }

    public void Undo()
    {
        foreach (var snap in _nodeSnapshots)
        {
            var vm = _editor.FindNodeById(snap.Id);
            if (vm != null)
                _editor.RemoveNodeInternal(vm);
        }
    }
}

public class ConnectionAction : IUndoableAction
{
    private readonly EditorViewModel _editor;
    private readonly string _srcNodeId;
    private readonly string _srcPortName;
    private readonly string _tgtNodeId;
    private readonly string _tgtPortName;
    private readonly bool _isAdd;

    public string Description => _isAdd ? "Connect" : "Disconnect";

    public ConnectionAction(EditorViewModel editor, string srcNodeId, string srcPortName,
        string tgtNodeId, string tgtPortName, bool isAdd)
    {
        _editor = editor;
        _srcNodeId = srcNodeId;
        _srcPortName = srcPortName;
        _tgtNodeId = tgtNodeId;
        _tgtPortName = tgtPortName;
        _isAdd = isAdd;
    }

    public void Execute()
    {
        if (_isAdd)
            _editor.TryConnectByIds(_srcNodeId, _srcPortName, _tgtNodeId, _tgtPortName);
        else
            _editor.DisconnectByIds(_srcNodeId, _srcPortName, _tgtNodeId, _tgtPortName);
    }

    public void Undo()
    {
        if (_isAdd)
            _editor.DisconnectByIds(_srcNodeId, _srcPortName, _tgtNodeId, _tgtPortName);
        else
            _editor.TryConnectByIds(_srcNodeId, _srcPortName, _tgtNodeId, _tgtPortName);
    }
}

public class ChangePropertyAction : IUndoableAction
{
    private readonly EditorViewModel _editor;
    private readonly string _nodeId;
    private readonly string _propertyName;
    private object? _oldValue;
    private object? _newValue;
    private DateTime _timestamp;

    public string Description => "Change Property";

    public ChangePropertyAction(EditorViewModel editor, string nodeId, string propertyName,
        object? oldValue, object? newValue)
    {
        _editor = editor;
        _nodeId = nodeId;
        _propertyName = propertyName;
        _oldValue = oldValue;
        _newValue = newValue;
        _timestamp = DateTime.UtcNow;
    }

    public bool TryMerge(string nodeId, string propertyName, object? newValue)
    {
        if (_nodeId != nodeId || _propertyName != propertyName)
            return false;
        if ((DateTime.UtcNow - _timestamp).TotalMilliseconds > 500)
            return false;
        _newValue = newValue;
        _timestamp = DateTime.UtcNow;
        return true;
    }

    public void Execute()
    {
        var nodeVm = _editor.FindNodeById(_nodeId);
        var prop = nodeVm?.Model.Properties.FirstOrDefault(p => p.Name == _propertyName);
        prop?.SetValue(_newValue);
        _editor.RefreshPropertyEditor();
    }

    public void Undo()
    {
        var nodeVm = _editor.FindNodeById(_nodeId);
        var prop = nodeVm?.Model.Properties.FirstOrDefault(p => p.Name == _propertyName);
        prop?.SetValue(_oldValue);
        _editor.RefreshPropertyEditor();
    }
}

public class MoveNodesAction : IUndoableAction
{
    private readonly EditorViewModel _editor;
    private readonly Dictionary<string, (Point OldPos, Point NewPos)> _moves;

    public string Description => "Move Nodes";

    public MoveNodesAction(EditorViewModel editor, Dictionary<string, (Point OldPos, Point NewPos)> moves)
    {
        _editor = editor;
        _moves = moves;
    }

    public void Execute()
    {
        foreach (var kvp in _moves)
        {
            var vm = _editor.FindNodeById(kvp.Key);
            if (vm != null)
                vm.Location = kvp.Value.NewPos;
        }
    }

    public void Undo()
    {
        foreach (var kvp in _moves)
        {
            var vm = _editor.FindNodeById(kvp.Key);
            if (vm != null)
                vm.Location = kvp.Value.OldPos;
        }
    }
}
