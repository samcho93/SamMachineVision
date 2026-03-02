using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using MVXTester.Core.Models;
using MVXTester.Core.Registry;

namespace MVXTester.App.Views;

public partial class NodeReferenceView : UserControl
{
    // Category color map (Catppuccin Mocha palette)
    private static readonly Dictionary<string, string> CategoryColors = new()
    {
        [NodeCategories.Input] = "#74C7EC",
        [NodeCategories.Color] = "#CBA6F7",
        [NodeCategories.Filter] = "#89B4FA",
        [NodeCategories.Edge] = "#F38BA8",
        [NodeCategories.Morphology] = "#FAB387",
        [NodeCategories.Threshold] = "#F9E2AF",
        [NodeCategories.Contour] = "#A6E3A1",
        [NodeCategories.Feature] = "#94E2D5",
        [NodeCategories.Drawing] = "#F5C2E7",
        [NodeCategories.Transform] = "#89DCEB",
        [NodeCategories.Histogram] = "#B4BEFE",
        [NodeCategories.Arithmetic] = "#CDD6F4",
        [NodeCategories.Detection] = "#F2CDCD",
        [NodeCategories.Segmentation] = "#DDB6F2",
        [NodeCategories.Value] = "#F9E2AF",
        [NodeCategories.Control] = "#FAB387",
        [NodeCategories.Communication] = "#74C7EC",
        [NodeCategories.Data] = "#94E2D5",
        [NodeCategories.Event] = "#F38BA8",
        [NodeCategories.Script] = "#A6E3A1",
        [NodeCategories.Inspection] = "#F9E2AF",
        [NodeCategories.Measurement] = "#89B4FA",
        [NodeCategories.MediaPipe] = "#CBA6F7",
        [NodeCategories.YOLO] = "#F5C2E7",
        [NodeCategories.OCR] = "#94E2D5",
        [NodeCategories.LLMVLM] = "#89DCEB",
        [NodeCategories.Function] = "#A6E3A1",
    };

    // Port type display color
    private static string GetPortTypeColor(Type t)
    {
        var name = t.IsGenericType ? t.GetGenericTypeDefinition().Name : t.Name;
        return name switch
        {
            "Mat" => "#89B4FA",       // blue
            "Int32" => "#F9E2AF",     // yellow
            "Double" => "#FAB387",    // peach
            "Single" => "#FAB387",    // peach
            "Boolean" => "#F38BA8",   // red
            "String" => "#A6E3A1",    // green
            "Point" => "#CBA6F7",     // purple
            "Point2f" => "#CBA6F7",
            "Rect" => "#F5C2E7",      // pink
            "Size" => "#94E2D5",      // teal
            "Scalar" => "#B4BEFE",    // lavender
            _ when t.IsArray => "#89DCEB",  // sky for arrays
            _ => "#CDD6F4"            // text default
        };
    }

    // Friendly type name
    private static string FriendlyTypeName(Type t)
    {
        if (t.IsArray) return FriendlyTypeName(t.GetElementType()!) + "[]";
        if (t.IsGenericType)
        {
            var gen = t.GetGenericArguments();
            var baseName = t.Name.Split('`')[0];
            return $"{baseName}<{string.Join(", ", gen.Select(FriendlyTypeName))}>";
        }
        return t.Name switch
        {
            "Int32" => "int",
            "Int64" => "long",
            "Single" => "float",
            "Double" => "double",
            "Boolean" => "bool",
            "String" => "string",
            "Object" => "object",
            _ => t.Name
        };
    }

