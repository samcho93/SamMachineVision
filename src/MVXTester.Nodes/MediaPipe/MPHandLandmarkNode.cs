using OpenCvSharp;
using MVXTester.Core.Models;
using MVXTester.Core.Registry;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace MVXTester.Nodes.MediaPipe;

/// <summary>
/// Detects hand landmarks (21 points per hand) using MediaPipe.
/// Two-stage pipeline: palm detection → hand landmark extraction.
/// Uses flat array access for safe tensor handling regardless of output shape.
/// </summary>
[NodeInfo("MP Hand Landmark", NodeCategories.MediaPipe,
    Description = "Detect 21 hand landmarks using MediaPipe")]
public class MPHandLandmarkNode : BaseNode
{
    private InputPort<Mat> _imageInput = null!;
    private OutputPort<Mat> _resultOutput = null!;
    private OutputPort<Point[]> _landmarksOutput = null!;
    private OutputPort<int> _countOutput = null!;

    private NodeProperty _confidence = null!;
    private NodeProperty _maxHands = null!;
    private NodeProperty _drawSkeleton = null!;

    private const string PalmModelFile = "palm_detection.onnx";
    private const string HandModelFile = "hand_landmark.onnx";
    private const int PalmInputSize = 192;
    private const int HandInputSize = 224;
    private const int NumHandLandmarks = 21;

