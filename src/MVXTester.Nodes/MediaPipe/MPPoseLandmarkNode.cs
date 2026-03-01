using OpenCvSharp;
using MVXTester.Core.Models;
using MVXTester.Core.Registry;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace MVXTester.Nodes.MediaPipe;

/// <summary>
/// Detects 33 body pose landmarks using MediaPipe BlazePose.
/// Two-stage pipeline: pose detection → pose landmark extraction.
/// Uses flat array access for safe tensor handling.
/// </summary>
[NodeInfo("MP Pose Landmark", NodeCategories.MediaPipe,
    Description = "Detect 33 body pose landmarks using MediaPipe BlazePose")]
public class MPPoseLandmarkNode : BaseNode
{
    private InputPort<Mat> _imageInput = null!;
    private OutputPort<Mat> _resultOutput = null!;
    private OutputPort<Point[]> _landmarksOutput = null!;
    private OutputPort<double[]> _visibilityOutput = null!;
    private OutputPort<int> _countOutput = null!;

    private NodeProperty _confidence = null!;
    private NodeProperty _drawSkeleton = null!;
    private NodeProperty _drawLabels = null!;

    private const string PoseDetModelFile = "pose_detection.onnx";
    private const string PoseLmModelFile = "pose_landmark_full.onnx";
    private const int DetInputSize = 224;
    private const int LmInputSize = 256;
    private const int NumPoseLandmarks = 33;

    private static readonly string[] LandmarkNames =
    {
        "nose", "left_eye_inner", "left_eye", "left_eye_outer",
        "right_eye_inner", "right_eye", "right_eye_outer",
        "left_ear", "right_ear", "mouth_left", "mouth_right",
        "left_shoulder", "right_shoulder", "left_elbow", "right_elbow",
        "left_wrist", "right_wrist", "left_pinky", "right_pinky",
        "left_index", "right_index", "left_thumb", "right_thumb",
        "left_hip", "right_hip", "left_knee", "right_knee",
        "left_ankle", "right_ankle", "left_heel", "right_heel",
        "left_foot_index", "right_foot_index"
    };

