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

    /// <summary>
    /// GraphData에서 NodeGraph를 재구성 (UI ViewModel 없이 모델만).
    /// FunctionNode의 서브그래프 재구성 등에 사용.
    /// </summary>
    /// <returns>재구성된 NodeGraph와 노드 ID 매핑. 실패 시 null.</returns>
    public static NodeGraph? ReconstructGraph(GraphData data)
    {
        var graph = new NodeGraph();
        var nodeMap = new Dictionary<string, INode>();

        foreach (var nodeData in data.Nodes)
        {
            // 타입 검색
            var nodeType = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a =>
                {
                    try { return a.GetTypes(); }
                    catch { return Array.Empty<Type>(); }
                })
                .FirstOrDefault(t => t.FullName == nodeData.TypeName);

            if (nodeType == null) continue;

            INode node;
            try
            {
                node = (INode)Activator.CreateInstance(nodeType)!;
            }
            catch { continue; }

            // ID 복원
            if (node is BaseNode baseNode)
                baseNode.Id = nodeData.Id;

            // 프로퍼티 값 복원
            foreach (var kvp in nodeData.Properties)
            {
                var prop = node.Properties.FirstOrDefault(p => p.Name == kvp.Key);
                if (prop != null)
                {
                    try
                    {
                        var value = kvp.Value.Deserialize(prop.ValueType);
                        prop.SetValue(value);
                    }
                    catch { }
                }
            }

            graph.AddNode(node);
            nodeMap[nodeData.Id] = node;
        }

        // 연결 재구성
        foreach (var connData in data.Connections)
        {
            if (!nodeMap.TryGetValue(connData.SourceNodeId, out var srcNode)) continue;
            if (!nodeMap.TryGetValue(connData.TargetNodeId, out var tgtNode)) continue;

            var srcPort = srcNode.Outputs.FirstOrDefault(p => p.Name == connData.SourcePortName);
            var tgtPort = tgtNode.Inputs.FirstOrDefault(p => p.Name == connData.TargetPortName);

            if (srcPort != null && tgtPort != null)
                graph.Connect(srcPort, tgtPort);
        }

        return graph;
    }

    /// <summary>
    /// 그래프 내 모든 FilePath 프로퍼티에서 유효한 파일 경로를 수집.
    /// Key: 절대 경로, Value: assets/내 상대 경로
    /// </summary>
    public static Dictionary<string, string> CollectReferencedFiles(NodeGraph graph)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var nameCount = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var node in graph.Nodes)
        {
            foreach (var prop in node.Properties)
            {
                if (prop.PropertyType != PropertyType.FilePath) continue;
                var path = prop.GetValue<string>();
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) continue;
                if (map.ContainsKey(path)) continue;

                var fileName = Path.GetFileName(path);

                // 중복 파일명 처리
                if (nameCount.TryGetValue(fileName, out var count))
                {
                    nameCount[fileName] = count + 1;
                    var ext = Path.GetExtension(fileName);
                    var name = Path.GetFileNameWithoutExtension(fileName);
                    fileName = $"{name}_{count + 1}{ext}";
                }
                else
                {
                    nameCount[fileName] = 0;
                }

                map[path] = $"{ProjectArchive.AssetsFolder}/{fileName}";
            }
        }

        return map;
    }
}
