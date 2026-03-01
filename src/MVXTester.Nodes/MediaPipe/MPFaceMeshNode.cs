using OpenCvSharp;
using MVXTester.Core.Models;
using MVXTester.Core.Registry;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace MVXTester.Nodes.MediaPipe;

/// <summary>
/// Detects 468 face landmarks using MediaPipe Face Mesh model.
/// Two-stage pipeline: face detection → face landmark extraction.
/// Outputs landmark points and annotated result image with face contours.
/// </summary>
[NodeInfo("MP Face Mesh", NodeCategories.MediaPipe,
    Description = "Detect 468 face landmarks using MediaPipe Face Mesh")]
public class MPFaceMeshNode : BaseNode
{
    private InputPort<Mat> _imageInput = null!;
    private OutputPort<Mat> _resultOutput = null!;
    private OutputPort<Point[]> _landmarksOutput = null!;
    private OutputPort<int> _countOutput = null!;

    private NodeProperty _confidence = null!;
    private NodeProperty _drawContours = null!;
    private NodeProperty _drawPoints = null!;

    private const string FaceDetModelFile = "face_detection_short_range.onnx";
    private const string FaceLmModelFile = "face_landmark.onnx";
    private const int DetInputSize = 128;
    private const int LmInputSize = 192;
    private const int NumLandmarks = 468;

    protected override void Setup()
    {
        _imageInput = AddInput<Mat>("Image");

        _resultOutput = AddOutput<Mat>("Result");
        _landmarksOutput = AddOutput<Point[]>("Landmarks");
        _countOutput = AddOutput<int>("Count");

        _confidence = AddDoubleProperty("Confidence", "Confidence", 0.5, 0.0, 1.0, "Minimum face confidence");
        _drawContours = AddBoolProperty("DrawContours", "Draw Contours", true, "Draw face mesh contour lines");
        _drawPoints = AddBoolProperty("DrawPoints", "Draw Points", false, "Draw individual landmark points");
    }

    public override void Process()
    {
        try
        {
            var image = GetInputValue(_imageInput);
            if (image == null || image.Empty())
            {
                Error = "No input image";
                return;
            }

            var threshold = (float)_confidence.GetValue<double>();
            var showContours = _drawContours.GetValue<bool>();
            var showPoints = _drawPoints.GetValue<bool>();

            var result = image.Clone();
            if (result.Channels() == 1)
                Cv2.CvtColor(result, result, ColorConversionCodes.GRAY2BGR);

            // Stage 1: Face Detection - find face ROI
            Rect faceRoi;
            float detScore;
            if (!DetectFaceRoi(image, threshold, out faceRoi, out detScore))
            {
                // Fallback: use entire image as ROI
                faceRoi = new Rect(0, 0, image.Width, image.Height);
                detScore = 0;
            }

            // Stage 2: Face Landmark on cropped face
            var lmSession = MediaPipeHelper.GetSession(FaceLmModelFile);

            // Square ROI with padding for better landmark detection
            var paddedRoi = MakeSquareRoi(faceRoi, 0.3f, image.Width, image.Height);
            using var roiMat = new Mat(image, paddedRoi);

            var lmInputData = MediaPipeHelper.PreprocessImageNHWC(roiMat, LmInputSize, LmInputSize);
            var lmInputName = lmSession.InputNames[0];
            var lmInputs = new List<NamedOnnxValue>
            {
                MediaPipeHelper.CreateTensor(lmInputName, lmInputData, new[] { 1, LmInputSize, LmInputSize, 3 })
            };

            using var lmResults = lmSession.Run(lmInputs);
            var lmOutputs = lmResults.ToList();

            // Parse landmarks using flat array
            var lmFlat = MediaPipeHelper.GetFlatArray(lmOutputs[0].AsTensor<float>());

            // Check face confidence from landmark model
            float faceConfidence = 1.0f;
            if (lmOutputs.Count > 1)
            {
                var confFlat = MediaPipeHelper.GetFlatArray(lmOutputs[1].AsTensor<float>());
                faceConfidence = MediaPipeHelper.Sigmoid(confFlat.Length > 0 ? confFlat[0] : 0);
            }

            if (faceConfidence < threshold && detScore < threshold)
            {
                Cv2.PutText(result, "No face detected", new Point(10, 25),
                    HersheyFonts.HersheySimplex, 0.6, new Scalar(0, 0, 255), 2);
                SetOutputValue(_resultOutput, result);
                SetOutputValue(_landmarksOutput, Array.Empty<Point>());
                SetOutputValue(_countOutput, 0);
                SetPreview(result);
                Error = null;
                return;
            }

            // Extract 468 landmarks
            int totalValues = lmFlat.Length;
            int stride = Math.Max(1, totalValues / NumLandmarks); // typically 3 (x, y, z)

            var landmarks = new Point[NumLandmarks];
            for (int i = 0; i < NumLandmarks && i * stride + 1 < totalValues; i++)
            {
                float x = lmFlat[i * stride];
                float y = lmFlat[i * stride + 1];

                // MediaPipe outputs pixel coords in [0, inputSize] space
                landmarks[i] = new Point(
                    (int)(x / LmInputSize * paddedRoi.Width + paddedRoi.X),
                    (int)(y / LmInputSize * paddedRoi.Height + paddedRoi.Y));
            }

            // Draw contours
            if (showContours)
            {
                foreach (var conn in MediaPipeHelper.FaceMeshContours)
                {
                    if (conn.Length < 2) continue;
                    int i0 = conn[0], i1 = conn[1];
                    if (i0 >= 0 && i0 < landmarks.Length && i1 >= 0 && i1 < landmarks.Length)
                    {
                        Cv2.Line(result, landmarks[i0], landmarks[i1],
                            new Scalar(0, 255, 200), 1, LineTypes.AntiAlias);
                    }
                }
            }

            // Draw individual points
            if (showPoints)
            {
                foreach (var pt in landmarks)
                {
                    Cv2.Circle(result, pt, 1, new Scalar(0, 255, 0), -1, LineTypes.AntiAlias);
                }
            }

            // Status label
            var statusText = $"Face: {Math.Max(faceConfidence, detScore):P0}";
            Cv2.PutText(result, statusText, new Point(10, 25),
                HersheyFonts.HersheySimplex, 0.7, new Scalar(0, 255, 0), 2);

            SetOutputValue(_resultOutput, result);
            SetOutputValue(_landmarksOutput, landmarks);
            SetOutputValue(_countOutput, NumLandmarks);
            SetPreview(result);
            Error = null;
        }
        catch (FileNotFoundException ex)
        {
            Error = ex.Message;
        }
        catch (Exception ex)
        {
            Error = $"MP Face Mesh error: {ex.Message}";
        }
    }

