using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using MVXTester.Core.Models;

namespace MVXTester.App.ViewModels;

/// <summary>
/// FunctionNode의 서브그래프를 읽기 전용으로 표시하기 위한 ViewModel.
/// </summary>
public partial class FunctionDetailViewModel : ObservableObject
{
    public string FunctionName { get; }
    public string SourceFilePath { get; }
    public ObservableCollection<NodeViewModel> Nodes { get; } = new();
    public ObservableCollection<ConnectionViewModel> Connections { get; } = new();

    public FunctionDetailViewModel(FunctionNode functionNode)
    {
        FunctionName = functionNode.CustomName ?? functionNode.Name;
        SourceFilePath = functionNode.SourceFilePath;

        var subGraph = functionNode.SubGraph;
        if (subGraph == null) return;

        var positions = functionNode.SubGraphPositions;

        // 1. 서브그래프 노드 → NodeViewModel 생성
        var nodeVmMap = new Dictionary<string, NodeViewModel>();
        foreach (var node in subGraph.Nodes)
        {
            var vm = new NodeViewModel(node);

            // 원본 좌표 복원
            if (positions != null && positions.TryGetValue(node.Id, out var pos))
            {
                vm.Location = new Point(pos.X, pos.Y);
            }

            Nodes.Add(vm);
            nodeVmMap[node.Id] = vm;
        }

        // 2. 서브그래프 연결선 → ConnectionViewModel 생성
        foreach (var conn in subGraph.Connections)
        {
            var sourceNodeId = conn.Source.Owner.Id;
            var targetNodeId = conn.Target.Owner.Id;

            if (!nodeVmMap.TryGetValue(sourceNodeId, out var sourceNodeVm)) continue;
            if (!nodeVmMap.TryGetValue(targetNodeId, out var targetNodeVm)) continue;

            var sourceConnector = FindOutputConnector(sourceNodeVm, conn.Source.Name);
            var targetConnector = FindInputConnector(targetNodeVm, conn.Target.Name);

            if (sourceConnector != null && targetConnector != null)
            {
                sourceConnector.IsConnected = true;
                targetConnector.IsConnected = true;
                Connections.Add(new ConnectionViewModel(sourceConnector, targetConnector, conn));
            }
        }
    }

    private static ConnectorViewModel? FindOutputConnector(NodeViewModel nodeVm, string portName)
    {
        foreach (var c in nodeVm.OutputConnectors)
        {
            if (c.Name == portName) return c;
        }
        return null;
    }

    private static ConnectorViewModel? FindInputConnector(NodeViewModel nodeVm, string portName)
    {
        foreach (var c in nodeVm.InputConnectors)
        {
            if (c.Name == portName) return c;
        }
        return null;
    }
}
