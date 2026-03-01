# SamMachineVision

노드 기반 머신비전 테스트 애플리케이션. 시각적 그래프 에디터에서 노드를 연결하여 이미지 처리 파이프라인을 구성하고 실행할 수 있습니다.

## 주요 기능

- **노드 기반 그래프 에디터** — 드래그 앤 드롭으로 노드 배치 및 연결
- **실시간 스트리밍** — 통합 카메라 노드 (USB, HIK GigE, Cognex) 실시간 영상 처리
- **160+ 노드** — 27개 카테고리에 걸친 다양한 영상처리/검사/측정/AI 노드
- **AI/ML 통합** — MediaPipe, YOLOv8, PaddleOCR, Tesseract OCR, GPT-4o/Gemini/Claude Vision
- **통합 카메라** — USB/HIK/Cognex 카메라를 하나의 노드로 통합, 자동 감지 및 동적 프로퍼티
- **백그라운드 통신** — TCP/Serial 백그라운드 수신 (IBackgroundNode)
- **코드 생성** — 구성한 그래프를 Python 또는 C# 코드로 자동 변환
- **Undo/Redo** — 최대 100단계 작업 이력 관리
- **다크/라이트 테마** — Catppuccin Mocha/Latte 기반 테마 전환

## 스크린샷

| 구성요소 | 설명 |
|---------|------|
| 좌측 패널 | 노드 팔레트 (카테고리별 노드 검색/선택) |
| 중앙 캔버스 | 그래프 에디터 (노드 배치 및 연결) |
| 우측 패널 | 프로퍼티 에디터 (선택 노드 속성 편집) |
| 하단 패널 | 실행 출력 (처리 결과 이미지 확인) |

## 기술 스택

| 구분 | 기술 |
|------|------|
| **플랫폼** | .NET 8.0 / C# |
| **UI 프레임워크** | WPF (Windows Presentation Foundation) |
| **아키텍처** | MVVM (CommunityToolkit.Mvvm) |
| **노드 그래프** | Nodify 7.x |
| **영상처리** | OpenCvSharp4 (OpenCV 4.11) |
| **AI 추론** | Microsoft.ML.OnnxRuntime (MediaPipe, YOLO, PaddleOCR) |
| **OCR** | PaddleOCR (ONNX), Tesseract 5.2.x |
| **AI Vision** | OpenAI GPT-4o, Google Gemini, Anthropic Claude (REST API) |
| **카메라 SDK** | HIK MvCameraControl.Net, Cognex VisionPro (동적 로딩) |
| **시리얼 통신** | System.IO.Ports |
| **직렬화** | System.Text.Json |

## 프로젝트 구조

```
MVXTester/
├── MVXTester.sln
├── Models/                        ONNX 모델 및 설정 파일
│   ├── MediaPipe/                 MediaPipe ONNX 모델 (8개)
│   ├── YOLO/                      YOLOv8 ONNX 모델
│   ├── OCR/                       PaddleOCR 모델 + 사전 파일
│   ├── Tesseract/                 Tesseract traineddata 파일
│   └── API/                       API Key 설정 (api_config.json)
│
└── src/
    ├── MVXTester.Core/            코어 프레임워크
    │   ├── Models/                BaseNode, Port, NodeGraph, Connection
    │   ├── Engine/                GraphExecutor, 코드 생성기
    │   ├── Registry/              NodeRegistry, NodeCategory
    │   ├── Serialization/         JSON 직렬화/역직렬화
    │   └── UndoRedo/              Undo/Redo 매니저
    │
    ├── MVXTester.Nodes/           노드 구현체 (160+)
    │   ├── Input/                 통합 카메라, 이미지/비디오 입력
    │   ├── Filter/                가우시안, 미디언, 양방향 필터 등
    │   ├── Edge/                  Canny, Sobel, Laplacian
    │   ├── Threshold/             이진화, 적응형 이진화, Otsu
    │   ├── Morphology/            침식, 팽창, 모폴로지 연산
    │   ├── Color/                 색공간 변환, 채널 분리/병합
    │   ├── Contour/               외곽선 검출, 필터링, 모멘트
    │   ├── Detection/             템플릿 매칭, 허프 변환, Haar
    │   ├── Feature/               ORB, AKAZE, SIFT, 블롭 검출
    │   ├── Drawing/               도형, 텍스트, 외곽선 그리기
    │   ├── Transform/             리사이즈, 회전, 어파인, 원근 변환
    │   ├── Arithmetic/            사칙연산, 비트연산, 블렌딩
    │   ├── Histogram/             히스토그램 계산, 평활화, 비교
    │   ├── Segmentation/          Watershed, GrabCut, KMeans
    │   ├── Inspection/            고수준 검사 (결함, 패턴, 정렬)
    │   ├── Measurement/           치수, 거리, 각도 측정
    │   ├── Value/                 기본 타입 값 노드
    │   ├── Control/               조건분기, 반복, 비교, Delay
    │   ├── Communication/         TCP, Serial 통신 (백그라운드 수신)
    │   ├── Data/                  CSV, 문자열 처리
    │   ├── Event/                 이벤트 처리, WaitKey
    │   ├── Script/                Python 스크립트 실행
    │   ├── MediaPipe/             얼굴/손/포즈/세그먼테이션 (6 노드)
    │   ├── YOLO/                  YOLOv8 객체 검출 (1 노드)
    │   ├── OCR/                   PaddleOCR, Tesseract OCR (2 노드)
    │   └── AI/                    OpenAI/Gemini/Claude Vision (3 노드)
    │
    └── MVXTester.App/             WPF 애플리케이션
        ├── ViewModels/            MVVM ViewModel
        ├── Views/                 XAML View
        ├── Themes/                다크/라이트 테마 (Catppuccin)
        └── Services/              클립보드, 테마 서비스
```