    /// <summary>
    /// Stage 1: Detect face bounding box using BlazeFace short-range model.
    /// </summary>
    private bool DetectFaceRoi(Mat image, float threshold, out Rect roi, out float score)
    {
        roi = new Rect(0, 0, image.Width, image.Height);
        score = 0;

        try
        {
            var detSession = MediaPipeHelper.GetSession(FaceDetModelFile);
            var detInputData = MediaPipeHelper.PreprocessImageNHWC(image, DetInputSize, DetInputSize);
            var detInputName = detSession.InputNames[0];
            var detInputs = new List<NamedOnnxValue>
            {
                MediaPipeHelper.CreateTensor(detInputName, detInputData, new[] { 1, DetInputSize, DetInputSize, 3 })
            };

            using var detResults = detSession.Run(detInputs);
            var detOutputs = detResults.ToList();

            if (detOutputs.Count < 2) return false;

            var regressors = MediaPipeHelper.GetFlatArray(detOutputs[0].AsTensor<float>());
            var scores = MediaPipeHelper.GetFlatArray(detOutputs[1].AsTensor<float>());

            var anchors = MediaPipeHelper.GetBlazeFaceAnchors();
            int numAnchors = anchors.GetLength(0);

            // Find regressor stride (values per anchor)
            int regTotal = regressors.Length;
            int regStride = numAnchors > 0 ? regTotal / numAnchors : 16;

            float bestScore = 0;
            int bestIdx = -1;

            for (int i = 0; i < numAnchors && i < scores.Length; i++)
            {
                float s = MediaPipeHelper.Sigmoid(scores[i]);
                if (s > bestScore)
                {
                    bestScore = s;
                    bestIdx = i;
                }
            }

            if (bestIdx < 0 || bestScore < threshold) return false;

            // Decode bounding box from anchor
            float anchorCx = anchors[bestIdx, 0];
            float anchorCy = anchors[bestIdx, 1];

            int regBase = bestIdx * regStride;
            if (regBase + 3 >= regTotal) return false;

            float cx = anchorCx + regressors[regBase + 0] / DetInputSize;
            float cy = anchorCy + regressors[regBase + 1] / DetInputSize;
            float w = regressors[regBase + 2] / DetInputSize;
            float h = regressors[regBase + 3] / DetInputSize;

            roi = new Rect(
                (int)((cx - w / 2) * image.Width),
                (int)((cy - h / 2) * image.Height),
                (int)(w * image.Width),
                (int)(h * image.Height));

            roi = ClampRect(roi, image.Width, image.Height);
            score = bestScore;
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Create square ROI centered on detection box with padding.
    /// Square crop prevents distortion when resizing to the model's square input.
    /// </summary>
    private static Rect MakeSquareRoi(Rect box, float padRatio, int imgW, int imgH)
    {
        int cx = box.X + box.Width / 2;
        int cy = box.Y + box.Height / 2;
        int maxSide = Math.Max(box.Width, box.Height);

        int padded = (int)(maxSide * (1 + padRatio));
        int half = padded / 2;

        int x = Math.Max(0, cx - half);
        int y = Math.Max(0, cy - half);
        int w = Math.Min(padded, imgW - x);
        int h = Math.Min(padded, imgH - y);

        int side = Math.Min(w, h);
        return new Rect(x, y, Math.Max(1, side), Math.Max(1, side));
    }

    private static Rect ClampRect(Rect r, int imgW, int imgH)
    {
        int x = Math.Max(0, r.X);
        int y = Math.Max(0, r.Y);
        int w = Math.Min(r.Width, imgW - x);
        int h = Math.Min(r.Height, imgH - y);
        return new Rect(x, y, Math.Max(1, w), Math.Max(1, h));
    }
}