    // Korean descriptions per node: (기능 설명, 응용 분야)
    private static readonly Dictionary<string, (string Desc, string Apps)> KoreanDescriptions = new()
    {
        // ── Input/Output ──
        ["Image Read"] = ("파일 경로에서 이미지를 읽어 Mat 형식으로 출력합니다. PNG, JPG, BMP, TIFF 등 다양한 포맷을 지원합니다.",
            "이미지 파일 기반 검사, 오프라인 이미지 분석, 배치 처리 파이프라인의 입력"),
        ["Image Write"] = ("입력받은 이미지를 지정된 경로에 파일로 저장합니다. PNG, JPG, BMP, TIFF 포맷을 지원합니다.",
            "검사 결과 이미지 저장, 처리된 이미지 아카이빙, 품질 관리 기록 보관"),
        ["Video Read"] = ("비디오 파일을 프레임 단위로 읽습니다. 스트리밍 소스로 동작하며 끝에 도달하면 자동으로 루프됩니다.",
            "비디오 파일 분석, 녹화 영상 재생 및 검사, 알고리즘 테스트용 영상 소스"),
        ["Camera"] = ("USB, HIKROBOT, Cognex GigE 카메라를 통합 지원하는 카메라 노드입니다. 장치 자동 검색, 트리거 모드, 노출, 게인 등 카메라별 설정을 제공합니다.",
            "실시간 라인 검사, 산업용 비전 시스템, 라이브 모니터링, 품질 검사 자동화"),
        ["Image Show"] = ("입력 이미지를 OpenCV 윈도우에 표시합니다. 런타임 모드(F5)에서만 동작하며, 마우스/키보드 이벤트를 Event 노드로 전달합니다.",
            "실시간 결과 확인, 인터랙티브 ROI 설정, 디버깅용 이미지 표시"),

        // ── Value ──
        ["Integer"] = ("정수 상수값을 출력합니다.", "좌표값 설정, 카운터 초기값, 인덱스 지정, 임계값 설정"),
        ["Float"] = ("실수(float) 상수값을 출력합니다.", "가중치 설정, 스케일 팩터, 정밀도가 필요한 파라미터"),
        ["Double"] = ("배정밀도 실수(double) 상수값을 출력합니다.", "고정밀 연산, 측정값 기준, 각도/거리 설정"),
        ["String"] = ("문자열 상수값을 출력합니다.", "파일 경로 지정, 라벨 텍스트, 통신 메시지 설정"),
        ["Bool"] = ("불리언(true/false) 상수값을 출력합니다.", "조건 플래그, 기능 토글, 검사 통과/실패 기준"),
        ["Point"] = ("2D 좌표(X, Y)를 출력합니다.", "ROI 좌표 설정, 그리기 위치, 측정 기준점"),
        ["Size"] = ("크기(Width, Height)를 출력합니다.", "이미지 리사이즈 크기, 커널 크기, 윈도우 크기"),
        ["Scalar"] = ("4개 컴포넌트(V0~V3)의 스칼라값을 출력합니다. BGR 색상이나 BGRA 값을 표현할 수 있습니다.",
            "색상값 지정, 그리기 색상 설정, 범위 필터링 경계값"),
        ["Rect"] = ("사각형(X, Y, Width, Height)을 출력합니다.", "ROI 영역 설정, 크롭 범위, 검출 영역 지정"),
        ["Math Operation"] = ("두 숫자에 대해 산술 연산(+, -, ×, ÷, %, ^, Min, Max)을 수행합니다.",
            "치수 계산, 스케일 변환, 면적 계산, 비율 연산"),
        ["Comparison"] = ("두 숫자를 비교(>, <, >=, <=, ==, !=)하여 불리언 결과를 출력합니다.",
            "임계값 판정, 크기 비교, 범위 검사, 통과/불량 판정"),
        ["Logic Gate"] = ("두 불리언 값에 대한 논리 연산(AND, OR, XOR, NAND, NOR)을 수행합니다.",
            "다중 조건 결합, 복합 검사 로직, 알람 조건 설정"),
        ["List Create"] = ("여러 입력값을 하나의 리스트로 합칩니다. 최대 8개의 입력을 지원합니다.",
            "배치 처리용 데이터 수집, 다중 결과 집계, 컬렉션 생성"),
        ["String Format"] = ("서식 문자열({0}, {1} 등)에 입력 인자를 대입하여 결과 문자열을 생성합니다.",
            "검사 결과 메시지 생성, 로그 포맷팅, 라벨 텍스트 조합"),
        ["Print"] = ("입력값을 사람이 읽기 쉬운 텍스트로 포맷하여 노드 미리보기에 표시합니다. Mat, 배열, 원시 타입 등을 지원합니다.",
            "디버깅, 중간 결과 확인, 데이터 모니터링, 검사 결과 표시"),

        // ── Color ──
        ["Convert Color"] = ("이미지의 색상 공간을 변환합니다. BGR↔Gray, BGR↔HSV, BGR↔RGB 등 다양한 변환을 지원합니다.",
            "그레이스케일 변환, HSV 색상 분석, 색상 공간 전처리"),
        ["In Range"] = ("HSV 또는 다른 색상 공간에서 지정 범위 내의 픽셀만 추출하여 이진 마스크를 생성합니다.",
            "색상 기반 객체 검출, 특정 색상 영역 분리, 색상 필터링"),
        ["Split Channels"] = ("다채널 이미지를 개별 채널로 분리합니다 (예: B, G, R 또는 H, S, V).",
            "채널별 분석, 특정 채널만 처리, 채널별 히스토그램 분석"),
        ["Merge Channels"] = ("개별 채널 이미지를 하나의 다채널 이미지로 합칩니다.",
            "채널별 처리 후 결합, 커스텀 색상 합성, 마스크 합성"),

        // ── Filter ──
        ["Gaussian Blur"] = ("가우시안 블러를 적용하여 이미지를 부드럽게 만듭니다. 노이즈 제거에 효과적입니다.",
            "전처리 노이즈 제거, 에지 검출 전 스무딩, 이미지 품질 개선"),
        ["Median Blur"] = ("중앙값 블러를 적용합니다. 소금-후추 노이즈(salt-and-pepper noise) 제거에 특히 효과적입니다.",
            "임펄스 노이즈 제거, 의료 영상 전처리, 디지털 이미지 복원"),
        ["Bilateral Filter"] = ("양방향 필터를 적용합니다. 에지를 보존하면서 노이즈를 제거하는 고급 필터입니다.",
            "얼굴 피부 보정, 에지 보존 스무딩, 텍스처 보존 필터링"),
        ["Box Filter"] = ("박스(평균) 필터를 적용합니다. 단순한 평균화로 블러 효과를 줍니다.",
            "빠른 스무딩, 이미지 평균화, 단순 전처리"),
        ["Sharpen"] = ("언샤프 마스킹으로 이미지를 선명하게 만듭니다.",
            "흐릿한 이미지 개선, 텍스트 선명화, 디테일 강조"),
        ["Filter 2D"] = ("사용자 정의 컨볼루션 커널을 적용합니다. 커널 행렬을 직접 지정하여 맞춤 필터를 만듭니다.",
            "커스텀 에지 검출, 엠보싱 효과, 특수 필터 연구"),
        ["Non-Local Means Denoise"] = ("비-로컬 평균(NLM) 알고리즘으로 고급 노이즈를 제거합니다. 반복 패턴을 활용하여 우수한 노이즈 제거 성능을 제공합니다.",
            "고품질 노이즈 제거, 저조도 이미지 개선, 의료/과학 영상 처리"),
        ["Inpaint"] = ("마스크로 지정된 손상 영역을 주변 정보를 기반으로 복원합니다.",
            "이미지 복원, 로고/텍스트 제거, 스크래치 보수, 결함 영역 메우기"),
        ["Normalize"] = ("이미지 강도 범위를 정규화합니다. 히스토그램 스트레칭과 유사한 효과를 줍니다.",
            "콘트라스트 향상, 밝기 정규화, 이미지 비교 전 전처리"),
        ["LUT"] = ("룩업 테이블(LUT)을 적용하여 감마, 밝기, 대비를 조정합니다.",
            "밝기/대비 조정, 감마 보정, 이미지 톤 조절, 카메라 영상 보정"),

        // ── Edge ──
        ["Canny Edge"] = ("캐니 에지 검출을 수행합니다. 두 개의 임계값을 사용하여 약한 에지와 강한 에지를 구분합니다.",
            "윤곽 검출, 물체 경계 추출, 형상 분석 전처리, 문서 경계 검출"),
        ["Sobel Edge"] = ("소벨 에지 검출을 수행합니다. X 또는 Y 방향의 미분을 계산하여 에지를 검출합니다.",
            "방향별 에지 분석, 수평/수직 라인 검출, 그래디언트 계산"),
        ["Laplacian Edge"] = ("라플라시안 에지 검출을 수행합니다. 2차 미분으로 모든 방향의 에지를 동시에 검출합니다.",
            "전방위 에지 검출, 블러 정도 측정, 포커스 품질 평가"),
        ["Scharr Edge"] = ("샤르 에지 검출을 수행합니다. 소벨보다 작은 커널에서 더 정확한 결과를 제공합니다.",
            "정밀 에지 검출, 그래디언트 방향 분석, 미세 에지 검출"),

        // ── Morphology ──
        ["Erode"] = ("침식(Erosion) 연산을 적용합니다. 밝은 영역을 축소하고 어두운 영역을 확장합니다.",
            "노이즈 제거, 객체 분리, 가는 연결 끊기, 이진 이미지 정리"),
        ["Dilate"] = ("팽창(Dilation) 연산을 적용합니다. 밝은 영역을 확장하고 어두운 영역을 축소합니다.",
            "객체 연결, 구멍 메우기, 에지 강화, 글자 굵기 증가"),
        ["Morphology Ex"] = ("고급 모폴로지 연산(열림, 닫힘, 그래디언트, 톱햇, 블랙햇)을 수행합니다.",
            "배경 추출, 조명 보정(톱햇), 미세 구조 검출, 노이즈 제거(열림/닫힘)"),

        // ── Threshold ──
        ["Threshold"] = ("고정 임계값으로 이미지를 이진화합니다. Binary, BinaryInv, Trunc, ToZero 등의 타입을 지원합니다.",
            "객체 분리, 배경 제거, 문서 이진화, 간단한 세그멘테이션"),
        ["Adaptive Threshold"] = ("적응형 임계값을 적용합니다. 이미지의 각 영역에 맞춰 다른 임계값을 사용하여 조명 불균일 환경에서 효과적입니다.",
            "조명 변화가 있는 환경의 이진화, 문서 스캔, 불균일 배경 처리"),
        ["Otsu Threshold"] = ("오쓰(Otsu) 알고리즘으로 최적의 임계값을 자동 결정하여 이진화합니다.",
            "자동 임계값 결정, 히스토그램이 이봉형인 이미지, 범용 이진화"),

        // ── Contour ──
        ["Find Contours"] = ("이진 이미지에서 윤곽선(외곽선)을 검출합니다. 다양한 검색 모드와 근사화 방법을 지원합니다.",
            "객체 검출, 형상 분석의 기본 단계, 물체 카운팅 전처리"),
        ["Draw Contours"] = ("검출된 윤곽선을 이미지 위에 색상과 두께를 지정하여 그립니다.",
            "윤곽선 시각화, 검출 결과 표시, 디버깅용 오버레이"),
        ["Contour Area"] = ("각 윤곽선의 면적을 계산합니다.", "면적 기반 필터링, 크기 측정, 객체 분류"),
        ["Contour Centers"] = ("모멘트를 이용하여 각 윤곽선의 중심점(무게중심)을 찾습니다.",
            "객체 위치 추적, 정렬 기준점 설정, 중심 좌표 기반 측정"),
        ["Contour Filter"] = ("면적과 둘레 범위로 윤곽선을 필터링합니다. 노이즈 제거와 원하는 크기의 객체만 선택할 수 있습니다.",
            "노이즈 윤곽 제거, 특정 크기 객체 선택, 검사 대상 필터링"),
        ["Bounding Rect"] = ("각 윤곽선의 축 정렬 바운딩 박스(사각형)를 계산합니다.",
            "객체 위치/크기 추출, ROI 자동 설정, 바운딩 박스 기반 분석"),
        ["Approx Poly"] = ("윤곽선을 다각형으로 근사화합니다. epsilon 값으로 근사화 정밀도를 조절합니다.",
            "형상 단순화, 꼭짓점 수 기반 형상 분류, 도형 인식"),
        ["Convex Hull"] = ("각 윤곽선의 볼록 껍질을 계산합니다.", "볼록성 분석, 결함 검출 보조, 형상 특성 추출"),
        ["Min Enclosing Circle"] = ("각 윤곽선을 감싸는 최소 원을 계산하여 중심과 반지름을 출력합니다.",
            "원형 객체 측정, 최소 외접원 기반 크기 비교, 원형도 분석"),
        ["Fit Ellipse"] = ("각 윤곽선에 최적 타원을 적합(fit)합니다. 최소 5개 점이 필요합니다.",
            "타원형 객체 분석, 회전 각도 측정, 형상 특성 추출"),
        ["Min Area Rect"] = ("각 윤곽선의 최소 면적 회전 사각형을 계산합니다.",
            "회전된 객체의 크기/각도 측정, 방향 분석, 정밀 치수 측정"),
        ["Moments"] = ("각 윤곽선의 이미지 모멘트를 계산합니다. 면적, 중심점 좌표를 출력합니다.",
            "무게중심 계산, 형상 기술자 추출, 면적 계산"),
        ["Match Shapes"] = ("형상 매칭으로 윤곽선간의 유사도를 비교합니다. Hu 모멘트 기반으로 회전/크기 불변 비교가 가능합니다.",
            "형상 기반 분류, 템플릿 형상 비교, 불량 형상 검출"),

        // ── Feature ──
        ["FAST Features"] = ("FAST 코너 검출을 수행합니다. 매우 빠른 속도로 코너점을 검출합니다.",
            "실시간 특징점 검출, 영상 추적 전처리, 빠른 관심점 추출"),
        ["Good Features To Track"] = ("Shi-Tomasi 코너 검출을 수행합니다. 추적에 적합한 특징점을 선별합니다.",
            "광학 흐름 추적용 특징점, 움직임 분석, 안정적인 관심점 추출"),
        ["Harris Corner"] = ("해리스 코너 검출을 수행합니다. 에지와 코너를 구분하는 고전적인 알고리즘입니다.",
            "코너 검출, 특징점 기반 정렬, 기하학적 특징 추출"),
        ["ORB Features"] = ("ORB(Oriented FAST and Rotated BRIEF) 특징점을 검출하고 기술자를 생성합니다. 특허 무료입니다.",
            "실시간 특징 매칭, 객체 인식, 이미지 스티칭, AR 마커 인식"),
        ["SIFT Features"] = ("SIFT(Scale-Invariant Feature Transform) 특징점을 검출합니다. 스케일과 회전에 불변인 강력한 특징점입니다.",
            "파노라마 스티칭, 정밀 객체 인식, 3D 복원, 이미지 검색"),
        ["Shi-Tomasi Corners"] = ("Shi-Tomasi 방법으로 코너를 검출합니다. Harris보다 안정적인 결과를 제공합니다.",
            "코너 기반 추적, 움직임 분석, 특징점 추출"),
        ["Simple Blob Detector"] = ("SimpleBlobDetector로 블롭(덩어리)을 검출합니다. 면적, 원형도, 볼록도, 관성비로 필터링합니다.",
            "점/구멍 검출, 세포 카운팅, 결함 점 검출, 마커 검출"),
        ["Match Features"] = ("두 이미지의 특징 기술자를 매칭합니다. BruteForce, Hamming, FLANN 매처를 지원합니다.",
            "이미지 유사도 비교, 스티칭, 객체 재인식, 위치 추정"),

        // ── Drawing ──
        ["Draw Line"] = ("이미지 위에 직선을 그립니다.", "측정선 표시, 참조선 그리기, 결과 오버레이"),
        ["Draw Circle"] = ("이미지 위에 원을 그립니다. 채움(-1) 또는 윤곽만 그릴 수 있습니다.",
            "검출 위치 마킹, ROI 표시, 결과 시각화"),
        ["Draw Rectangle"] = ("이미지 위에 사각형을 그립니다.", "ROI 영역 표시, 바운딩 박스 표시, 검사 영역 표시"),
        ["Draw Ellipse"] = ("이미지 위에 회전 가능한 타원을 그립니다.",
            "타원형 검출 결과 표시, 회전 객체 시각화, 피팅 결과 표시"),
        ["Draw Text"] = ("이미지 위에 텍스트를 그립니다. 다양한 폰트, 크기, 색상을 설정할 수 있습니다.",
            "검사 결과 라벨링, 측정값 표시, 상태 정보 오버레이"),
        ["Draw Grid"] = ("이미지 위에 격자(그리드) 오버레이를 그립니다.",
            "정렬 검사, 위치 참조용 격자, 구역 분할 시각화"),
        ["Draw Crosshair"] = ("이미지 위에 십자선(크로스헤어)을 그립니다. 자동 중심 또는 지정 위치를 지원합니다.",
            "중심점 마킹, 정렬 기준 표시, 카메라 보정 보조"),
        ["Draw Polylines"] = ("이미지 위에 다각형 선을 그립니다. 열린/닫힌 폴리라인을 지원합니다.",
            "윤곽선 시각화, 경로 표시, 커스텀 도형 그리기"),
        ["Draw Bounding Boxes"] = ("Rect 배열로부터 바운딩 박스를 그립니다. 선택적으로 치수 라벨을 표시합니다.",
            "객체 검출 결과 시각화, 연결 컴포넌트 표시, 검사 영역 표시"),
        ["Draw Contours Info"] = ("윤곽선에 중심점, 면적, 인덱스 등의 정보 라벨을 함께 그립니다.",
            "윤곽선 분석 결과 시각화, 디버깅, 객체별 정보 표시"),

        // ── Transform ──
        ["Resize"] = ("이미지 크기를 절대 치수 또는 비율로 변경합니다. 다양한 보간법을 지원합니다.",
            "입력 이미지 크기 통일, 처리 속도 향상을 위한 축소, 출력 크기 조정"),
        ["Rotate"] = ("이미지를 지정 각도로 회전합니다. 자동 중심 또는 사용자 지정 중심을 지원합니다.",
            "기울기 보정, 방향 정렬, 회전된 객체 정규화"),
        ["Crop"] = ("이미지에서 지정된 영역(ROI)을 잘라냅니다.",
            "관심 영역 추출, 부분 이미지 분석, 검사 대상 분리"),
        ["Flip"] = ("이미지를 수평, 수직 또는 양방향으로 뒤집습니다.",
            "미러링 보정, 좌우 반전, 카메라 영상 보정"),
        ["Warp Affine"] = ("3쌍의 대응점으로 아핀 변환을 적용합니다. 이동, 회전, 스케일링, 기울이기가 가능합니다.",
            "이미지 정렬, 기하학적 보정, 문서 정규화"),
        ["Warp Perspective"] = ("4쌍의 대응점으로 원근(투시) 변환을 적용합니다.",
            "문서 스캔 보정, 조감도(Bird's eye view) 변환, 사다리꼴 보정"),
        ["Pyramid"] = ("이미지 피라미드 연산(확대 또는 축소)을 수행합니다.",
            "다중 해상도 분석, 이미지 스케일링, 피라미드 기반 처리"),
        ["Distance Transform"] = ("이진 이미지의 거리 변환을 수행합니다. 각 픽셀에서 가장 가까운 0 픽셀까지의 거리를 계산합니다.",
            "워터셰드 마커 생성, 객체 분리, 스켈레톤 추출 보조"),

        // ── Histogram ──
        ["Calc Histogram"] = ("이미지의 히스토그램을 계산하고 시각화합니다.",
            "밝기 분포 분석, 이미지 품질 평가, 이진화 임계값 결정 보조"),
        ["Equalize Histogram"] = ("히스토그램 평활화를 적용하여 콘트라스트를 개선합니다.",
            "콘트라스트 향상, 어두운 이미지 개선, 얼굴 인식 전처리"),
        ["CLAHE"] = ("제한된 대비 적응형 히스토그램 평활화(CLAHE)를 적용합니다. 지역별로 독립적인 평활화를 수행합니다.",
            "조명 불균일 보정, 의료 영상 향상, 지역적 콘트라스트 개선"),
        ["Calc Back Project"] = ("히스토그램 역투영을 수행하여 대상 색상과 유사한 영역을 찾습니다.",
            "색상 기반 객체 추적, 피부색 검출, 관심 색상 영역 강조"),

        // ── Arithmetic ──
        ["Add"] = ("두 이미지를 픽셀 단위로 더합니다.", "이미지 합성, 밝기 증가, 마스크 결합"),
        ["Subtract"] = ("두 이미지를 픽셀 단위로 뺍니다.", "배경 제거, 변화 검출, 차이 분석"),
        ["Multiply"] = ("두 이미지를 픽셀 단위로 곱합니다.", "마스크 적용, 가중치 맵 적용, 텍스처 블렌딩"),
        ["Abs Diff"] = ("두 이미지의 절대 차이를 계산합니다.", "변화 검출, 결함 비교, 차분 이미지 생성"),
        ["Bitwise AND"] = ("두 이미지의 비트 AND 연산을 수행합니다.", "마스크 적용, 공통 영역 추출, 논리 결합"),
        ["Bitwise OR"] = ("두 이미지의 비트 OR 연산을 수행합니다.", "마스크 합집합, 영역 결합, 다중 마스크 병합"),
        ["Bitwise XOR"] = ("두 이미지의 비트 XOR 연산을 수행합니다.", "차이점 하이라이트, 변경 영역 검출"),
        ["Bitwise NOT"] = ("이미지의 비트 NOT(반전) 연산을 수행합니다.", "이미지 반전, 마스크 반전, 네거티브 이미지"),
        ["Weighted Add"] = ("가중 합(알파 블렌딩)을 수행합니다. result = α×A + β×B + γ.",
            "이미지 블렌딩, 투명도 합성, 오버레이 효과"),
        ["Mask Apply"] = ("마스크를 이미지에 적용합니다. 비트 AND로 마스크 영역만 남깁니다.",
            "ROI 추출, 관심 영역만 표시, 배경 제거"),
        ["Image Blend"] = ("두 이미지를 알파값으로 블렌딩합니다. result = α×A + (1-α)×B.",
            "이미지 합성, 투명도 조절, 오버레이 효과"),

        // ── Detection ──
        ["Hough Lines"] = ("허프 변환으로 직선을 검출합니다. 확률적 허프 변환(HoughLinesP)을 사용합니다.",
            "직선 검출, 차선 인식, 에지 정렬 분석, 문서 기울기 검출"),
        ["Hough Circles"] = ("허프 변환으로 원을 검출합니다.", "원형 부품 검출, 동전 검출, 구멍 검출, 홍채 검출"),
        ["Template Match"] = ("템플릿 매칭으로 이미지에서 패턴을 찾습니다. 최적 매치 1개를 반환합니다.",
            "패턴 검색, 부품 위치 확인, 마크 검출, 로고 인식"),
        ["Template Match Multi"] = ("NMS를 적용하여 이미지에서 다수의 템플릿 매치를 찾습니다.",
            "반복 패턴 검출, 다수 부품 위치 확인, 배열 검사"),
        ["Haar Cascade"] = ("Haar 캐스케이드 분류기로 객체를 검출합니다. 사전 학습된 XML 파일을 사용합니다.",
            "얼굴 검출, 눈 검출, 사람 전신 검출, 차량 검출"),
        ["Min Max Loc"] = ("이미지의 최소/최대 픽셀 값과 그 위치를 찾습니다.",
            "밝은/어두운 점 검출, 템플릿 매칭 결과 분석, 극값 탐색"),
        ["Pixel Count"] = ("비영(non-zero) 또는 임계값 이상의 픽셀 수를 세고 비율을 계산합니다.",
            "영역 비율 측정, 채우기 정도 확인, 존재 유무 판단"),
        ["Line Profile"] = ("지정한 직선 위의 픽셀 강도를 측정하여 프로파일을 생성합니다.",
            "에지 품질 분석, 선폭 측정, 강도 분포 분석, 단면 프로파일"),
        ["Connected Components"] = ("연결 성분 라벨링을 수행합니다. 각 독립 영역에 고유 라벨을 부여하고 통계를 제공합니다.",
            "개별 객체 식별, 카운팅, 영역별 분석, 결함 개별 식별"),

        // ── Segmentation ──
        ["Flood Fill"] = ("시드 포인트에서 시작하여 유사한 색상/밝기의 연결 영역을 채웁니다.",
            "영역 채우기, 배경 교체, 연결 영역 마킹"),
        ["GrabCut"] = ("GrabCut 알고리즘으로 전경/배경을 분리합니다. ROI 사각형으로 초기 영역을 지정합니다.",
            "객체 분리, 배경 제거, 전경 추출, 이미지 편집"),
        ["Watershed"] = ("워터셰드 알고리즘으로 이미지를 세그먼트합니다. 겹치는 객체의 분리에 효과적입니다.",
            "접촉/겹침 객체 분리, 세포 분리, 입자 분석"),

        // ── Control ──
        ["Boolean"] = ("제어 흐름에 사용되는 불리언 상수를 출력합니다.",
            "조건부 실행 플래그, 기능 활성화/비활성화, 디버그 토글"),
        ["Compare"] = ("두 숫자를 비교하여 불리언 결과를 출력합니다.",
            "검사 판정, 임계값 비교, 범위 확인"),
        ["Boolean Logic"] = ("두 불리언 값에 대한 논리 연산을 수행합니다.",
            "복합 조건 로직, 다중 검사 결과 결합, 알람 조건"),
        ["If Select"] = ("조건에 따라 두 값 중 하나를 선택하여 출력합니다.",
            "조건부 경로 분기, 합격/불합격별 다른 처리, 동적 파라미터 선택"),
        ["Switch"] = ("인덱스에 따라 여러 입력 중 하나를 선택합니다. 최대 8개 입력을 지원합니다.",
            "다중 선택, 모드 전환, 다중 이미지 소스 선택"),
        ["For Loop"] = ("시작부터 끝까지 순차적으로 인덱스를 생성합니다. ForLoopNode은 단순 카운터 방식입니다.",
            "반복 카운터, 인덱스 생성, 순차 처리"),
        ["For"] = ("시작~끝 범위의 for 루프를 실행합니다. 하위 노드들을 반복 실행합니다.",
            "배치 이미지 처리, 반복 검사, 파라미터 스위핑"),
        ["ForEach"] = ("컬렉션의 각 요소에 대해 하위 노드를 반복 실행합니다.",
            "배열 요소별 처리, 컨투어 개별 분석, 검출 결과 순회"),
        ["While"] = ("BreakIf로 중단할 때까지 반복 실행합니다.",
            "수렴 조건까지 반복, 동적 종료 조건, 반복 최적화"),
        ["BreakIf"] = ("조건이 참이면 현재 루프를 중단합니다.",
            "루프 조기 종료, 에러 시 중단, 조건 충족 시 종료"),
        ["Collect"] = ("루프 반복의 결과를 배열로 수집합니다.",
            "루프 결과 집계, 다중 측정값 수집, 배치 결과 저장"),
        ["Delay"] = ("지정된 밀리초 동안 실행을 지연합니다. 런타임 모드에서만 동작합니다.",
            "타이밍 제어, 프레임 간격 조절, 순차 제어 대기"),

        // ── Data ──
        ["String to Number"] = ("문자열을 숫자(double)로 변환합니다.",
            "통신 수신 데이터 파싱, CSV 데이터 변환, 사용자 입력 처리"),
        ["Number to String"] = ("숫자를 포맷 문자열(F2, N0 등)로 변환합니다.",
            "결과 표시 포맷팅, 로그 생성, 리포트 값 포맷"),
        ["CSV Reader"] = ("디스크에서 CSV 파일을 읽어 데이터 배열과 헤더를 출력합니다.",
            "검사 데이터 로드, 설정 파일 읽기, 배치 처리 목록 로드"),
        ["CSV Parser"] = ("입력 문자열에서 CSV를 파싱합니다.",
            "통신으로 수신한 CSV 처리, 동적 데이터 파싱"),
        ["String Split"] = ("구분자로 문자열을 분할합니다.", "데이터 파싱, 통신 프로토콜 처리, 필드 추출"),
        ["String Join"] = ("문자열 배열을 구분자로 결합합니다.", "데이터 조합, 전송 메시지 생성, CSV 행 생성"),
        ["String Replace"] = ("문자열에서 찾기/바꾸기를 수행합니다.", "데이터 정제, 포맷 변환, 특수문자 처리"),

        // ── Communication ──
        ["Serial Port"] = ("시리얼(COM) 포트 통신을 수행합니다. 백그라운드 수신을 지원하며 ASCII/HEX 모드를 제공합니다.",
            "PLC 통신, 센서 데이터 수신, 로봇 제어, 산업용 장비 연동"),
        ["TCP Server"] = ("TCP 서버를 구동하여 클라이언트 연결을 받아들이고 데이터를 송수신합니다.",
            "MES 연동, 외부 시스템과 통신, 원격 모니터링 서버"),
        ["TCP Client"] = ("TCP 클라이언트로 서버에 연결하여 데이터를 송수신합니다.",
            "MES 클라이언트, PLC 이더넷 통신, 외부 서비스 연동"),

        // ── Event ──
        ["Keyboard Event"] = ("Image Show 윈도우에서 키보드 이벤트를 수신합니다. 키 코드, 이름, 눌림 상태를 출력합니다.",
            "키보드 트리거, 인터랙티브 제어, 모드 전환"),
        ["Mouse Event"] = ("Image Show 윈도우에서 마우스 이벤트를 수신합니다. 좌표, 이벤트 타입, 버튼 정보를 출력합니다.",
            "마우스 좌표 추적, 클릭 기반 선택, 인터랙티브 파라미터 조정"),
        ["Mouse ROI"] = ("Image Show 윈도우에서 마우스 드래그로 ROI 사각형을 그립니다.",
            "동적 ROI 설정, 인터랙티브 영역 선택, 실시간 관심 영역 지정"),
        ["WaitKey"] = ("키 입력을 대기합니다. IStreamingSource를 구현하여 런타임 루프를 유지합니다.",
            "키 입력 대기, 실행 흐름 제어, cv2.waitKey()와 동일한 기능"),

        // ── Script ──
        ["Python Script"] = ("시스템 Python으로 스크립트를 실행합니다. 입력 이미지를 전달하고 결과 이미지를 받을 수 있습니다.",
            "커스텀 알고리즘, 딥러닝 모델 실행, 외부 라이브러리 활용, 프로토타이핑"),
        ["C# Script"] = ("OpenCvSharp을 포함한 C# 스크립트를 실행합니다. .NET SDK가 필요합니다.",
            "커스텀 C# 알고리즘, 복잡한 처리 로직, .NET 라이브러리 활용"),

        // ── Inspection ──
        ["Alignment Checker"] = ("객체의 방향 각도를 측정하여 정렬 상태를 검사합니다. 기대 각도와 허용 오차로 합격/불합격을 판정합니다.",
            "부품 정렬 검사, PCB 방향 확인, 조립 정렬도 검사, 로봇 그리퍼 정렬"),
        ["Brightness Uniformity"] = ("격자로 나눈 이미지의 밝기 균일성을 검사합니다. 평균, 표준편차, 최소/최대 밝기를 분석합니다.",
            "디스플레이 균일성 검사, 조명 품질 확인, 백라이트 검사, 도장 균일성"),
        ["Circle Detector"] = ("허프 변환으로 원형 객체를 검출하고 중심과 반지름을 출력합니다.",
            "원형 부품 검출, 구멍 검사, 동전/캡 검출, O-링 검사"),
        ["Color Object Detector"] = ("HSV 색상 범위로 특정 색상의 객체를 검출합니다. 바운딩 박스와 중심을 출력합니다.",
            "색상별 부품 분류, 컬러 마커 검출, 식품 색상 검사"),
        ["Contour Center Finder"] = ("이미지에서 객체의 중심을 자동으로 찾는 완전한 파이프라인입니다. 블러→이진화→윤곽선→중심 계산을 자동 수행합니다.",
            "객체 위치 측정, 정렬 기준점 추출, 자동화된 중심 검출"),
        ["Defect Detector"] = ("기준 이미지와 비교하여 결함(차이)을 검출합니다. 차이 마스크와 결함 바운딩 박스를 출력합니다.",
            "참조 비교 검사, 외관 결함 검출, PCB 결함 검사, 인쇄 불량 검출"),
        ["Edge Inspector"] = ("허프 직선 검출로 에지/라인을 찾아 정렬 상태를 분석합니다. 각도 정보를 출력합니다.",
            "에지 정렬 검사, 직선도 측정, 가이드 레일 정렬"),
        ["Face Detector"] = ("Haar 캐스케이드로 인간 얼굴을 검출합니다.",
            "출입 관리, 얼굴 기반 트리거, 인원 카운팅, 비전 기반 HMI"),
        ["Object Counter"] = ("이미지에서 객체를 자동 카운팅합니다. 블러, 이진화, 윤곽선 검출, 면적 필터를 자동 수행합니다.",
            "부품 카운팅, 제품 수량 확인, 결함 개수 세기, 재고 관리"),
        ["Pattern Matcher"] = ("템플릿 매칭+NMS로 패턴의 모든 발생 위치를 찾고 합격/불합격을 판정합니다.",
            "마크 검사, 패턴 유무 검사, 반복 패턴 검증"),
        ["Presence Checker"] = ("ROI 영역 내의 채워진 비율을 측정하여 객체 존재 여부를 판단합니다.",
            "부품 유무 확인, 조립 완성도 검사, 나사 유무 확인"),
        ["Scratch Detector"] = ("모폴로지(TopHat/BlackHat)를 활용하여 표면의 선형 스크래치를 검출합니다.",
            "표면 스크래치 검사, 유리/금속 표면 결함 검출, 페인트 긁힘 검출"),
        ["Shape Classifier"] = ("검출된 윤곽선을 원, 사각형, 삼각형, 정사각형, 다각형으로 분류합니다.",
            "형상 기반 부품 분류, 도형 인식, 형상 품질 검사"),

        // ── Measurement ──
        ["Angle Measure"] = ("허프 직선 검출로 두 주요 직선 사이의 각도를 측정합니다.",
            "조립 각도 확인, 직각도 검사, 기울기 측정, 경사 분석"),
        ["Distance Measure"] = ("두 검출 객체의 중심 간 거리를 측정합니다. 픽셀/실제 단위 변환을 지원합니다.",
            "부품 간격 측정, 위치 정확도 확인, 갭(gap) 측정, 피치 측정"),
        ["Object Measure"] = ("윤곽선 분석으로 객체의 폭과 높이를 측정합니다. 실제 단위 변환을 지원합니다.",
            "부품 치수 측정, 크기 검사, 폭/높이 자동 측정"),

        // ── MediaPipe ──
        ["MP Face Detection"] = ("MediaPipe BlazeFace 모델로 얼굴을 검출합니다. 바운딩 박스와 신뢰도를 출력합니다.",
            "실시간 얼굴 검출, 출입 관리, 인원 카운팅, 얼굴 기반 트리거"),
        ["MP Face Mesh"] = ("MediaPipe Face Mesh로 468개의 얼굴 랜드마크를 검출합니다.",
            "표정 분석, 얼굴 AR 필터, 시선 추적, 감정 인식 기초"),
        ["MP Hand Landmark"] = ("MediaPipe로 21개의 손 랜드마크를 검출합니다. 최대 4개 손까지 동시 검출합니다.",
            "제스처 인식, 손동작 기반 제어, 수화 인식, AR 인터랙션"),
        ["MP Pose Landmark"] = ("MediaPipe BlazePose로 33개의 신체 포즈 랜드마크를 검출합니다. 가시성 점수를 출력합니다.",
            "자세 분석, 운동 폼 분석, 안전 자세 모니터링, 동작 인식"),
        ["MP Object Detection"] = ("SSD MobileNet V2로 COCO 80개 클래스의 객체를 검출합니다.",
            "범용 객체 검출, 물체 인식, 장면 이해, 자동 분류"),
        ["MP Selfie Segmentation"] = ("MediaPipe로 사람/배경을 분리합니다. 블러, 제거, 그린스크린 모드를 지원합니다.",
            "배경 블러/교체, 비디오 컨퍼런스, 증강현실, 포토 편집"),

        // ── YOLO ──
        ["YOLOv8 Detection"] = ("YOLOv8 ONNX 모델로 객체를 검출합니다. 모델에서 클래스 수를 자동 감지하며, NMS를 적용합니다.",
            "실시간 객체 검출, 다중 객체 인식, 산업 부품 검출, 보안 모니터링"),

        // ── OCR ──
        ["PaddleOCR"] = ("PaddleOCR(DB+CTC)로 다국어 텍스트를 검출하고 인식합니다. 한국어, 영어, 중국어 등을 지원합니다.",
            "문서 OCR, 라벨 텍스트 읽기, 시리얼 넘버 인식, 자동 데이터 입력"),
        ["Tesseract OCR"] = ("Tesseract 엔진으로 텍스트를 인식합니다. 100+ 언어를 지원하며 다양한 페이지 분할 모드를 제공합니다.",
            "인쇄 문서 OCR, 번호판 인식, 다국어 텍스트 인식"),

        // ── LLM/VLM ──
        ["Claude Vision"] = ("Anthropic Claude API로 이미지를 분석합니다. 이미지와 프롬프트를 전송하고 자연어 응답을 받습니다.",
            "이미지 분석 리포트, 결함 설명 생성, 장면 설명, AI 기반 품질 평가"),
        ["Gemini Vision"] = ("Google Gemini API로 이미지를 분석합니다.",
            "이미지 설명 생성, 복합 분석, 문서 이해, AI 검사 어시스턴트"),
        ["OpenAI Vision"] = ("OpenAI GPT-4o API로 이미지를 분석합니다.",
            "이미지 캡셔닝, 결함 분석, 품질 평가 리포트, 시각적 질의응답"),
    };

