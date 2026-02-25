using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MVXTester.Core.Models;
using MVXTester.Core.Engine;
using MVXTester.Core.Registry;
using MVXTester.Core.UndoRedo;
using MVXTester.App.UndoRedo;
using MVXTester.App.Services;

namespace MVXTester.App.ViewModels;

public partial class EditorViewModel : ObservableObject
{
    private readonly NodeGraph _graph = new();
    private readonly GraphExecutor _executor = new();
    private readonly NodeRegistry _registry;
    private readonly UndoRedoManager _undoManager = new();
    private readonly ClipboardService _clipboard = new();

    public ObservableCollection<NodeViewModel> Nodes { get; } = new();
    public ObservableCollection<ConnectionViewModel> Connections { get; } = new();
    public PendingConnectionViewModel PendingConnection { get; }
    public ObservableCollection<object> SelectedNodes { get; } = new();

    [ObservableProperty] private NodeViewModel? _selectedNode;

    private bool _isExecuting;
    private bool _isStreaming;
    private CancellationTokenSource? _executionCts;
    private bool _isSyncingSelection;

    public bool IsExecuting
    {
        get => _isExecuting;
        private set => SetProperty(ref _isExecuting, value);
    }

    public bool IsStreaming
    {
        get => _isStreaming;
        private set => SetProperty(ref _isStreaming, value);
    }

    public NodeGraph Graph => _graph;
    public UndoRedoManager UndoManager => _undoManager;

    private bool _isBatchingConnections;

    public event Action? GraphExecuted;
    public event Action<NodeViewModel?>? NodeSelected;
    public event Action? PropertyEditorRefreshRequested;
    public event Action? ConnectionChanged;
    public event Func<NodeViewModel, Task>? NodeDoubleClicked;
    public event Action<string>? ConnectionWarning;
    public event Action<NodeViewModel>? NodeDropped;
    public event Action<List<NodeViewModel>>? AutoConnectRequested;

    public EditorViewModel(NodeRegistry registry)
    {
        _registry = registry;
        PendingConnection = new PendingConnectionViewModel(this);
        SelectedNodes.CollectionChanged += OnSelectedNodesCollectionChanged;
    }

