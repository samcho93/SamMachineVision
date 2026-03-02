# SamMachineVision (MVXTester)

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
- **도움말 시스템** — 노드 레퍼런스 (166개 노드 상세 정보) + 내장 PDF 학습 교재

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
| **PDF 뷰어** | Microsoft.Web.WebView2 (Chromium 기반) |
| **시리얼 통신** | System.IO.Ports |
| **직렬화** | System.Text.Json |

## 프로젝트 구조

```
MVXTester/
├── MVXTester.sln
├── Models/                        ONNX 모델 및 설정 파일
│   ├── MediaPipe/                 MediaPipe ONNX 모델 (7개)
│   ├── YOLO/                      YOLOv8 ONNX 모델
│   ├── OCR/                       PaddleOCR 모델 + 사전 파일
│   ├── Tesseract/                 Tesseract traineddata 파일
│   └── API/                       API Key 설정 (api_config.json)
│
├── libs/                          외부 SDK DLL
│   └── MvCameraControl.Net.dll    HIK 카메라 SDK
│
├── docs/                          문서
│   ├── SamMachineVision (MVXTester) 학습 교재.pdf  (학습 교재 PDF)
│   ├── MVXTester_Textbook.html    HTML 교재 원본
│   └── parts/                     교재 파트 파일
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
    │   └── LLM/                   OpenAI/Gemini/Claude Vision (3 노드)
    │
    └── MVXTester.App/             WPF 애플리케이션
        ├── ViewModels/            MVVM ViewModel
        ├── Views/                 XAML View
        │   ├── HelpWindow.xaml    도움말 창 (7개 탭)
        │   ├── NodeReferenceView  노드 레퍼런스 (동적 생성)
        │   └── ...
        ├── Themes/                다크/라이트 테마 (Catppuccin)
        └── Services/              클립보드, 테마 서비스
```

## 도움말 시스템

Help 창 (`F1`)에는 7개 탭이 포함됩니다:

| 탭 | 내용 |
|----|------|
| **Usage** | 기본 사용법, 실행 모드, 프로젝트 저장/로드 |
| **Node Reference** | 166개 노드의 포트, 속성, 기능 설명, 응용 분야 (동적 생성) |
| **Tutorial** | 단계별 튜토리얼 예제 |
| **Program Structure** | 아키텍처 및 프로젝트 구조 |
| **Tech Stack** | 기술 스택 상세 |
| **Setup Guide** | SDK, 모델, API 설정 가이드 |
| **학습 교재** | PDF 교재 내장 뷰어 (WebView2) |

- **Node Reference**: `NodeReferenceView.xaml.cs`에서 `NodeRegistry`를 통해 모든 노드를 리플렉션으로 탐색하여 포트/속성/설명을 동적으로 생성합니다.
- **학습 교재**: `Microsoft.Web.WebView2`를 사용하여 PDF를 탭 내에서 직접 렌더링합니다. PDF 파일은 빌드 시 `docs/MVXTester_Textbook.pdf`로 출력 디렉토리에 복사됩니다.

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
| **LLM/VLM** | 3 | OpenAI GPT-4o Vision, Google Gemini Vision, Claude Vision |
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

## 개발 환경 설정

### 필수 요구 사항

- .NET 8.0 SDK
- Windows 10/11 (WPF)
- Visual Studio 2022 또는 `dotnet` CLI

### NuGet 패키지

#### MVXTester.App
| 패키지 | 버전 | 용도 |
|--------|------|------|
| Nodify | 7.* | 노드 그래프 에디터 UI |
| OpenCvSharp4 | 4.11.* | OpenCV 매니지드 래퍼 |
| OpenCvSharp4.WpfExtensions | 4.11.* | WPF 이미지 변환 |
| OpenCvSharp4.runtime.win | 4.11.* | OpenCV 네이티브 바이너리 (x86/x64) |
| CommunityToolkit.Mvvm | 8.* | MVVM 프레임워크 |
| Microsoft.Web.WebView2 | 1.* | PDF 내장 뷰어 (Chromium 기반) |

#### MVXTester.Core
| 패키지 | 버전 | 용도 |
|--------|------|------|
| OpenCvSharp4 | 4.11.* | 코어 이미지 타입 (Mat, Point, Rect 등) |

#### MVXTester.Nodes
| 패키지 | 버전 | 용도 |
|--------|------|------|
| OpenCvSharp4 | 4.11.* | 영상처리 알고리즘 |
| System.IO.Ports | 8.* | 시리얼 통신 |
| Microsoft.ML.OnnxRuntime | 1.20.* | ONNX 모델 추론 (MediaPipe, YOLO, OCR) |
| Tesseract | 5.2.* | Tesseract OCR 엔진 |

### ONNX 모델 파일

| 폴더 | 파일 | 크기 | 설명 |
|------|------|------|------|
| `Models/MediaPipe/` | `face_detection_short_range.onnx` | 412 KB | 얼굴 검출 |
| | `face_landmark.onnx` | 2.4 MB | 페이스메시 |
| | `palm_detection.onnx` | 3.8 MB | 손바닥 검출 |
| | `hand_landmark.onnx` | 4.0 MB | 손 랜드마크 |
| | `pose_landmark_full.onnx` | 5.3 MB | 포즈 랜드마크 |
| | `selfie_segmentation.onnx` | 452 KB | 셀피 세그먼테이션 |
| | `ssd_mobilenet_v2.onnx` | 65 MB | 객체 검출 (COCO 80) |
| `Models/YOLO/` | `yolov8n.onnx` | 13 MB | YOLOv8 nano |
| `Models/OCR/` | `ppocr_det.onnx` | 2.4 MB | 텍스트 영역 검출 |
| | `ppocr_rec.onnx` | 13 MB | 텍스트 인식 (한중일) |
| | `ppocr_rec_en.onnx` | 7.5 MB | 텍스트 인식 (영어) |
| | `ppocr_keys.txt` | 47 KB | 문자 사전 (한중일) |
| | `ppocr_keys_en.txt` | 1.4 KB | 문자 사전 (영어) |
| `Models/Tesseract/` | `eng.traineddata` | 23 MB | 영어 OCR |
| | `kor.traineddata` | 15 MB | 한국어 OCR |

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