    // Korean category names
    private static readonly Dictionary<string, string> CategoryKoreanNames = new()
    {
        [NodeCategories.Input] = "입출력 (Input/Output)",
        [NodeCategories.Color] = "색상 (Color)",
        [NodeCategories.Filter] = "필터 (Filter)",
        [NodeCategories.Edge] = "에지 검출 (Edge Detection)",
        [NodeCategories.Morphology] = "모폴로지 (Morphology)",
        [NodeCategories.Threshold] = "임계값 (Threshold)",
        [NodeCategories.Contour] = "윤곽선 (Contour)",
        [NodeCategories.Feature] = "특징점 (Feature Detection)",
        [NodeCategories.Drawing] = "그리기 (Drawing)",
        [NodeCategories.Transform] = "변환 (Transform)",
        [NodeCategories.Histogram] = "히스토그램 (Histogram)",
        [NodeCategories.Arithmetic] = "연산 (Arithmetic)",
        [NodeCategories.Detection] = "검출 (Detection)",
        [NodeCategories.Segmentation] = "분할 (Segmentation)",
        [NodeCategories.Value] = "값 (Value)",
        [NodeCategories.Control] = "제어 (Control)",
        [NodeCategories.Communication] = "통신 (Communication)",
        [NodeCategories.Data] = "데이터 처리 (Data Processing)",
        [NodeCategories.Event] = "이벤트 (Event)",
        [NodeCategories.Script] = "스크립트 (Script)",
        [NodeCategories.Inspection] = "검사 (Inspection)",
        [NodeCategories.Measurement] = "측정 (Measurement)",
        [NodeCategories.MediaPipe] = "MediaPipe",
        [NodeCategories.YOLO] = "YOLO",
        [NodeCategories.OCR] = "OCR",
        [NodeCategories.LLMVLM] = "LLM/VLM (AI 비전)",
        [NodeCategories.Function] = "함수 (Function)",
    };

