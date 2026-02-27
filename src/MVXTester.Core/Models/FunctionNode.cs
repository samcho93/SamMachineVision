using MVXTester.Core.Engine;
using MVXTester.Core.Serialization;

namespace MVXTester.Core.Models;

/// <summary>
/// 저장된 프로젝트 파일(.mvxp/.mvx)을 함수처럼 불러와서 사용하는 노드.
/// - 파일명 = 함수명 = 노드명
/// - 서브그래프의 소스 노드 출력 → 함수의 매개변수 (InputPort)
/// - 서브그래프의 싱크 노드 값 → 함수의 반환값 (OutputPort)
/// - 모든 데이터 타입 지원, 다수 매개변수/반환값 가능
/// </summary>
public class FunctionNode : BaseNode
{
    private string _sourceFilePath = "";
    private string? _extractDir; // ZIP 추출 임시 디렉토리
    private NodeGraph? _subGraph;
    private GraphExecutor? _executor;

    // 매핑: FunctionNode 입력 포트 → 서브그래프 소스 노드의 출력 포트
    private readonly List<(IInputPort fnInput, INode subNode, IOutputPort subOutput)> _inputMappings = new();

    // 매핑: FunctionNode 출력 포트 → 서브그래프 싱크 노드의 포트
    private readonly List<(IOutputPort fnOutput, INode subNode, string portName, bool readFromInput)> _outputMappings = new();

    // 서브그래프 원본 좌표 (Detail 팝업용)
    private Dictionary<string, (double X, double Y)>? _subGraphPositions;

    /// <summary>
    /// 함수 소스 프로젝트 파일 경로
    /// </summary>
    public string SourceFilePath => _sourceFilePath;

    /// <summary>
    /// 서브그래프 구조 (읽기 전용 — Detail 팝업 표시용)
    /// </summary>
    public NodeGraph? SubGraph => _subGraph;

    /// <summary>
    /// 서브그래프 노드의 원본 좌표 (Detail 팝업 레이아웃용)
    /// </summary>
    public IReadOnlyDictionary<string, (double X, double Y)>? SubGraphPositions => _subGraphPositions;

    /// <summary>
    /// 사용자 지정 표시 이름. null이면 파일명을 사용.
    /// </summary>
    public string? CustomName
    {
        get
        {
            var prop = Properties.FirstOrDefault(p => p.Name == "CustomName");
            var val = prop?.GetValue<string>();
            return string.IsNullOrEmpty(val) ? null : val;
        }
    }

    protected override void Setup()
    {
        // 비어있음 — Initialize()에서 동적으로 포트 생성
    }

    /// <summary>
    /// 프로젝트 파일을 로드하여 서브그래프를 구성하고 경계 포트를 매핑.
    /// NodeRegistry.CreateNode(entry) 이후에 호출됨.
    /// </summary>
    public void Initialize(string filePath)
    {
        _sourceFilePath = filePath;

        // 노드 이름을 파일명(확장자 제외)으로 설정
        Name = System.IO.Path.GetFileNameWithoutExtension(filePath);
        Category = "Function";
        Description = $"Function: {Name}";

        // 직렬화용 프로퍼티 추가 (저장/로드 시 파일 경로 복원에 사용)
        AddFilePathProperty("SourceFilePath", "Source File", filePath,
            "Function source project file path");

        // 사용자 지정 표시 이름 프로퍼티
        AddStringProperty("CustomName", "Display Name", "",
            "Custom display name for this function node");

        try
        {
            // 1. 파일 로드 (ZIP 또는 JSON)
            GraphData? data;
            if (ProjectArchive.IsProjectArchive(filePath))
            {
                var (graphJson, extractDir) = ProjectArchive.Load(filePath);
                _extractDir = extractDir;
                data = GraphSerializer.Deserialize(graphJson);
            }
            else
            {
                data = GraphSerializer.LoadFromFile(filePath);
            }

            if (data == null)
            {
                Error = $"Failed to load function: {filePath}";
                return;
            }

            // 서브그래프 원본 좌표 저장 (Detail 팝업용)
            _subGraphPositions = data.Nodes.ToDictionary(n => n.Id, n => (n.X, n.Y));

            // 2. 서브그래프 재구성
            _subGraph = GraphSerializer.ReconstructGraph(data);
            if (_subGraph == null)
            {
                Error = "Failed to reconstruct sub-graph";
                return;
            }

            // 3. 경계 포트 분석
            var boundaryPorts = SubGraphAnalyzer.Analyze(_subGraph);

            // 4. FunctionNode 포트 생성 및 매핑
            foreach (var bp in boundaryPorts)
            {
                var subNode = _subGraph.Nodes.FirstOrDefault(n => n.Id == bp.NodeId);
                if (subNode == null) continue;

                if (bp.IsInput)
                {
                    // 매개변수: FunctionNode 입력 포트 생성
                    var portName = MakePortName(bp, boundaryPorts);
                    var fnInput = AddInputDynamic(portName, bp.DataType);

                    // 대응하는 서브그래프 소스 노드의 출력 포트 찾기
                    var subOutput = subNode.Outputs.FirstOrDefault(p => p.Name == bp.PortName);
                    if (subOutput != null)
                        _inputMappings.Add((fnInput, subNode, subOutput));
                }
                else
                {
                    // 반환값: FunctionNode 출력 포트 생성
                    var portName = MakePortName(bp, boundaryPorts);
                    var fnOutput = AddOutputDynamic(portName, bp.DataType);

                    _outputMappings.Add((fnOutput, subNode, bp.PortName, bp.ReadFromInputPort));
                }
            }

            _executor = new GraphExecutor();
            Error = null;
        }
        catch (Exception ex)
        {
            Error = $"Function init error: {ex.Message}";
        }
    }