## 노드 카테고리 (27개)

| 카테고리 | 노드 수 | 설명 |
|---------|---------|------|
| **Input** | 7 | 통합 카메라 (USB/HIK/Cognex), 이미지/비디오 읽기 |
| **Color** | 4 | 색공간 변환, 채널 분리/병합, InRange |
| **Filter** | 10 | 가우시안, 미디언, 양방향 필터, 샤프닝, LUT |
| **Edge** | 4 | Canny, Sobel, Scharr, Laplacian |
| **Morphology** | 3 | 침식, 팽창, 모폴로지 연산 |
| **Threshold** | 3 | 전역/적응형/Otsu 이진화 |
| **Contour** | 13 | 외곽선 검출, 필터링, 모멘트, 타원/사각형 피팅 |
| **Feature** | 8 | ORB, AKAZE, SIFT, 블롭 검출, 특징점 매칭 |
| **Drawing** | 10 | 도형, 텍스트, 외곽선, 바운딩 박스 그리기 |
| **Transform** | 8 | 리사이즈, 회전, 어파인, 원근, 피라미드 |
| **Histogram** | 4 | 히스토그램 계산, 평활화, 비교, 역투영 |
| **Arithmetic** | 11 | 이미지 연산, 비트 연산, 마스크 적용, 블렌딩 |
| **Detection** | 9 | 템플릿 매칭, 허프 원/직선, 연결 성분 분석 |
| **Segmentation** | 3 | Watershed, GrabCut, KMeans |
| **Value** | 14 | Integer, Float, String, Point, Scalar 등 기본 타입 |
| **Control** | 12 | 조건분기(If), 반복(For/While), 비교, 스위치, Delay |
| **Communication** | 3 | TCP 클라이언트/서버, 시리얼 포트 (백그라운드 수신) |
| **Data** | 7 | CSV 읽기/파싱, 문자열 처리 |
| **Event** | 4 | 키보드/마우스 이벤트, WaitKey |
| **Script** | 1 | Python 스크립트 실행 |
| **Inspection** | 13 | 색상 객체 검출, 얼굴 인식, 결함 검사, 패턴 매칭 |
| **Measurement** | 3 | 치수 측정, 거리 측정, 각도 측정 |
| **MediaPipe** | 6 | 얼굴 검출, 손/포즈 랜드마크, 페이스메시, 셀피 세그먼테이션, 객체 검출 |
| **YOLO** | 1 | YOLOv8 객체 검출 (nano~xlarge, 자동 클래스 감지) |
| **OCR** | 2 | PaddleOCR (다국어), Tesseract OCR (100+ 언어) |
| **AI** | 3 | OpenAI GPT-4o Vision, Google Gemini Vision, Claude Vision |
| **Function** | - | 서브그래프 재사용 노드 (.mvxp 임포트) |

## 아키텍처

### 코어 프레임워크

```
BaseNode (추상 클래스)
├── Setup()          → 포트, 프로퍼티 정의
├── Process()        → 영상처리 로직 실행
├── Inputs/Outputs   → 타입 안전한 제네릭 포트
├── Properties       → 동적 속성 시스템 (NodeProperty, 동적 가시성)
├── Preview          → Mat? 미리보기 이미지
└── Error            → 에러 메시지 표시
```

### 그래프 실행 엔진

```
[단일 실행]
Execute() → TopologicalSort() (Kahn's Algorithm)
         → 각 노드 순차 실행 (Dirty 노드만)
         → 하류 노드 Dirty 전파

[런타임 실행 (F5)]
ExecuteRuntime() → IBackgroundNode.StartBackground()
               → IStreamingSource 노드 Dirty 마킹
               → 16ms 폴링 루프 (반응형)
               → IBackgroundNode.StopBackground()

[스트리밍 실행 (F6)]
ExecuteContinuous() → IBackgroundNode.StartBackground()
                   → 목표 FPS 루프
                   → IStreamingSource 노드 Dirty 마킹
                   → TopologicalSort() → 순차 실행
                   → IBackgroundNode.StopBackground()
```

