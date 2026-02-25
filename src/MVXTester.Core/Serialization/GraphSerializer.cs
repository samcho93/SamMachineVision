using System.Text.Json;
using System.Text.Json.Serialization;
using MVXTester.Core.Models;
using MVXTester.Core.Registry;

namespace MVXTester.Core.Serialization;

public class GraphData
{
    public string Version { get; set; } = "1.0";
    public List<NodeData> Nodes { get; set; } = new();
    public List<ConnectionData> Connections { get; set; } = new();
}

public class NodeData
{
    public string Id { get; set; } = "";
    public string TypeName { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
    public Dictionary<string, JsonElement> Properties { get; set; } = new();
}

public class ConnectionData
{
    public string SourceNodeId { get; set; } = "";
    public string SourcePortName { get; set; } = "";
    public string TargetNodeId { get; set; } = "";
    public string TargetPortName { get; set; } = "";
}

public class GraphSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string Serialize(NodeGraph graph, Func<INode, (double X, double Y)> getPosition)
    {
        var data = new GraphData();

        foreach (var node in graph.Nodes)
        {
            var pos = getPosition(node);
            var nodeData = new NodeData
            {
                Id = node.Id,
                TypeName = node.GetType().FullName ?? node.GetType().Name,
                X = pos.X,
                Y = pos.Y
            };

            foreach (var prop in node.Properties)
            {
                if (prop.Value != null)
                {
                    var json = JsonSerializer.SerializeToElement(prop.Value, prop.ValueType, Options);
                    nodeData.Properties[prop.Name] = json;
                }
            }

            data.Nodes.Add(nodeData);
        }

        foreach (var conn in graph.Connections)
        {
            data.Connections.Add(new ConnectionData
            {
                SourceNodeId = conn.Source.Owner.Id,
                SourcePortName = conn.Source.Name,
                TargetNodeId = conn.Target.Owner.Id,
                TargetPortName = conn.Target.Name
            });
        }

        return JsonSerializer.Serialize(data, Options);
    }

    public static GraphData? Deserialize(string json)
    {
        return JsonSerializer.Deserialize<GraphData>(json, Options);
    }

    public static void SaveToFile(NodeGraph graph, string filePath, Func<INode, (double X, double Y)> getPosition)
    {
        var json = Serialize(graph, getPosition);
        File.WriteAllText(filePath, json);
    }

    public static GraphData? LoadFromFile(string filePath)
    {
        var json = File.ReadAllText(filePath);
        return Deserialize(json);
    }
}