    public override void Process()
    {
        if (_subGraph == null || _executor == null)
        {
            Error = "Function not initialized";
            return;
        }

        try
        {
            // 1. FunctionNode 입력값 → 서브그래프 소스 노드 출력 포트에 주입
            foreach (var (fnInput, subNode, subOutput) in _inputMappings)
            {
                var value = fnInput.GetValue();
                subOutput.SetValue(value);
                subNode.IsDirty = false; // 소스 노드는 Process 호출 불필요 (값 직접 주입)
            }

            // 소스 노드 이외의 모든 노드를 dirty로 설정
            var sourceNodeIds = new HashSet<string>(_inputMappings.Select(m => m.subNode.Id));
            foreach (var node in _subGraph.Nodes)
            {
                if (!sourceNodeIds.Contains(node.Id))
                    node.IsDirty = true;
            }

            // 2. 서브그래프 실행
            _executor.Execute(_subGraph);

            // 3. 서브그래프 싱크 노드 값 → FunctionNode 출력 포트로 전달
            foreach (var (fnOutput, subNode, portName, readFromInput) in _outputMappings)
            {
                object? value;
                if (readFromInput)
                {
                    // 싱크 노드의 입력 포트에서 값 읽기 (ImageShow 등)
                    var port = subNode.Inputs.FirstOrDefault(p => p.Name == portName);
                    value = port?.GetValue();
                }
                else
                {
                    // 싱크 노드의 출력 포트에서 값 읽기
                    var port = subNode.Outputs.FirstOrDefault(p => p.Name == portName);
                    value = port?.GetValue();
                }

                fnOutput.SetValue(value);
            }

            Error = null;
        }
        catch (Exception ex)
        {
            Error = $"Function error: {ex.Message}";
        }
    }

    public override void Cleanup()
    {
        // 서브그래프 내 모든 노드 Cleanup
        if (_subGraph != null)
        {
            foreach (var node in _subGraph.Nodes)
                (node as BaseNode)?.Cleanup();
        }

        // ZIP 추출 디렉토리 정리
        ProjectArchive.CleanupExtractDir(_extractDir);
        _extractDir = null;

        base.Cleanup();
    }

    /// <summary>
    /// 포트 이름 생성. 동일 노드에서 여러 포트가 올 경우 "NodeName.PortName" 형식,
    /// 단일 포트면 "NodeName" 형식으로 간결하게 생성.
    /// </summary>
    private static string MakePortName(BoundaryPort bp, List<BoundaryPort> allPorts)
    {
        // 같은 노드에서 같은 방향의 포트가 여러 개인지 확인
        var sameNodePorts = allPorts.Count(p =>
            p.NodeId == bp.NodeId && p.IsInput == bp.IsInput);

        if (sameNodePorts > 1)
            return $"{bp.NodeName}.{bp.PortName}";

        return bp.NodeName;
    }
}