### AI/ML 파이프라인

```
[ONNX Runtime 노드]
Mat → 전처리(Resize, Normalize) → ONNX InferenceSession → 후처리 → 결과

[VLM 노드]
Mat → Cv2.ImEncode(".png") → byte[] → base64 → REST API → 텍스트 응답
                                                          → 이미지 오버레이
```

### MVVM 구조

```
MainViewModel
├── EditorViewModel        → 그래프 편집, 실행 제어
├── NodePaletteViewModel   → 노드 검색/선택, 아코디언 UI
├── PropertyEditorViewModel → 선택 노드 속성 편집
└── ExecuteOutputViewModel  → 실행 결과 표시
```

## 설치 및 설정

### 필수 요구 사항

- .NET 8.0 SDK
- Windows 10/11 (WPF)
- Visual Studio 2022 또는 `dotnet` CLI

### ONNX 모델 다운로드

| 폴더 | 파일 | 설명 | 다운로드 |
|------|------|------|---------|
| `Models/MediaPipe/` | `blazeface_short.onnx` | 얼굴 검출 | google/mediapipe |
| | `palm_detection.onnx` | 손바닥 검출 | |
| | `hand_landmark.onnx` | 손 랜드마크 | |
| | `pose_detection.onnx` | 포즈 검출 | |
| | `pose_landmark_lite.onnx` | 포즈 랜드마크 | |
| | `face_landmark.onnx` | 페이스메시 | |
| | `selfie_segmentation.onnx` | 셀피 세그먼테이션 | |
| | `ssd_mobilenet_v2.onnx` | 객체 검출 (COCO 80) | |
| `Models/YOLO/` | `yolov8n.onnx` | YOLOv8 nano (6MB) | ultralytics/assets |
| `Models/OCR/` | `ppocr_det.onnx` | 텍스트 검출 | HuggingFace monkt/paddleocr-onnx |
| | `ppocr_rec.onnx` | 텍스트 인식 (한중일) | |
| | `ppocr_keys.txt` | 문자 사전 | |
| `Models/Tesseract/` | `eng.traineddata` | 영어 OCR (23MB) | github.com/tesseract-ocr/tessdata |
| | `kor.traineddata` | 한국어 OCR (15MB) | |

### API Key 설정

AI Vision 노드 사용 시 `Models/API/api_config.json` 파일에 API 키를 설정합니다:

```json
{
  "openai": {
    "api_key": "sk-proj-...",
    "model": "gpt-4o-mini"
  },
  "gemini": {
    "api_key": "AIza...",
    "model": "gemini-2.5-flash"
  },
  "claude": {
    "api_key": "",
    "model": "claude-sonnet-4-20250514"
  }
}
```

**API 키 발급:**
- **OpenAI**: https://platform.openai.com → API Keys → Create new secret key
- **Google Gemini**: https://aistudio.google.com → Get API key
- **Anthropic Claude**: https://console.anthropic.com → API Keys → Create Key

> 파일이 없어도 노드 Properties 패널에서 수동 입력 가능

### 카메라 SDK (선택)

- **HIK**: MVS SDK 설치 시 `MvCameraControl.Net.dll` 자동 검색
- **Cognex**: VisionPro 9.x 설치 시 자동 검색
- SDK 미설치 환경에서도 카메라 외 기능 정상 작동

## 키보드 단축키

| 단축키 | 기능 |
|--------|------|
| `Ctrl+N` | 새 그래프 |
| `Ctrl+O` | 그래프 열기 |
| `Ctrl+S` | 저장 |
| `Ctrl+Shift+S` | 다른 이름으로 저장 |
| `F1` | 도움말 |
| `F5` | 런타임 실행 (반응형) |
| `Ctrl+F5` | 강제 실행 |
| `F6` | 스트리밍 시작/중지 |
| `Escape` | 실행 취소 |
| `Ctrl+Z` | 되돌리기 |
| `Ctrl+Y` / `Ctrl+Shift+Z` | 다시 실행 |
| `Ctrl+C` / `Ctrl+X` / `Ctrl+V` | 복사 / 잘라내기 / 붙여넣기 |
| `Ctrl+D` | 복제 |
| `Ctrl+A` | 전체 선택 |
| `Delete` | 선택 노드 삭제 |

## 빌드 및 실행

```bash
# 빌드
dotnet build MVXTester.sln

# 실행
dotnet run --project src/MVXTester.App/MVXTester.App.csproj
```

## 라이선스

This project is for internal use.