    private bool _built;

    public NodeReferenceView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            if (!_built) BuildNodeReference();
        };
    }

    private void BuildNodeReference()
    {
        _built = true;
        try
        {
            var registry = new NodeRegistry();
            var nodesAssembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "MVXTester.Nodes");
            if (nodesAssembly == null) return;

            registry.RegisterAssembly(nodesAssembly);
            var categories = registry.GetByCategory();

            foreach (var kvp in categories)
            {
                if (kvp.Key == NodeCategories.Function) continue; // Skip dynamic function nodes
                var section = CreateCategorySection(kvp.Key, kvp.Value);
                MainPanel.Children.Add(section);
            }
        }
        catch { /* Silently handle if nodes assembly not loaded */ }
    }

    private UIElement CreateCategorySection(string category, List<NodeRegistryEntry> entries)
    {
        var color = CategoryColors.GetValueOrDefault(category, "#CDD6F4");
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        var korName = CategoryKoreanNames.GetValueOrDefault(category, category);

        var expander = new Expander
        {
            IsExpanded = false,
            Margin = new Thickness(0, 0, 0, 8),
            Header = CreateCategoryHeader(korName, entries.Count, brush),
        };

        var panel = new StackPanel { Margin = new Thickness(12, 4, 0, 0) };

        foreach (var entry in entries)
        {
            try
            {
                var node = (BaseNode)Activator.CreateInstance(entry.NodeType)!;
                panel.Children.Add(CreateNodeEntry(node, entry, brush));
            }
            catch { /* Skip nodes that can't be instantiated */ }
        }

        expander.Content = panel;
        return expander;
    }

    private UIElement CreateCategoryHeader(string name, int count, SolidColorBrush accent)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        sp.Children.Add(new Border
        {
            Width = 4, Height = 18, CornerRadius = new CornerRadius(2),
            Background = accent, Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
        });
        sp.Children.Add(new TextBlock
        {
            Text = $"{name}  ({count})",
            FontSize = 14, FontWeight = FontWeights.SemiBold,
            Foreground = FindBrush("ForegroundBrush"),
            VerticalAlignment = VerticalAlignment.Center,
        });
        return sp;
    }

    private UIElement CreateNodeEntry(BaseNode node, NodeRegistryEntry entry, SolidColorBrush categoryBrush)
    {
        var container = new Border
        {
            Margin = new Thickness(0, 0, 0, 16),
            BorderThickness = new Thickness(0, 0, 0, 1),
            BorderBrush = FindBrush("BorderBrush"),
            Padding = new Thickness(0, 0, 0, 12),
        };

        var stack = new StackPanel();

        // Node Name + Category
        var nameBlock = new TextBlock
        {
            FontSize = 13, FontWeight = FontWeights.SemiBold,
            Foreground = categoryBrush, Margin = new Thickness(0, 0, 0, 2),
        };
        nameBlock.Inlines.Add(new Run(entry.Name));
        nameBlock.Inlines.Add(new Run($"  ({entry.Category})") { FontSize = 10, Foreground = FindBrush("ForegroundDimBrush") });
        stack.Children.Add(nameBlock);

        // English Description
        if (!string.IsNullOrEmpty(entry.Description))
        {
            stack.Children.Add(new TextBlock
            {
                Text = entry.Description,
                FontSize = 10.5, FontStyle = FontStyles.Italic,
                Foreground = FindBrush("ForegroundDimBrush"),
                Margin = new Thickness(0, 0, 0, 6),
                TextWrapping = TextWrapping.Wrap,
            });
        }

        // Visual Node Diagram
        stack.Children.Add(CreateNodeDiagram(node, categoryBrush));

        // Port Table
        if (node.Inputs.Count > 0 || node.Outputs.Count > 0)
        {
            stack.Children.Add(CreateSectionHeader("포트 (Ports)"));
            stack.Children.Add(CreatePortTable(node));
        }

        // Property Table
        if (node.Properties.Count > 0)
        {
            stack.Children.Add(CreateSectionHeader("속성 (Properties)"));
            stack.Children.Add(CreatePropertyTable(node));
        }

        // Korean Description
        if (KoreanDescriptions.TryGetValue(entry.Name, out var kr))
        {
            stack.Children.Add(CreateSectionHeader("기능"));
            stack.Children.Add(new TextBlock
            {
                Text = kr.Desc, FontSize = 11.5, TextWrapping = TextWrapping.Wrap,
                Foreground = FindBrush("ForegroundBrush"),
                Margin = new Thickness(0, 0, 0, 4), LineHeight = 18,
            });

            stack.Children.Add(CreateSectionHeader("응용 분야"));
            stack.Children.Add(new TextBlock
            {
                Text = kr.Apps, FontSize = 11.5, TextWrapping = TextWrapping.Wrap,
                Foreground = FindBrush("ForegroundBrush"),
                Margin = new Thickness(0, 0, 0, 4), LineHeight = 18,
            });
        }

        container.Child = stack;
        return container;
    }

    private UIElement CreateNodeDiagram(BaseNode node, SolidColorBrush categoryBrush)
    {
        var diagram = new Border
        {
            Background = FindBrush("HighlightBrush"),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(14, 10, 14, 10),
            Margin = new Thickness(0, 4, 0, 8),
        };

        var grid = new Grid { HorizontalAlignment = HorizontalAlignment.Center };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // inputs
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // arrow
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // node
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // arrow
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // outputs

        // Input ports column
        if (node.Inputs.Count > 0)
        {
            var inputPanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 0) };
            foreach (var input in node.Inputs)
            {
                inputPanel.Children.Add(CreatePortBadge(input.Name, input.DataType, true));
            }
            Grid.SetColumn(inputPanel, 0);
            grid.Children.Add(inputPanel);

            var arrow1 = new TextBlock
            {
                Text = "\u2192", FontSize = 14, VerticalAlignment = VerticalAlignment.Center,
                Foreground = categoryBrush, Margin = new Thickness(6, 0, 6, 0),
            };
            Grid.SetColumn(arrow1, 1);
            grid.Children.Add(arrow1);
        }

        // Center node box
        var nodeBox = new Border
        {
            CornerRadius = new CornerRadius(5),
            BorderThickness = new Thickness(2),
            BorderBrush = categoryBrush,
            Background = new SolidColorBrush(Color.FromArgb(48, categoryBrush.Color.R, categoryBrush.Color.G, categoryBrush.Color.B)),
            Padding = new Thickness(14, 6, 14, 6),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var nodeBoxPanel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        nodeBoxPanel.Children.Add(new TextBlock
        {
            Text = node.Name, FontSize = 11, FontWeight = FontWeights.SemiBold,
            Foreground = FindBrush("ForegroundBrush"), HorizontalAlignment = HorizontalAlignment.Center,
        });
        nodeBoxPanel.Children.Add(new TextBlock
        {
            Text = node.Category, FontSize = 8,
            Foreground = FindBrush("ForegroundDimBrush"), HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 1, 0, 0),
        });
        nodeBox.Child = nodeBoxPanel;
        Grid.SetColumn(nodeBox, 2);
        grid.Children.Add(nodeBox);

        // Output ports column
        if (node.Outputs.Count > 0)
        {
            var arrow2 = new TextBlock
            {
                Text = "\u2192", FontSize = 14, VerticalAlignment = VerticalAlignment.Center,
                Foreground = categoryBrush, Margin = new Thickness(6, 0, 6, 0),
            };
            Grid.SetColumn(arrow2, 3);
            grid.Children.Add(arrow2);

            var outputPanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            foreach (var output in node.Outputs)
            {
                outputPanel.Children.Add(CreatePortBadge(output.Name, output.DataType, false));
            }
            Grid.SetColumn(outputPanel, 4);
            grid.Children.Add(outputPanel);
        }

        diagram.Child = grid;
        return diagram;
    }

    private UIElement CreatePortBadge(string name, Type dataType, bool isInput)
    {
        var color = GetPortTypeColor(dataType);
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        var typeName = FriendlyTypeName(dataType);

        var border = new Border
        {
            CornerRadius = new CornerRadius(3),
            BorderThickness = new Thickness(1),
            BorderBrush = brush,
            Background = new SolidColorBrush(Color.FromArgb(32, brush.Color.R, brush.Color.G, brush.Color.B)),
            Padding = new Thickness(6, 2, 6, 2),
            Margin = new Thickness(0, 1, 0, 1),
        };

        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        sp.Children.Add(new TextBlock
        {
            Text = name, FontSize = 9, FontWeight = FontWeights.SemiBold,
            Foreground = FindBrush("ForegroundBrush"),
        });
        sp.Children.Add(new TextBlock
        {
            Text = $" ({typeName})", FontSize = 8, FontFamily = new FontFamily("Consolas"),
            Foreground = brush,
        });

        border.Child = sp;
        return border;
    }

    private UIElement CreatePortTable(BaseNode node)
    {
        var grid = new Grid { Margin = new Thickness(8, 2, 0, 8) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(55) });  // Direction
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) }); // Name
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) }); // Type
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });     // Description

        int row = 0;

        // Header
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        AddTableCell(grid, row, 0, "방향", true);
        AddTableCell(grid, row, 1, "이름", true);
        AddTableCell(grid, row, 2, "타입", true);
        row++;

        foreach (var input in node.Inputs)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            AddTableCell(grid, row, 0, "▶ 입력", false, "#89B4FA");
            AddTableCell(grid, row, 1, input.Name, false);
            AddTableCell(grid, row, 2, FriendlyTypeName(input.DataType), false, GetPortTypeColor(input.DataType));
            row++;
        }

        foreach (var output in node.Outputs)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            AddTableCell(grid, row, 0, "◀ 출력", false, "#A6E3A1");
            AddTableCell(grid, row, 1, output.Name, false);
            AddTableCell(grid, row, 2, FriendlyTypeName(output.DataType), false, GetPortTypeColor(output.DataType));
            row++;
        }

        return grid;
    }

    private UIElement CreatePropertyTable(BaseNode node)
    {
        var grid = new Grid { Margin = new Thickness(8, 2, 0, 8) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) }); // Name
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });  // Type
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });  // Default
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });     // Description

        int row = 0;

        // Header
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        AddTableCell(grid, row, 0, "이름", true);
        AddTableCell(grid, row, 1, "타입", true);
        AddTableCell(grid, row, 2, "기본값", true);
        AddTableCell(grid, row, 3, "설명", true);
        row++;

        foreach (var prop in node.Properties)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            AddTableCell(grid, row, 0, prop.DisplayName, false);
            AddTableCell(grid, row, 1, prop.PropertyType.ToString(), false);
            AddTableCell(grid, row, 2, prop.DefaultValue?.ToString() ?? "-", false);

            var desc = prop.Description ?? "";
            if (prop.MinValue != null && prop.MaxValue != null &&
                !prop.MinValue.ToString()!.Contains("E+") && !prop.MaxValue.ToString()!.Contains("E+"))
            {
                desc += $" [{prop.MinValue}~{prop.MaxValue}]";
            }
            AddTableCell(grid, row, 3, desc, false);
            row++;
        }

        return grid;
    }

    private void AddTableCell(Grid grid, int row, int col, string text, bool isHeader, string? colorHex = null)
    {
        var tb = new TextBlock
        {
            Text = text,
            FontSize = isHeader ? 10 : 10.5,
            FontWeight = isHeader ? FontWeights.SemiBold : FontWeights.Normal,
            Foreground = colorHex != null
                ? new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorHex))
                : FindBrush(isHeader ? "ForegroundDimBrush" : "ForegroundBrush"),
            Margin = new Thickness(0, 2, 12, 2),
            TextWrapping = TextWrapping.Wrap,
        };
        if (!isHeader && col == 2 && !text.Contains("["))
            tb.FontFamily = new FontFamily("Consolas");
        Grid.SetRow(tb, row);
        Grid.SetColumn(tb, col);
        grid.Children.Add(tb);
    }

    private TextBlock CreateSectionHeader(string text)
    {
        return new TextBlock
        {
            Text = text, FontSize = 11.5, FontWeight = FontWeights.SemiBold,
            Foreground = FindBrush("AccentBrush"),
            Margin = new Thickness(0, 6, 0, 2),
        };
    }

    private Brush FindBrush(string name)
    {
        return TryFindResource(name) as Brush ?? Brushes.White;
    }
}