    private void OnSelectedNodesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_isSyncingSelection) return;
        _isSyncingSelection = true;
        try
        {
            var lastSelected = SelectedNodes.OfType<NodeViewModel>().LastOrDefault();
            SelectedNode = lastSelected;
        }
        finally
        {
            _isSyncingSelection = false;
        }
    }

    partial void OnSelectedNodeChanged(NodeViewModel? value)
    {
        NodeSelected?.Invoke(value);
    }

    public void SelectNode(NodeViewModel? node)
    {
        _isSyncingSelection = true;
        try
        {
            SelectedNodes.Clear();
            if (node != null)
                SelectedNodes.Add(node);
            SelectedNode = node;
        }
        finally
        {
            _isSyncingSelection = false;
        }
    }

    // ===== Node CRUD with Undo support =====

    public NodeViewModel AddNode(Type nodeType, Point position)
    {
        var vm = AddNodeInternal(nodeType, position, null);

        if (!_undoManager.IsExecutingUndoRedo)
        {
            var props = new Dictionary<string, object?>();
            foreach (var p in vm.Model.Properties)
                props[p.Name] = p.Value;

            var action = new AddNodeAction(this, nodeType, position,
                properties: props, existingNodeId: vm.Model.Id);
            _undoManager.PushAction(action);
        }

        return vm;
    }

    public NodeViewModel AddNode(NodeRegistryEntry entry, Point position)
    {
        return AddNode(entry.NodeType, position);
    }

    public void DropNodeFromPalette(NodeRegistryEntry entry, Point position)
    {
        var nodeVm = AddNode(entry, position);
        SelectNode(nodeVm);
        NodeDropped?.Invoke(nodeVm);
    }

    [RelayCommand]
    private void DeleteSelectedNodes()
    {
        var selected = GetEffectiveSelection();
        if (selected.Count == 0) return;

        var nodeSnaps = selected.Select(NodeSnapshot.FromViewModel).ToList();
        var connSnaps = new List<ConnectionSnapshot>();

        var selectedSet = new HashSet<NodeViewModel>(selected);
        var affectedConnections = Connections
            .Where(c => selectedSet.Contains(c.Source.Node) || selectedSet.Contains(c.Target.Node))
            .ToList();

        foreach (var c in affectedConnections)
        {
            connSnaps.Add(new ConnectionSnapshot
            {
                SourceNodeId = c.Source.Node.Model.Id,
                SourcePortName = c.Source.Name,
                TargetNodeId = c.Target.Node.Model.Id,
                TargetPortName = c.Target.Name
            });
        }

        foreach (var vm in selected)
            RemoveNodeInternal(vm);

        if (!_undoManager.IsExecutingUndoRedo)
        {
            var action = new DeleteNodesAction(this, nodeSnaps, connSnaps);
            _undoManager.PushAction(action);
        }

        if (connSnaps.Count > 0 && !_isBatchingConnections)
            ConnectionChanged?.Invoke();
    }

    [RelayCommand]
    private void DeleteNode(NodeViewModel? nodeVm)
    {
        if (nodeVm == null) return;

        var nodeSnaps = new List<NodeSnapshot> { NodeSnapshot.FromViewModel(nodeVm) };
        var connSnaps = new List<ConnectionSnapshot>();

        var affectedConnections = Connections
            .Where(c => c.Source.Node == nodeVm || c.Target.Node == nodeVm)
            .ToList();

        foreach (var c in affectedConnections)
        {
            connSnaps.Add(new ConnectionSnapshot
            {
                SourceNodeId = c.Source.Node.Model.Id,
                SourcePortName = c.Source.Name,
                TargetNodeId = c.Target.Node.Model.Id,
                TargetPortName = c.Target.Name
            });
        }

        RemoveNodeInternal(nodeVm);

        if (!_undoManager.IsExecutingUndoRedo)
        {
            var action = new DeleteNodesAction(this, nodeSnaps, connSnaps);
            _undoManager.PushAction(action);
        }

        if (connSnaps.Count > 0 && !_isBatchingConnections)
            ConnectionChanged?.Invoke();
    }

    public void TryConnect(ConnectorViewModel source, ConnectorViewModel target)
    {
        if (source.IsInput && !target.IsInput)
            (source, target) = (target, source);

        if (source.IsInput || !target.IsInput) return;
        if (source.OutputPort == null || target.InputPort == null) return;

        if (source.OutputPort.Owner == target.InputPort.Owner)
            return;

        if (!target.DataType.IsAssignableFrom(source.DataType)
            && !source.DataType.IsAssignableFrom(target.DataType))
        {
            var srcType = GetTypeShortName(source.DataType);
            var tgtType = GetTypeShortName(target.DataType);
            ConnectionWarning?.Invoke(
                $"Type mismatch: {source.Node.Title}.{source.Name}({srcType}) -> {target.Node.Title}.{target.Name}({tgtType})");
            return;
        }

        if (_graph.WouldCreateCycle(source.OutputPort.Owner, target.InputPort.Owner))
        {
            ConnectionWarning?.Invoke("Cycle connections are not allowed.");
            return;
        }

        ConnectionSnapshot? removedConn = null;
        var existing = Connections.FirstOrDefault(c => c.Target == target);
        if (existing != null)
        {
            removedConn = new ConnectionSnapshot
            {
                SourceNodeId = existing.Source.Node.Model.Id,
                SourcePortName = existing.Source.Name,
                TargetNodeId = existing.Target.Node.Model.Id,
                TargetPortName = existing.Target.Name
            };
            _graph.RemoveConnection(existing.Model);
            Connections.Remove(existing);
        }

        var connection = _graph.Connect(source.OutputPort, target.InputPort);
        if (connection != null)
        {
            Connections.Add(new ConnectionViewModel(source, target, connection));
            UpdateAllConnectorStates();

            if (!_undoManager.IsExecutingUndoRedo)
            {
                var actions = new List<IUndoableAction>();
                if (removedConn != null)
                {
                    actions.Add(new ConnectionAction(this,
                        removedConn.SourceNodeId, removedConn.SourcePortName,
                        removedConn.TargetNodeId, removedConn.TargetPortName, isAdd: false));
                }
                actions.Add(new ConnectionAction(this,
                    source.Node.Model.Id, source.Name,
                    target.Node.Model.Id, target.Name, isAdd: true));

                if (actions.Count == 1)
                    _undoManager.PushAction(actions[0]);
                else
                    _undoManager.PushAction(new CompositeAction("Connect", actions));
            }

            if (!_isBatchingConnections)
                ConnectionChanged?.Invoke();
        }
    }

    [RelayCommand]
    private void DeleteConnection(ConnectionViewModel? connVm)
    {
        if (connVm == null) return;

        var snapshot = new ConnectionSnapshot
        {
            SourceNodeId = connVm.Source.Node.Model.Id,
            SourcePortName = connVm.Source.Name,
            TargetNodeId = connVm.Target.Node.Model.Id,
            TargetPortName = connVm.Target.Name
        };

        _graph.RemoveConnection(connVm.Model);
        Connections.Remove(connVm);
        UpdateAllConnectorStates();

        if (!_undoManager.IsExecutingUndoRedo)
        {
            var action = new ConnectionAction(this,
                snapshot.SourceNodeId, snapshot.SourcePortName,
                snapshot.TargetNodeId, snapshot.TargetPortName, isAdd: false);
            _undoManager.PushAction(action);
        }

        if (!_isBatchingConnections)
            ConnectionChanged?.Invoke();
    }

    // ===== Internal methods =====

    private const double GridSize = 20.0;

    public static Point SnapToGrid(Point position)
        => new(Math.Round(position.X / GridSize) * GridSize,
               Math.Round(position.Y / GridSize) * GridSize);

    public NodeViewModel AddNodeInternal(Type nodeType, Point position, string? idOverride)
    {
        var node = _registry.CreateNode(nodeType);
        if (idOverride != null)
            ((BaseNode)node).Id = idOverride;

        _graph.AddNode(node);
        var vm = new NodeViewModel(node) { Location = SnapToGrid(position) };
        Nodes.Add(vm);
        return vm;
    }

    public void RemoveNodeInternal(NodeViewModel nodeVm)
    {
        var connectionsToRemove = Connections
            .Where(c => c.Source.Node == nodeVm || c.Target.Node == nodeVm)
            .ToList();

        foreach (var conn in connectionsToRemove)
        {
            _graph.RemoveConnection(conn.Model);
            Connections.Remove(conn);
        }

        if (connectionsToRemove.Count > 0)
            UpdateAllConnectorStates();

        (nodeVm.Model as BaseNode)?.Cleanup();
        _graph.RemoveNode(nodeVm.Model);
        Nodes.Remove(nodeVm);

        if (SelectedNode == nodeVm)
            SelectNode(null);
    }

    public void TryConnectByIds(string srcNodeId, string srcPortName,
        string tgtNodeId, string tgtPortName)
    {
        var srcVm = FindNodeById(srcNodeId);
        var tgtVm = FindNodeById(tgtNodeId);
        if (srcVm == null || tgtVm == null) return;

        var srcConn = srcVm.OutputConnectors.FirstOrDefault(c => c.Name == srcPortName);
        var tgtConn = tgtVm.InputConnectors.FirstOrDefault(c => c.Name == tgtPortName);

        if (srcConn?.OutputPort == null || tgtConn?.InputPort == null) return;

        var existing = Connections.FirstOrDefault(c => c.Target == tgtConn);
        if (existing != null)
        {
            _graph.RemoveConnection(existing.Model);
            Connections.Remove(existing);
        }

        var connection = _graph.Connect(srcConn.OutputPort, tgtConn.InputPort);
        if (connection != null)
        {
            Connections.Add(new ConnectionViewModel(srcConn, tgtConn, connection));
            UpdateAllConnectorStates();
        }
    }

    public void DisconnectByIds(string srcNodeId, string srcPortName,
        string tgtNodeId, string tgtPortName)
    {
        var conn = Connections.FirstOrDefault(c =>
            c.Source.Node.Model.Id == srcNodeId && c.Source.Name == srcPortName &&
            c.Target.Node.Model.Id == tgtNodeId && c.Target.Name == tgtPortName);

        if (conn != null)
        {
            _graph.RemoveConnection(conn.Model);
            Connections.Remove(conn);
            UpdateAllConnectorStates();
        }
    }

    public NodeViewModel? FindNodeById(string nodeId)
    {
        return Nodes.FirstOrDefault(n => n.Model.Id == nodeId);
    }

    public void UpdateAllConnectorStates()
    {
        foreach (var nodeVm in Nodes)
        {
            foreach (var c in nodeVm.InputConnectors)
            {
                var port = nodeVm.Model.Inputs.FirstOrDefault(i => i.Name == c.Name);
                c.IsConnected = port?.IsConnected ?? false;
            }
            foreach (var c in nodeVm.OutputConnectors)
            {
                var port = nodeVm.Model.Outputs.FirstOrDefault(o => o.Name == c.Name);
                c.IsConnected = port != null && port.Connections.Count > 0;
            }
        }
    }

    public void RefreshPropertyEditor()
    {
        PropertyEditorRefreshRequested?.Invoke();
    }

    // ===== Clipboard =====

    private List<NodeViewModel> GetEffectiveSelection()
    {
        var selected = SelectedNodes.OfType<NodeViewModel>().ToList();
        if (selected.Count == 0)
            selected = Nodes.Where(n => n.IsSelected).ToList();
        if (selected.Count == 0 && SelectedNode != null)
            selected.Add(SelectedNode);
        return selected;
    }

    [RelayCommand]
    private void Copy()
    {
        var selected = GetEffectiveSelection();
        if (selected.Count == 0) return;
        _clipboard.Copy(this, selected);
    }

    [RelayCommand]
    private void Cut()
    {
        var selected = GetEffectiveSelection();
        if (selected.Count == 0) return;
        _clipboard.Copy(this, selected);
        DeleteSelectedNodes();
    }

    [RelayCommand]
    private void Paste()
    {
        if (!_clipboard.HasData) return;

        var pasteResult = _clipboard.Paste(this);
        var created = pasteResult.CreatedNodes;
        if (created.Count == 0) return;

        _isSyncingSelection = true;
        try
        {
            SelectedNodes.Clear();
            foreach (var vm in created)
                SelectedNodes.Add(vm);
            SelectedNode = created.LastOrDefault();
        }
        finally
        {
            _isSyncingSelection = false;
        }

        if (pasteResult.DeferredConnections.Count > 0)
        {
            var deferredConns = pasteResult.DeferredConnections;
            Application.Current.Dispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.Loaded,
                () =>
                {
                    _isBatchingConnections = true;
                    foreach (var (srcId, srcPort, tgtId, tgtPort) in deferredConns)
                    {
                        TryConnectByIds(srcId, srcPort, tgtId, tgtPort);
                    }
                    _isBatchingConnections = false;
                    ConnectionChanged?.Invoke();

                    if (!_undoManager.IsExecutingUndoRedo)
                    {
                        var nodeSnaps = created.Select(NodeSnapshot.FromViewModel).ToList();
                        var pastedIds = new HashSet<string>(created.Select(n => n.Model.Id));
                        var connSnaps = Connections
                            .Where(c => pastedIds.Contains(c.Source.Node.Model.Id) &&
                                        pastedIds.Contains(c.Target.Node.Model.Id))
                            .Select(c => new ConnectionSnapshot
                            {
                                SourceNodeId = c.Source.Node.Model.Id,
                                SourcePortName = c.Source.Name,
                                TargetNodeId = c.Target.Node.Model.Id,
                                TargetPortName = c.Target.Name
                            }).ToList();

                        var action = new AddNodesGroupAction(this, nodeSnaps, connSnaps, "Paste");
                        _undoManager.PushAction(action);
                    }
                });
        }
        else
        {
            if (!_undoManager.IsExecutingUndoRedo)
            {
                var nodeSnaps = created.Select(NodeSnapshot.FromViewModel).ToList();
                var action = new AddNodesGroupAction(this, nodeSnaps, new List<ConnectionSnapshot>(), "Paste");
                _undoManager.PushAction(action);
            }
        }
    }

    [RelayCommand]
    private void Duplicate()
    {
        var selected = GetEffectiveSelection();
        if (selected.Count == 0) return;
        _clipboard.Copy(this, selected);
        Paste();
    }

    // ===== Undo / Redo =====

    [RelayCommand] private void Undo() => _undoManager.Undo();
    [RelayCommand] private void Redo() => _undoManager.Redo();

    // ===== Select All =====

    [RelayCommand]
    private void SelectAll()
    {
        _isSyncingSelection = true;
        try
        {
            SelectedNodes.Clear();
            foreach (var node in Nodes)
                SelectedNodes.Add(node);
            SelectedNode = Nodes.LastOrDefault();
        }
        finally
        {
            _isSyncingSelection = false;
        }
    }

    // ===== Drag tracking =====

    private Dictionary<string, Point>? _dragStartPositions;

    [RelayCommand]
    private void ItemsDragStarted()
    {
        _dragStartPositions = new Dictionary<string, Point>();
        foreach (var node in SelectedNodes.OfType<NodeViewModel>())
        {
            _dragStartPositions[node.Model.Id] = node.Location;
        }
    }

    [RelayCommand]
    private void ItemsDragCompleted()
    {
        if (_dragStartPositions == null) return;

        foreach (var kvp in _dragStartPositions)
        {
            var vm = FindNodeById(kvp.Key);
            if (vm != null)
                vm.Location = SnapToGrid(vm.Location);
        }

        var movedNodes = _dragStartPositions.Keys
            .Select(id => FindNodeById(id))
            .Where(n => n != null
                && _dragStartPositions.TryGetValue(n!.Model.Id, out var startPos)
                && startPos != n.Location)
            .Cast<NodeViewModel>()
            .ToList();

        var moves = new Dictionary<string, (Point OldPos, Point NewPos)>();
        foreach (var kvp in _dragStartPositions)
        {
            var vm = FindNodeById(kvp.Key);
            if (vm != null && kvp.Value != vm.Location)
                moves[kvp.Key] = (kvp.Value, vm.Location);
        }

        if (moves.Count > 0 && !_undoManager.IsExecutingUndoRedo)
        {
            var action = new MoveNodesAction(this, moves);
            _undoManager.PushAction(action);
        }

        _dragStartPositions = null;

        if (movedNodes.Count > 0)
            AutoConnectRequested?.Invoke(movedNodes);
    }

    // ===== Auto-connect by proximity =====

    public const double ProximityThreshold = 50.0;

    public void AutoConnectByProximity(IEnumerable<NodeViewModel> movedNodes)
    {
        var draggedSet = new HashSet<NodeViewModel>(movedNodes);
        if (draggedSet.Count == 0) return;

        var newConnections = new List<(ConnectorViewModel src, ConnectorViewModel tgt)>();

        foreach (var node in draggedSet)
        {
            foreach (var output in node.OutputConnectors)
            {
                if (output.OutputPort == null) continue;

                ConnectorViewModel? bestMatch = null;
                double bestDist = ProximityThreshold;

                foreach (var otherNode in Nodes)
                {
                    if (draggedSet.Contains(otherNode)) continue;
                    foreach (var input in otherNode.InputConnectors)
                    {
                        if (input.InputPort == null || input.InputPort.IsConnected) continue;
                        if (!Connection.CanConnect(output.OutputPort, input.InputPort)) continue;

                        var dist = Distance(output.Anchor, input.Anchor);
                        if (dist < bestDist)
                        {
                            bestDist = dist;
                            bestMatch = input;
                        }
                    }
                }

                if (bestMatch != null)
                    newConnections.Add((output, bestMatch));
            }
        }

        foreach (var node in draggedSet)
        {
            foreach (var input in node.InputConnectors)
            {
                if (input.InputPort == null || input.InputPort.IsConnected) continue;
                if (newConnections.Any(c => c.tgt == input)) continue;

                ConnectorViewModel? bestMatch = null;
                double bestDist = ProximityThreshold;

                foreach (var otherNode in Nodes)
                {
                    if (draggedSet.Contains(otherNode)) continue;
                    foreach (var output in otherNode.OutputConnectors)
                    {
                        if (output.OutputPort == null) continue;
                        if (!Connection.CanConnect(output.OutputPort, input.InputPort)) continue;

                        var dist = Distance(output.Anchor, input.Anchor);
                        if (dist < bestDist)
                        {
                            bestDist = dist;
                            bestMatch = output;
                        }
                    }
                }

                if (bestMatch != null)
                    newConnections.Add((bestMatch, input));
            }
        }

        foreach (var (src, tgt) in newConnections)
            TryConnect(src, tgt);
    }

    private static double Distance(Point a, Point b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static string GetTypeShortName(Type type)
    {
        if (type == typeof(OpenCvSharp.Mat)) return "Mat";
        if (type == typeof(int)) return "int";
        if (type == typeof(double)) return "double";
        if (type == typeof(string)) return "string";
        if (type == typeof(bool)) return "bool";
        if (type == typeof(float)) return "float";
        if (type == typeof(OpenCvSharp.Point)) return "Point";
        if (type == typeof(OpenCvSharp.Size)) return "Size";
        if (type == typeof(OpenCvSharp.Scalar)) return "Scalar";
        if (type == typeof(OpenCvSharp.Rect)) return "Rect";
        if (type == typeof(OpenCvSharp.Rect[])) return "Rect[]";
        if (type == typeof(OpenCvSharp.Point[][])) return "Contours";
        if (type == typeof(double[])) return "double[]";
        return type.Name;
    }

    // ===== Double-click connector =====

    [RelayCommand]
    private void ConnectorDoubleClick(ConnectorViewModel? connector)
    {
        if (connector == null) return;
        if (connector.IsInput && connector.InputPort?.IsConnected == true) return;

        ConnectorViewModel? bestMatch = null;
        double bestDist = double.MaxValue;

        foreach (var node in Nodes)
        {
            if (node == connector.Node) continue;

            var candidates = connector.IsInput
                ? node.OutputConnectors.AsEnumerable()
                : node.InputConnectors.AsEnumerable();

            foreach (var candidate in candidates)
            {
                if (!connector.IsInput && candidate.IsConnected) continue;

                var output = connector.IsInput ? candidate : connector;
                var input = connector.IsInput ? connector : candidate;
                if (output.OutputPort == null || input.InputPort == null) continue;

                if (output.OutputPort.Owner == input.InputPort.Owner) continue;
                if (!input.InputPort.DataType.IsAssignableFrom(output.OutputPort.DataType)
                    && !output.OutputPort.DataType.IsAssignableFrom(input.InputPort.DataType))
                    continue;

                var dist = Distance(connector.Anchor, candidate.Anchor);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    bestMatch = candidate;
                }
            }
        }

        if (bestMatch != null)
            TryConnect(
                connector.IsInput ? bestMatch : connector,
                connector.IsInput ? connector : bestMatch);
    }

    // ===== Double-click node =====

    [RelayCommand]
    private async Task NodeDoubleClick(NodeViewModel? nodeVm)
    {
        if (nodeVm == null) return;
        if (NodeDoubleClicked != null)
            await NodeDoubleClicked.Invoke(nodeVm);
    }

    // ===== Execution =====

    [RelayCommand]
    public async Task Execute()
    {
        if (IsExecuting) return;

        _executionCts?.Dispose();
        _executionCts = new CancellationTokenSource();
        IsExecuting = true;

        try
        {
            await Task.Run(() => _executor.Execute(_graph, cancellationToken: _executionCts.Token));
            foreach (var nodeVm in Nodes)
                nodeVm.UpdatePreview();
            GraphExecuted?.Invoke();
        }
        catch (OperationCanceledException)
        {
            foreach (var nodeVm in Nodes)
                nodeVm.UpdatePreview();
            GraphExecuted?.Invoke();
        }
        finally
        {
            IsExecuting = false;
        }
    }

    public async Task ExecuteForce()
    {
        if (IsExecuting) return;

        _executionCts?.Dispose();
        _executionCts = new CancellationTokenSource();
        IsExecuting = true;

        try
        {
            await Task.Run(() => _executor.Execute(_graph, forceAll: true, cancellationToken: _executionCts.Token));
            foreach (var nodeVm in Nodes)
                nodeVm.UpdatePreview();
            GraphExecuted?.Invoke();
        }
        catch (OperationCanceledException)
        {
            foreach (var nodeVm in Nodes)
                nodeVm.UpdatePreview();
            GraphExecuted?.Invoke();
        }
        finally
        {
            IsExecuting = false;
        }
    }

    [RelayCommand]
    public async Task StartStreaming()
    {
        if (IsExecuting || IsStreaming) return;

        _executionCts?.Dispose();
        _executionCts = new CancellationTokenSource();
        IsExecuting = true;
        IsStreaming = true;

        try
        {
            await Task.Run(() => _executor.ExecuteContinuous(
                _graph, _executionCts.Token,
                onFrameComplete: () => Application.Current.Dispatcher.BeginInvoke(() =>
                {
                    foreach (var nodeVm in Nodes)
                        nodeVm.UpdatePreview();
                    GraphExecuted?.Invoke();
                }),
                targetFps: 30
            ));
        }
        catch (OperationCanceledException) { }
        finally
        {
            IsExecuting = false;
            IsStreaming = false;
            foreach (var nodeVm in Nodes)
                nodeVm.UpdatePreview();
            GraphExecuted?.Invoke();
        }
    }

    [RelayCommand]
    public void CancelExecution()
    {
        _executionCts?.Cancel();
    }

    public TimeSpan LastExecutionTime => _executor.LastExecutionTime;

    public (double X, double Y) GetNodePosition(INode node)
    {
        var vm = Nodes.FirstOrDefault(n => n.Model == node);
        return vm != null ? (vm.Location.X, vm.Location.Y) : (0, 0);
    }

    [RelayCommand]
    public void Clear()
    {
        foreach (var nodeVm in Nodes)
            (nodeVm.Model as BaseNode)?.Cleanup();

        Connections.Clear();
        Nodes.Clear();
        _graph.Clear();
        SelectNode(null);
        _undoManager.Clear();
    }
}
