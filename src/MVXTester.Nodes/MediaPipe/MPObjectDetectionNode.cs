using OpenCvSharp;
using MVXTester.Core.Models;
using MVXTester.Core.Registry;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace MVXTester.Nodes.MediaPipe;

/// <summary>
/// Detects objects using SSD MobileNet V2 model trained on COCO dataset (80 classes).
/// Outputs bounding boxes, class labels, scores, and annotated result image.
/// </summary>
[NodeInfo("MP Object Detection", NodeCategories.MediaPipe,
    Description = "Detect objects using SSD MobileNet V2 (COCO 80 classes)")]
public class MPObjectDetectionNode : BaseNode
{
    private InputPort<Mat> _imageInput = null!;
    private OutputPort<Mat> _resultOutput = null!;
    private OutputPort<Rect[]> _boxesOutput = null!;
    private OutputPort<string[]> _labelsOutput = null!;
    private OutputPort<double[]> _scoresOutput = null!;
    private OutputPort<int> _countOutput = null!;

    private NodeProperty _confidence = null!;
    private NodeProperty _maxDetections = null!;
    private NodeProperty _nmsThreshold = null!;

    private const string ModelFile = "ssd_mobilenet_v2.onnx";
    private const int InputSize = 300;

    // Colors for different classes (cycling)
    private static readonly Scalar[] ClassColors =
    {
        new(0, 255, 0), new(255, 0, 0), new(0, 0, 255), new(255, 255, 0),
        new(255, 0, 255), new(0, 255, 255), new(128, 255, 0), new(255, 128, 0),
        new(0, 128, 255), new(128, 0, 255), new(255, 0, 128), new(0, 255, 128)
    };