- **HIK**: MVS SDK 설치 시 `MvCameraControl.Net.dll` 자동 검색 (`libs/` 폴더에 포함)
- **Cognex**: VisionPro 9.x 설치 시 자동 검색 (동적 로딩)
- SDK 미설치 환경에서도 카메라 외 기능 정상 작동

## 빌드 및 실행

```bash
# 복원
dotnet restore MVXTester.sln

# 빌드
dotnet build MVXTester.sln

# 실행
dotnet run --project src/MVXTester.App/MVXTester.App.csproj
```

### 빌드 출력에 자동 복사되는 파일

| 원본 | 출력 위치 | 설정 위치 |
|------|-----------|-----------|
| `Models/MediaPipe/*.onnx` | `Models/MediaPipe/` | MVXTester.App.csproj |
| `Models/API/api_config.json` | `Models/API/` | MVXTester.App.csproj |
| `libs/MvCameraControl.Net.dll` | 루트 | MVXTester.Nodes.csproj |
| `docs/...학습 교재.pdf` | `docs/MVXTester_Textbook.pdf` | MVXTester.App.csproj |

> YOLO, OCR, Tesseract 모델은 수동으로 출력 디렉토리에 복사하거나 csproj에 추가 필요

## 배포 (Beta)

### Release 빌드

```bash
# Framework-dependent (사용자가 .NET 8 런타임 별도 설치)
dotnet publish src/MVXTester.App/MVXTester.App.csproj ^
  -c Release -r win-x64 --self-contained false -o ./publish

# Self-contained (런타임 포함, 설치 파일 +150MB)
dotnet publish src/MVXTester.App/MVXTester.App.csproj ^
  -c Release -r win-x64 --self-contained true -o ./publish
```

### 배포 패키지 구성

```
publish/                               (~304 MB, x64 기준)
├── MVXTester.App.exe                  실행 파일
├── MVXTester.App.dll                  WPF UI
├── MVXTester.Core.dll                 코어 엔진
├── MVXTester.Nodes.dll                노드 라이브러리
├── MvCameraControl.Net.dll            HIK 카메라 SDK
├── OpenCvSharp.dll                    OpenCV 매니지드 래퍼
├── Microsoft.ML.OnnxRuntime.dll       ONNX Runtime 래퍼
├── Microsoft.Web.WebView2.*.dll       WebView2 (PDF 뷰어)
├── Tesseract.dll                      Tesseract OCR
├── *.deps.json / *.runtimeconfig.json 런타임 설정
│
├── runtimes/win-x64/native/           네이티브 바이너리 (~98 MB)
│   ├── OpenCvSharpExtern.dll          OpenCV 네이티브 (60 MB)
│   ├── opencv_videoio_ffmpeg*.dll     FFmpeg 코덱 (27 MB)
│   ├── onnxruntime.dll                ONNX Runtime (12 MB)
│   └── WebView2Loader.dll             WebView2 로더
│
├── x64/                               Tesseract 네이티브 (~6.7 MB)
│   ├── leptonica-1.82.0.dll
│   └── tesseract50.dll
│
├── Models/                            AI 모델 (~153 MB)
│   ├── MediaPipe/*.onnx
│   ├── YOLO/yolov8n.onnx
│   ├── OCR/ppocr_*.onnx + keys
│   ├── Tesseract/*.traineddata
│   └── API/api_config.json
│
└── docs/                              문서 (~41 MB)
    └── MVXTester_Textbook.pdf
```

### 사전 요구 사항 (사용자 PC)

| 요구 사항 | 필요 조건 | 비고 |
|-----------|-----------|------|
| OS | Windows 10 (1809+) / 11 | WPF 필수 |
| .NET Runtime | .NET 8.0 Desktop Runtime | self-contained 빌드 시 불필요 |
| WebView2 Runtime | Edge WebView2 Runtime | Win10/11 기본 포함, 구버전 시 설치 |
| VC++ Runtime | Visual C++ Redistributable 2022 | OpenCvSharp 네이티브 필요 |
| 카메라 SDK | HIK MVS SDK / Cognex VisionPro | 카메라 사용 시에만 필요 |

### 설치파일 생성 (Inno Setup 권장)

베타 배포 시 [Inno Setup](https://jrsoftware.org/isinfo.php)을 사용하여 EXE 설치 파일을 생성할 수 있습니다:

1. `dotnet publish`로 Release 빌드
2. `publish/` 폴더에 Models, docs 폴더 복사
3. Inno Setup 스크립트(`.iss`)에서 전체 폴더를 패키징
4. .NET 8 런타임, VC++ Runtime 재배포 가능 패키지를 Prerequisites로 포함
5. 설치 마법사 생성 → 단일 `Setup.exe` 생성

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

## 라이선스

This project is for internal use.
