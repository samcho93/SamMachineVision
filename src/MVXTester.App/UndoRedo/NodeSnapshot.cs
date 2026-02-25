using System.Windows;
using MVXTester.App.ViewModels;

namespace MVXTester.App.UndoRedo;

public class NodeSnapshot
{
    public string Id { get; init; } = "";
    public Type NodeType { get; init; } = null!;
    public Point Position { get; init; }
    public Dictionary<string, object?> Properties { get; init; } = new();

    public static NodeSnapshot FromViewModel(NodeViewModel vm)
    {
        var props = new Dictionary<string, object?>();
        foreach (var p in vm.Model.Properties)
            props[p.Name] = p.Value;

        return new NodeSnapshot
        {
            Id = vm.Model.Id,
            NodeType = vm.Model.GetType(),
            Position = vm.Location,
            Properties = props
        };
    }
}

public class ConnectionSnapshot
{
    public string SourceNodeId { get; init; } = "";
    public string SourcePortName { get; init; } = "";
    public string TargetNodeId { get; init; } = "";
    public string TargetPortName { get; init; } = "";
}