    protected override void Setup()
    {
        _imageInput = AddInput<Mat>("Image");

        _resultOutput = AddOutput<Mat>("Result");
        _landmarksOutput = AddOutput<Point[]>("Landmarks");
        _visibilityOutput = AddOutput<double[]>("Visibility");
        _countOutput = AddOutput<int>("Count");

        _confidence = AddDoubleProperty("Confidence", "Confidence", 0.5, 0.0, 1.0, "Minimum detection confidence");
        _drawSkeleton = AddBoolProperty("DrawSkeleton", "Draw Skeleton", true, "Draw pose skeleton connections");
        _drawLabels = AddBoolProperty("DrawLabels", "Draw Labels", false, "Draw landmark name labels");
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
            var drawSkel = _drawSkeleton.GetValue<bool>();
            var drawLabels = _drawLabels.GetValue<bool>();

            var result = image.Clone();
            if (result.Channels() == 1)
                Cv2.CvtColor(result, result, ColorConversionCodes.GRAY2BGR);

            // Stage 1: Pose Detection - find body ROI
            Rect bodyRoi;
            float detScore;
            if (!DetectBodyRoi(image, threshold, out bodyRoi, out detScore))
            {
                bodyRoi = new Rect(0, 0, image.Width, image.Height);
                detScore = 0;
            }

            // Make square ROI with padding
            var squareRoi = MakeSquareRoi(bodyRoi, 0.25f, image.Width, image.Height);

            // Stage 2: Pose Landmark
            var lmSession = MediaPipeHelper.GetSession(PoseLmModelFile);

            using var roiMat = new Mat(image, squareRoi);

            var lmInputData = MediaPipeHelper.PreprocessImageNHWC(roiMat, LmInputSize, LmInputSize);
            var lmInputName = lmSession.InputNames[0];
            var lmInputs = new List<NamedOnnxValue>
            {
                MediaPipeHelper.CreateTensor(lmInputName, lmInputData, new[] { 1, LmInputSize, LmInputSize, 3 })
            };

            using var lmResults = lmSession.Run(lmInputs);
            var lmOutputs = lmResults.ToList();

            // Flat array for safe access
            var lmFlat = MediaPipeHelper.GetFlatArray(lmOutputs[0].AsTensor<float>());

            // Check confidence
            float poseConfidence = 1.0f;
            if (lmOutputs.Count > 1)
            {
                var confFlat = MediaPipeHelper.GetFlatArray(lmOutputs[1].AsTensor<float>());
                poseConfidence = MediaPipeHelper.Sigmoid(confFlat.Length > 0 ? confFlat[0] : 0);
            }

            int totalValues = lmFlat.Length;
            int valuesPerLandmark = totalValues / NumPoseLandmarks;
            if (valuesPerLandmark < 2) valuesPerLandmark = 5;

            var landmarks = new Point[NumPoseLandmarks];
            var visibility = new double[NumPoseLandmarks];

            for (int i = 0; i < NumPoseLandmarks; i++)
            {
                int baseIdx = i * valuesPerLandmark;
                if (baseIdx + 1 >= totalValues) break;

                float lx = lmFlat[baseIdx];
                float ly = lmFlat[baseIdx + 1];

                // MediaPipe outputs pixel coords in [0, inputSize] space
                landmarks[i] = new Point(
                    (int)(lx / LmInputSize * squareRoi.Width + squareRoi.X),
                    (int)(ly / LmInputSize * squareRoi.Height + squareRoi.Y));

                // Visibility (index 3 if present)
                if (valuesPerLandmark >= 4 && baseIdx + 3 < totalValues)
                    visibility[i] = MediaPipeHelper.Sigmoid(lmFlat[baseIdx + 3]);
                else
                    visibility[i] = 1.0;
            }

            // Draw skeleton
            if (drawSkel)
            {
                foreach (var conn in MediaPipeHelper.PoseConnections)
                {
                    if (conn.Length < 2) continue;
                    int i0 = conn[0], i1 = conn[1];
                    if (i0 >= NumPoseLandmarks || i1 >= NumPoseLandmarks) continue;
                    if (visibility[i0] < 0.5 || visibility[i1] < 0.5) continue;

                    Cv2.Line(result, landmarks[i0], landmarks[i1],
                        new Scalar(0, 255, 128), 2, LineTypes.AntiAlias);
                }

                for (int i = 0; i < NumPoseLandmarks; i++)
                {
                    if (visibility[i] < 0.5) continue;
                    var color = i < 11
                        ? new Scalar(255, 128, 0)
                        : new Scalar(0, 255, 0);

                    Cv2.Circle(result, landmarks[i], 4, color, -1, LineTypes.AntiAlias);
                    Cv2.Circle(result, landmarks[i], 4, new Scalar(255, 255, 255), 1, LineTypes.AntiAlias);
                }
            }

            // Draw labels
            if (drawLabels)
            {
                for (int i = 0; i < NumPoseLandmarks && i < LandmarkNames.Length; i++)
                {
                    if (visibility[i] < 0.5) continue;
                    Cv2.PutText(result, LandmarkNames[i],
                        new Point(landmarks[i].X + 5, landmarks[i].Y - 5),
                        HersheyFonts.HersheySimplex, 0.3, new Scalar(255, 255, 255), 1);
                }
            }

            // Status
            var statusText = poseConfidence > threshold
                ? $"Pose: {poseConfidence:P0}"
                : "No pose detected";
            var statusColor = poseConfidence > threshold
                ? new Scalar(0, 255, 0) : new Scalar(0, 0, 255);
            Cv2.PutText(result, statusText, new Point(10, 25),
                HersheyFonts.HersheySimplex, 0.7, statusColor, 2);

            SetOutputValue(_resultOutput, result);
            SetOutputValue(_landmarksOutput, landmarks);
            SetOutputValue(_visibilityOutput, visibility);
            SetOutputValue(_countOutput, poseConfidence > threshold ? NumPoseLandmarks : 0);
            SetPreview(result);
            Error = null;
        }
        catch (FileNotFoundException ex)
        {
            Error = ex.Message;
        }
        catch (Exception ex)
        {
            Error = $"MP Pose Landmark error: {ex.Message}";
        }
    }

    /// <summary>
    /// Stage 1: Detect body bounding box using flat array access.
    /// </summary>
    private bool DetectBodyRoi(Mat image, float threshold, out Rect roi, out float score)
    {
        roi = new Rect(0, 0, image.Width, image.Height);
        score = 0;

        try
        {
            var detSession = MediaPipeHelper.GetSession(PoseDetModelFile);
            var detInputData = MediaPipeHelper.PreprocessImageNHWC(image, DetInputSize, DetInputSize);
            var detInputName = detSession.InputNames[0];
            var detInputs = new List<NamedOnnxValue>
            {
                MediaPipeHelper.CreateTensor(detInputName, detInputData, new[] { 1, DetInputSize, DetInputSize, 3 })
            };

            using var detResults = detSession.Run(detInputs);
            var detOutputs = detResults.ToList();

            if (detOutputs.Count < 2) return false;

            // Use flat arrays for safe access
            var regFlat = MediaPipeHelper.GetFlatArray(detOutputs[0].AsTensor<float>());
            var scoreFlat = MediaPipeHelper.GetFlatArray(detOutputs[1].AsTensor<float>());

            int numAnchors = scoreFlat.Length;
            int regStride = numAnchors > 0 ? regFlat.Length / numAnchors : 12;

            float bestScore = 0;
            int bestIdx = -1;

            for (int i = 0; i < numAnchors; i++)
            {
                float s = MediaPipeHelper.Sigmoid(scoreFlat[i]);
                if (s > bestScore)
                {
                    bestScore = s;
                    bestIdx = i;
                }
            }

            if (bestIdx < 0 || bestScore < threshold) return false;

            int regBase = bestIdx * regStride;
            if (regBase + 3 >= regFlat.Length) return false;

            float cx = regFlat[regBase + 0] / DetInputSize;
            float cy = regFlat[regBase + 1] / DetInputSize;
            float w = regFlat[regBase + 2] / DetInputSize;
            float h = regFlat[regBase + 3] / DetInputSize;

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