    protected override void Setup()
    {
        _imageInput = AddInput<Mat>("Image");

        _resultOutput = AddOutput<Mat>("Result");
        _landmarksOutput = AddOutput<Point[]>("Landmarks");
        _countOutput = AddOutput<int>("Count");

        _confidence = AddDoubleProperty("Confidence", "Confidence", 0.5, 0.0, 1.0, "Minimum detection confidence");
        _maxHands = AddIntProperty("MaxHands", "Max Hands", 2, 1, 4, "Maximum number of hands to detect");
        _drawSkeleton = AddBoolProperty("DrawSkeleton", "Draw Skeleton", true, "Draw hand skeleton connections");
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
            var maxHands = _maxHands.GetValue<int>();
            var drawSkel = _drawSkeleton.GetValue<bool>();

            var result = image.Clone();
            if (result.Channels() == 1)
                Cv2.CvtColor(result, result, ColorConversionCodes.GRAY2BGR);

            // Stage 1: Palm Detection (flat array access for safety)
            var palmDetections = DetectPalms(image, threshold);

            var nmsHands = MediaPipeHelper.NonMaxSuppression(palmDetections, 0.3f);
            if (nmsHands.Count > maxHands)
                nmsHands = nmsHands.Take(maxHands).ToList();

            // Stage 2: Hand Landmark for each detected palm
            var allLandmarks = new List<Point>();
            var handSession = MediaPipeHelper.GetSession(HandModelFile);

            foreach (var (palmBox, palmScore, _) in nmsHands)
            {
                // Make square ROI (prevent distortion when resizing to 224x224)
                var squareRoi = MakeSquareRoi(palmBox, 0.6f, image.Width, image.Height);
                if (squareRoi.Width < 10 || squareRoi.Height < 10) continue;

                using var handRoi = new Mat(image, squareRoi);

                var handInputData = MediaPipeHelper.PreprocessImageNHWC(handRoi, HandInputSize, HandInputSize);
                var handInputName = handSession.InputNames[0];
                var handInputs = new List<NamedOnnxValue>
                {
                    MediaPipeHelper.CreateTensor(handInputName, handInputData, new[] { 1, HandInputSize, HandInputSize, 3 })
                };

                using var handResults = handSession.Run(handInputs);
                var handOutputs = handResults.ToList();

                // Parse landmarks - flat array for safe access
                var lmFlat = MediaPipeHelper.GetFlatArray(handOutputs[0].AsTensor<float>());

                // Check hand presence/confidence
                float handPresence = 1.0f;
                if (handOutputs.Count > 1)
                {
                    var presFlat = MediaPipeHelper.GetFlatArray(handOutputs[1].AsTensor<float>());
                    handPresence = MediaPipeHelper.Sigmoid(presFlat.Length > 0 ? presFlat[0] : 0);
                }

                if (handPresence < threshold * 0.5f) continue;

                // Extract landmarks and map to original image coordinates
                var handLandmarks = new Point[NumHandLandmarks];
                int totalValues = lmFlat.Length;
                int stride = Math.Max(1, totalValues / NumHandLandmarks); // typically 3 (x, y, z)

                for (int i = 0; i < NumHandLandmarks && i * stride + 1 < totalValues; i++)
                {
                    float lx = lmFlat[i * stride];
                    float ly = lmFlat[i * stride + 1];

                    // MediaPipe landmark models output pixel coords in [0, inputSize] space
                    handLandmarks[i] = new Point(
                        (int)(lx / HandInputSize * squareRoi.Width + squareRoi.X),
                        (int)(ly / HandInputSize * squareRoi.Height + squareRoi.Y));
                }

                allLandmarks.AddRange(handLandmarks);

                // Draw
                if (drawSkel)
                {
                    MediaPipeHelper.DrawLandmarks(result, handLandmarks,
                        MediaPipeHelper.HandConnections,
                        new Scalar(0, 255, 0), 2, 4);
                }

                Cv2.Rectangle(result, squareRoi, new Scalar(255, 200, 0), 1);
            }

            // Status text
            if (nmsHands.Count == 0)
            {
                Cv2.PutText(result, "No hands detected", new Point(10, 25),
                    HersheyFonts.HersheySimplex, 0.6, new Scalar(0, 0, 255), 2);
            }
            else
            {
                Cv2.PutText(result, $"Hands: {nmsHands.Count}", new Point(10, 25),
                    HersheyFonts.HersheySimplex, 0.7, new Scalar(0, 255, 0), 2);
            }

            SetOutputValue(_resultOutput, result);
            SetOutputValue(_landmarksOutput, allLandmarks.ToArray());
            SetOutputValue(_countOutput, nmsHands.Count);
            SetPreview(result);
            Error = null;
        }
        catch (FileNotFoundException ex)
        {
            Error = ex.Message;
        }
        catch (Exception ex)
        {
            Error = $"MP Hand Landmark error: {ex.Message}";
        }
    }

    /// <summary>
    /// Stage 1: Detect palms using flat array access for safe tensor handling.
    /// </summary>
    private List<(Rect Box, float Score)> DetectPalms(Mat image, float threshold)
    {
        var detections = new List<(Rect Box, float Score)>();

        try
        {
            var palmSession = MediaPipeHelper.GetSession(PalmModelFile);
            var palmInputData = MediaPipeHelper.PreprocessImageNHWC(image, PalmInputSize, PalmInputSize);
            var palmInputName = palmSession.InputNames[0];
            var palmInputs = new List<NamedOnnxValue>
            {
                MediaPipeHelper.CreateTensor(palmInputName, palmInputData, new[] { 1, PalmInputSize, PalmInputSize, 3 })
            };

            using var palmResults = palmSession.Run(palmInputs);
            var palmOutputs = palmResults.ToList();

            if (palmOutputs.Count < 2) return detections;

            // Use flat arrays for safe access regardless of tensor shape
            var regFlat = MediaPipeHelper.GetFlatArray(palmOutputs[0].AsTensor<float>());
            var scoreFlat = MediaPipeHelper.GetFlatArray(palmOutputs[1].AsTensor<float>());

            var palmAnchors = MediaPipeHelper.GetPalmDetectionAnchors();
            int numAnchors = palmAnchors.GetLength(0);

            // Regressor stride: values per anchor (typically 18 = 4 bbox + 7 keypoints * 2)
            int regStride = numAnchors > 0 ? regFlat.Length / numAnchors : 18;

            for (int i = 0; i < numAnchors && i < scoreFlat.Length; i++)
            {
                float score = MediaPipeHelper.Sigmoid(scoreFlat[i]);
                if (score < threshold) continue;

                float anchorCx = palmAnchors[i, 0];
                float anchorCy = palmAnchors[i, 1];

                int regBase = i * regStride;
                if (regBase + 3 >= regFlat.Length) continue;

                float cx = anchorCx + regFlat[regBase + 0] / PalmInputSize;
                float cy = anchorCy + regFlat[regBase + 1] / PalmInputSize;
                float w = regFlat[regBase + 2] / PalmInputSize;
                float h = regFlat[regBase + 3] / PalmInputSize;

                int x1 = (int)((cx - w / 2) * image.Width);
                int y1 = (int)((cy - h / 2) * image.Height);
                int bw = (int)(w * image.Width);
                int bh = (int)(h * image.Height);

                x1 = Math.Max(0, x1);
                y1 = Math.Max(0, y1);
                bw = Math.Min(bw, image.Width - x1);
                bh = Math.Min(bh, image.Height - y1);

                if (bw > 10 && bh > 10)
                    detections.Add((new Rect(x1, y1, bw, bh), score));
            }
        }
        catch
        {
            // Palm detection failed, return empty
        }

        return detections;
    }

    /// <summary>
    /// Create a square ROI centered on the detection box with padding.
    /// Square crop prevents distortion when resizing to the model's square input.
    /// </summary>
    private static Rect MakeSquareRoi(Rect box, float padRatio, int imgW, int imgH)
    {
        int cx = box.X + box.Width / 2;
        int cy = box.Y + box.Height / 2;
        int maxSide = Math.Max(box.Width, box.Height);

        // Apply padding
        int padded = (int)(maxSide * (1 + padRatio));
        int half = padded / 2;

        int x = Math.Max(0, cx - half);
        int y = Math.Max(0, cy - half);
        int w = Math.Min(padded, imgW - x);
        int h = Math.Min(padded, imgH - y);

        // Keep square by using the smaller dimension
        int side = Math.Min(w, h);
        return new Rect(x, y, Math.Max(1, side), Math.Max(1, side));
    }
}