    protected override void Setup()
    {
        _imageInput = AddInput<Mat>("Image");

        _resultOutput = AddOutput<Mat>("Result");
        _boxesOutput = AddOutput<Rect[]>("BoundingBoxes");
        _labelsOutput = AddOutput<string[]>("Labels");
        _scoresOutput = AddOutput<double[]>("Scores");
        _countOutput = AddOutput<int>("Count");

        _confidence = AddDoubleProperty("Confidence", "Confidence", 0.5, 0.0, 1.0, "Minimum detection confidence");
        _maxDetections = AddIntProperty("MaxDetections", "Max Detections", 20, 1, 100, "Maximum number of detections");
        _nmsThreshold = AddDoubleProperty("NMSThreshold", "NMS Threshold", 0.45, 0.0, 1.0, "Non-Maximum Suppression IoU threshold");
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

            var session = MediaPipeHelper.GetSession(ModelFile);
            var threshold = (float)_confidence.GetValue<double>();
            var maxDet = _maxDetections.GetValue<int>();
            var nmsThresh = (float)_nmsThreshold.GetValue<double>();

            // Determine input format from model metadata (NCHW vs NHWC)
            var inputName = session.InputNames[0];
            var inputShape = session.InputMetadata[inputName].Dimensions;
            bool isNCHW = inputShape.Length >= 4 && inputShape[1] == 3;

            float[] inputData;
            int[] tensorShape;
            if (isNCHW)
            {
                inputData = MediaPipeHelper.PreprocessImageNCHW(image, InputSize, InputSize);
                tensorShape = new[] { 1, 3, InputSize, InputSize };
            }
            else
            {
                inputData = MediaPipeHelper.PreprocessImageNHWC(image, InputSize, InputSize);
                tensorShape = new[] { 1, InputSize, InputSize, 3 };
            }

            var inputs = new List<NamedOnnxValue>
            {
                MediaPipeHelper.CreateTensor(inputName, inputData, tensorShape)
            };

            // Run inference
            using var results = session.Run(inputs);
            var outputs = results.ToList();

            var result = image.Clone();
            if (result.Channels() == 1)
                Cv2.CvtColor(result, result, ColorConversionCodes.GRAY2BGR);

            // Parse outputs - different SSD models have different output formats
            // Common format: detection_boxes, detection_classes, detection_scores, num_detections
            var boxList = new List<Rect>();
            var labelList = new List<string>();
            var scoreList = new List<double>();

            if (outputs.Count >= 4)
            {
                // Standard TF SSD format
                ParseStandardSSDOutput(outputs, image.Width, image.Height, threshold, maxDet,
                    boxList, labelList, scoreList);
            }
            else if (outputs.Count >= 1)
            {
                // Single output format [1, N, 7] or [N, 7] (batch_id, class_id, score, x1, y1, x2, y2)
                ParseSingleOutput(outputs[0], image.Width, image.Height, threshold, maxDet,
                    boxList, labelList, scoreList);
            }

            // Draw detections
            for (int i = 0; i < boxList.Count; i++)
            {
                var color = ClassColors[i % ClassColors.Length];
                MediaPipeHelper.DrawDetectionBox(result, boxList[i], labelList[i],
                    (float)scoreList[i], color, 2);
            }

            // Status label
            Cv2.PutText(result, $"Objects: {boxList.Count}", new Point(10, 25),
                HersheyFonts.HersheySimplex, 0.7, new Scalar(0, 255, 0), 2);

            SetOutputValue(_resultOutput, result);
            SetOutputValue(_boxesOutput, boxList.ToArray());
            SetOutputValue(_labelsOutput, labelList.ToArray());
            SetOutputValue(_scoresOutput, scoreList.ToArray());
            SetOutputValue(_countOutput, boxList.Count);
            SetPreview(result);
            Error = null;
        }
        catch (FileNotFoundException ex)
        {
            Error = ex.Message;
        }
        catch (Exception ex)
        {
            Error = $"MP Object Detection error: {ex.Message}";
        }
    }

    /// <summary>
    /// Parse standard TF SSD output format with 4 outputs.
    /// </summary>
    private static void ParseStandardSSDOutput(List<DisposableNamedOnnxValue> outputs,
        int imgWidth, int imgHeight, float threshold, int maxDet,
        List<Rect> boxes, List<string> labels, List<double> scores)
    {
        // outputs[0]: detection_boxes [1, N, 4] as [y_min, x_min, y_max, x_max] normalized
        // outputs[1]: detection_classes [1, N] as float class IDs
        // outputs[2]: detection_scores [1, N]
        // outputs[3]: num_detections [1]
        var boxData = outputs[0].AsTensor<float>();
        var classData = outputs[1].AsTensor<float>();
        var scoreData = outputs[2].AsTensor<float>();
        var numDetData = outputs[3].AsTensor<float>();

        int numDetections = Math.Min((int)numDetData[0], maxDet);

        for (int i = 0; i < numDetections; i++)
        {
            float score = scoreData[0, i];
            if (score < threshold) continue;

            float yMin = boxData[0, i, 0];
            float xMin = boxData[0, i, 1];
            float yMax = boxData[0, i, 2];
            float xMax = boxData[0, i, 3];

            int x1 = (int)(xMin * imgWidth);
            int y1 = (int)(yMin * imgHeight);
            int w = (int)((xMax - xMin) * imgWidth);
            int h = (int)((yMax - yMin) * imgHeight);

            x1 = Math.Max(0, Math.Min(x1, imgWidth - 1));
            y1 = Math.Max(0, Math.Min(y1, imgHeight - 1));
            w = Math.Max(1, Math.Min(w, imgWidth - x1));
            h = Math.Max(1, Math.Min(h, imgHeight - y1));

            int classId = (int)classData[0, i];
            string label = MediaPipeHelper.GetCocoLabel(classId);

            boxes.Add(new Rect(x1, y1, w, h));
            labels.Add(label);
            scores.Add(score);
        }
    }

    /// <summary>
    /// Parse single-tensor output format [1, N, 7] or [N, 7].
    /// Each detection: [batch_id, class_id, score, x1, y1, x2, y2]
    /// </summary>
    private static void ParseSingleOutput(DisposableNamedOnnxValue output,
        int imgWidth, int imgHeight, float threshold, int maxDet,
        List<Rect> boxes, List<string> labels, List<double> scores)
    {
        var data = output.AsTensor<float>();
        var dims = data.Dimensions.ToArray();

        int numDetections;
        int offset;

        if (dims.Length == 3)
        {
            // [1, N, 7]
            numDetections = dims[1];
            offset = 7;
        }
        else if (dims.Length == 2)
        {
            // [N, 7]
            numDetections = dims[0];
            offset = dims[1];
        }
        else
        {
            return;
        }

        for (int i = 0; i < numDetections && boxes.Count < maxDet; i++)
        {
            float score;
            float x1Norm, y1Norm, x2Norm, y2Norm;
            int classId;

            if (dims.Length == 3)
            {
                score = data[0, i, 2];
                classId = (int)data[0, i, 1];
                x1Norm = data[0, i, 3];
                y1Norm = data[0, i, 4];
                x2Norm = data[0, i, 5];
                y2Norm = data[0, i, 6];
            }
            else
            {
                score = data[i, 2];
                classId = (int)data[i, 1];
                x1Norm = data[i, 3];
                y1Norm = data[i, 4];
                x2Norm = data[i, 5];
                y2Norm = data[i, 6];
            }

            if (score < threshold) continue;

            int x1 = (int)(x1Norm * imgWidth);
            int y1 = (int)(y1Norm * imgHeight);
            int w = (int)((x2Norm - x1Norm) * imgWidth);
            int h = (int)((y2Norm - y1Norm) * imgHeight);

            x1 = Math.Max(0, Math.Min(x1, imgWidth - 1));
            y1 = Math.Max(0, Math.Min(y1, imgHeight - 1));
            w = Math.Max(1, Math.Min(w, imgWidth - x1));
            h = Math.Max(1, Math.Min(h, imgHeight - y1));

            string label = MediaPipeHelper.GetCocoLabel(classId);
            boxes.Add(new Rect(x1, y1, w, h));
            labels.Add(label);
            scores.Add(score);
        }
    }
}
