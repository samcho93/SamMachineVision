using MVXTester.Core.Models;

namespace MVXTester.Core.Serialization;

/// <summary>
/// 서브그래프의 경계 포트 정보 (매개변수 또는 반환값)
/// </summary>
public class BoundaryPort
{
    /// <summary>서브그래프 내 노드 ID</summary>
    public string NodeId { get; set; } = "";

    /// <summary>서브그래프 내 노드 이름 (포트 이름 생성용)</summary>
    public string NodeName { get; set; } = "";

    /// <summary>해당 노드의 포트 이름</summary>
    public string PortName { get; set; } = "";

    /// <summary>포트의 데이터 타입</summary>
    public Type DataType { get; set; } = typeof(object);

    /// <summary>true = 함수의 입력 매개변수, false = 함수의 반환값</summary>
    public bool IsInput { get; set; }

    /// <summary>
    /// 반환값이 싱크 노드의 입력 포트에서 읽어야 하는 경우 true.
    /// (ImageShow 등 출력 포트 없는 노드)
    /// </summary>
    public bool ReadFromInputPort { get; set; }
}

/// <summary>
/// 서브그래프에서 매개변수(입력)와 반환값(출력) 경계 포트를 분석.
///
/// 소스 노드 (in-degree 0): 다른 노드로부터 입력을 받지 않는 노드.
///   → 이 노드의 출력 포트 타입이 함수의 매개변수(InputPort)가 됨.
///
/// 싱크 노드 (out-degree 0): 다른 노드에 출력을 보내지 않는 노드.
///   → 출력 포트가 있으면: 그 출력 포트 타입이 함수의 반환값(OutputPort).
///   → 출력 포트가 없으면 (예: ImageShow): 입력 포트 타입이 함수의 반환값.
/// </summary>
public static class SubGraphAnalyzer
{
    public static List<BoundaryPort> Analyze(NodeGraph graph)
    {
        var result = new List<BoundaryPort>();

        // 그래프 연결에서 in-degree/out-degree 계산
        var hasIncoming = new HashSet<INode>();
        var hasOutgoing = new HashSet<INode>();

        foreach (var conn in graph.Connections)
        {
            hasIncoming.Add(conn.Target.Owner);
            hasOutgoing.Add(conn.Source.Owner);
        }

        // 소스 노드 (in-degree 0) → 함수 매개변수
        foreach (var node in graph.Nodes)
        {
            if (hasIncoming.Contains(node)) continue;

            // 이 노드의 출력 포트들이 함수의 입력 매개변수가 됨
            foreach (var output in node.Outputs)
            {
                result.Add(new BoundaryPort
                {
                    NodeId = node.Id,
                    NodeName = node.Name,
                    PortName = output.Name,
                    DataType = output.DataType,
                    IsInput = true,
                    ReadFromInputPort = false
                });
            }
        }

        // 싱크 노드 (out-degree 0) → 함수 반환값
        foreach (var node in graph.Nodes)
        {
            if (hasOutgoing.Contains(node)) continue;

            if (node.Outputs.Count > 0)
            {
                // 출력 포트가 있는 경우 → 출력 포트에서 값 읽기
                foreach (var output in node.Outputs)
                {
                    result.Add(new BoundaryPort
                    {
                        NodeId = node.Id,
                        NodeName = node.Name,
                        PortName = output.Name,
                        DataType = output.DataType,
                        IsInput = false,
                        ReadFromInputPort = false
                    });
                }
            }
            else if (node.Inputs.Count > 0)
            {
                // 출력 포트가 없는 경우 (ImageShow 등) → 입력 포트 타입 사용
                foreach (var input in node.Inputs)
                {
                    result.Add(new BoundaryPort
                    {
                        NodeId = node.Id,
                        NodeName = node.Name,
                        PortName = input.Name,
                        DataType = input.DataType,
                        IsInput = false,
                        ReadFromInputPort = true
                    });
                }
            }
        }

        return result;
    }
}
