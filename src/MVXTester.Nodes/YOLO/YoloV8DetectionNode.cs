using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;
using OpenCvSharp;
using MVXTester.Core.Models;
using MVXTester.Core.Registry;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace MVXTester.Nodes.YOLO;

/// <summary>
/// YOLOv8 object detection node.
/// Supports any YOLOv8 ONNX model (nano/small/medium/large/xlarge).
/// Auto-detects input size and number of classes from model metadata.
/// Export: yolo export model=yolov8n.pt format=onnx
/// </summary>
[NodeInfo("YOLOv8 Detection", NodeCategories.YOLO,
    Description = "Detect objects using YOLOv8 (auto-detect classes from model)")]
public class YoloV8DetectionNode : BaseNode
{
    private InputPort<Mat> _imageInput = null!;
    private OutputPort<Mat> _resultOutput = null!;
    private OutputPort<Rect[]> _boxesOutput = null!;
    private OutputPort<string[]> _labelsOutput = null!;
    private OutputPort<double[]> _scoresOutput = null!;
    private OutputPort<int> _countOutput = null!;

    private NodeProperty _confidence = null!;
    private NodeProperty _iouThreshold = null!;
    private NodeProperty _maxDetections = null!;
    private NodeProperty _modelFile = null!;

    private static readonly ConcurrentDictionary<string, InferenceSession> _sessionCache = new();
    private string[]? _classNames;

    private static readonly Scalar[] ClassColors =
    {
        new(0, 255, 0), new(255, 0, 0), new(0, 0, 255), new(255, 255, 0),
        new(255, 0, 255), new(0, 255, 255), new(128, 255, 0), new(255, 128, 0),
        new(0, 128, 255), new(128, 0, 255), new(255, 0, 128), new(0, 255, 128),
        new(64, 255, 64), new(255, 64, 64), new(64, 64, 255), new(255, 255, 128)
    };

    // Standard COCO 80-class labels (0-indexed, used when model has no embedded names)
    private static readonly string[] Coco80Labels =
    {
        "person", "bicycle", "car", "motorcycle", "airplane", "bus", "train", "truck", "boat", "traffic light",
        "fire hydrant", "stop sign", "parking meter", "bench", "bird", "cat", "dog", "horse", "sheep", "cow",
        "elephant", "bear", "zebra", "giraffe", "backpack", "umbrella", "handbag", "tie", "suitcase", "frisbee",
        "skis", "snowboard", "sports ball", "kite", "baseball bat", "baseball glove", "skateboard", "surfboard",
        "tennis racket", "bottle", "wine glass", "cup", "fork", "knife", "spoon", "bowl", "banana", "apple",
        "sandwich", "orange", "broccoli", "carrot", "hot dog", "pizza", "donut", "cake", "chair", "couch",
        "potted plant", "bed", "dining table", "toilet", "tv", "laptop", "mouse", "remote", "keyboard", "cell phone",
        "microwave", "oven", "toaster", "sink", "refrigerator", "book", "clock", "vase", "scissors", "teddy bear",
        "hair drier", "toothbrush"
    };

    protected override void Setup()
    {
        _imageInput = AddInput<Mat>("Image");

        _resultOutput = AddOutput<Mat>("Result");
        _boxesOutput = AddOutput<Rect[]>("BoundingBoxes");
        _labelsOutput = AddOutput<string[]>("Labels");
        _scoresOutput = AddOutput<double[]>("Scores");
        _countOutput = AddOutput<int>("Count");

        _confidence = AddDoubleProperty("Confidence", "Confidence", 0.25, 0.0, 1.0, "Minimum detection confidence");
        _iouThreshold = AddDoubleProperty("IoUThreshold", "IoU Threshold", 0.45, 0.0, 1.0, "NMS IoU threshold");
        _maxDetections = AddIntProperty("MaxDetections", "Max Detections", 100, 1, 300, "Maximum number of detections");
        _modelFile = AddStringProperty("ModelFile", "Model File", "yolov8n.onnx", "ONNX model filename in Models/YOLO/");
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

            var modelName = _modelFile.GetValue<string>();
            var session = GetSession(modelName);
            var threshold = (float)_confidence.GetValue<double>();
            var iouThresh = (float)_iouThreshold.GetValue<double>();
            var maxDet = _maxDetections.GetValue<int>();

            // Auto-detect input size from model
            var inputName = session.InputNames[0];
            var inputShape = session.InputMetadata[inputName].Dimensions;
            int inputH = inputShape[2] > 0 ? inputShape[2] : 640;
            int inputW = inputShape[3] > 0 ? inputShape[3] : 640;

            // Load class names from model metadata (once)
            if (_classNames == null)
                _classNames = LoadClassNames(session);

            // Letterbox preprocessing
            var (inputData, ratio, padX, padY) = LetterboxPreprocess(image, inputW, inputH);

            var tensorShape = new[] { 1, 3, inputH, inputW };
            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor(inputName,
                    new DenseTensor<float>(inputData, tensorShape))
            };

            // Run inference
            using var results = session.Run(inputs);
            var output = results.First();
            var outputTensor = output.AsTensor<float>();
            var outputDims = outputTensor.Dimensions.ToArray();

            // Parse output: [1, 4+numClasses, numDetections]
            int numChannels = outputDims[1];
            int numDetections = outputDims[2];
            int numClasses = numChannels - 4;

            var flat = outputTensor is DenseTensor<float> dense
                ? dense.Buffer.ToArray()
                : outputTensor.ToArray();

            // Extract detections
            var rawBoxes = new List<Rect>();
            var rawScores = new List<float>();
            var rawClassIds = new List<int>();

            for (int i = 0; i < numDetections; i++)
            {
                // Find best class score
                float maxScore = 0;
                int bestClass = 0;
                for (int c = 0; c < numClasses; c++)
                {
                    float score = flat[(4 + c) * numDetections + i];
                    if (score > maxScore)
                    {
                        maxScore = score;
                        bestClass = c;
                    }
                }

                if (maxScore < threshold) continue;

                // Extract box (cx, cy, w, h) in input image space
                float cx = flat[0 * numDetections + i];
                float cy = flat[1 * numDetections + i];
                float bw = flat[2 * numDetections + i];
                float bh = flat[3 * numDetections + i];

                // Convert to x1,y1,x2,y2 and undo letterbox
                float x1 = (cx - bw / 2 - padX) / ratio;
                float y1 = (cy - bh / 2 - padY) / ratio;
                float x2 = (cx + bw / 2 - padX) / ratio;
                float y2 = (cy + bh / 2 - padY) / ratio;

                // Clamp to image bounds
                int ix1 = Math.Max(0, (int)x1);
                int iy1 = Math.Max(0, (int)y1);
                int ix2 = Math.Min(image.Width, (int)x2);
                int iy2 = Math.Min(image.Height, (int)y2);

                int w = ix2 - ix1;
                int h = iy2 - iy1;
                if (w < 1 || h < 1) continue;

                rawBoxes.Add(new Rect(ix1, iy1, w, h));
                rawScores.Add(maxScore);
                rawClassIds.Add(bestClass);
            }

            // NMS
            var kept = ApplyNMS(rawBoxes, rawScores, iouThresh, maxDet);

            // Build output
            var result = image.Clone();
            if (result.Channels() == 1)
                Cv2.CvtColor(result, result, ColorConversionCodes.GRAY2BGR);

            var boxList = new List<Rect>();
            var labelList = new List<string>();
            var scoreList = new List<double>();

            foreach (int idx in kept)
            {
                var box = rawBoxes[idx];
                int classId = rawClassIds[idx];
                float score = rawScores[idx];
                string label = classId < _classNames.Length ? _classNames[classId] : $"class_{classId}";

                boxList.Add(box);
                labelList.Add(label);
                scoreList.Add(score);

                var color = ClassColors[classId % ClassColors.Length];
                Cv2.Rectangle(result, box, color, 2);

                string text = $"{label} {score:P0}";
                var textSize = Cv2.GetTextSize(text, HersheyFonts.HersheySimplex, 0.5, 1, out int baseline);
                int textY = Math.Max(box.Y - 4, textSize.Height + 4);
                Cv2.Rectangle(result,
                    new Point(box.X, textY - textSize.Height - 4),
                    new Point(box.X + textSize.Width + 4, textY + baseline),
                    color, -1);
                Cv2.PutText(result, text, new Point(box.X + 2, textY - 2),
                    HersheyFonts.HersheySimplex, 0.5, new Scalar(0, 0, 0), 1, LineTypes.AntiAlias);
            }

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
            Error = $"YOLOv8 error: {ex.Message}";
        }
    }

    /// <summary>
    /// Letterbox resize: maintain aspect ratio, pad with gray (114).
    /// Returns NCHW float array [1,3,H,W] normalized to [0,1], RGB.
    /// </summary>
    private static (float[] Data, float Ratio, int PadX, int PadY) LetterboxPreprocess(
        Mat image, int targetW, int targetH)
    {
        int h = image.Height, w = image.Width;
        float ratio = Math.Min((float)targetW / w, (float)targetH / h);
        int newW = (int)(w * ratio);
        int newH = (int)(h * ratio);
        int padX = (targetW - newW) / 2;
        int padY = (targetH - newH) / 2;

        using var resized = new Mat();
        Cv2.Resize(image, resized, new Size(newW, newH), 0, 0, InterpolationFlags.Linear);

        using var padded = new Mat(targetH, targetW, MatType.CV_8UC3, new Scalar(114, 114, 114));
        resized.CopyTo(padded[new Rect(padX, padY, newW, newH)]);

        // BGR to RGB, normalize to [0,1], NCHW
        using var rgb = new Mat();
        Cv2.CvtColor(padded, rgb, ColorConversionCodes.BGR2RGB);

        int total = 3 * targetH * targetW;
        float[] data = new float[total];
        int hw = targetH * targetW;

        for (int y = 0; y < targetH; y++)
        {
            for (int x = 0; x < targetW; x++)
            {
                var pixel = rgb.At<Vec3b>(y, x);
                int offset = y * targetW + x;
                data[offset] = pixel.Item0 / 255f;           // R
                data[hw + offset] = pixel.Item1 / 255f;      // G
                data[2 * hw + offset] = pixel.Item2 / 255f;  // B
            }
        }

        return (data, ratio, padX, padY);
    }

    /// <summary>
    /// Non-Maximum Suppression. Returns indices of kept detections.
    /// </summary>
    private static List<int> ApplyNMS(List<Rect> boxes, List<float> scores, float iouThreshold, int maxDet)
    {
        // Sort by score descending
        var indices = Enumerable.Range(0, boxes.Count)
            .OrderByDescending(i => scores[i])
            .ToList();

        var kept = new List<int>();

        while (indices.Count > 0 && kept.Count < maxDet)
        {
            int best = indices[0];
            kept.Add(best);
            indices.RemoveAt(0);

            indices.RemoveAll(i => IoU(boxes[best], boxes[i]) > iouThreshold);
        }

        return kept;
    }

    private static float IoU(Rect a, Rect b)
    {
        int x1 = Math.Max(a.X, b.X);
        int y1 = Math.Max(a.Y, b.Y);
        int x2 = Math.Min(a.X + a.Width, b.X + b.Width);
        int y2 = Math.Min(a.Y + a.Height, b.Y + b.Height);

        if (x2 <= x1 || y2 <= y1) return 0;

        float intersection = (x2 - x1) * (y2 - y1);
        float union = (float)(a.Width * a.Height + b.Width * b.Height) - intersection;
        return union > 0 ? intersection / union : 0;
    }

    /// <summary>
    /// Load class names from model metadata "names" field, or fall back to COCO 80.
    /// YOLOv8 embeds: {0: 'person', 1: 'bicycle', ...}
    /// </summary>
    private static string[] LoadClassNames(InferenceSession session)
    {
        try
        {
            var metadata = session.ModelMetadata.CustomMetadataMap;
            if (metadata.TryGetValue("names", out var namesStr) && !string.IsNullOrEmpty(namesStr))
            {
                var matches = Regex.Matches(namesStr, @"(\d+)\s*:\s*'([^']+)'");
                if (matches.Count > 0)
                {
                    int maxIdx = matches.Cast<Match>().Max(m => int.Parse(m.Groups[1].Value));
                    var names = new string[maxIdx + 1];
                    for (int i = 0; i < names.Length; i++) names[i] = $"class_{i}";

                    foreach (Match m in matches)
                    {
                        int idx = int.Parse(m.Groups[1].Value);
                        names[idx] = m.Groups[2].Value;
                    }
                    return names;
                }
            }
        }
        catch { /* Fall back to defaults */ }

        return Coco80Labels;
    }

    /// <summary>
    /// Get or create ONNX inference session with caching.
    /// </summary>
    private static InferenceSession GetSession(string modelFileName)
    {
        var modelPath = ResolveModelPath(modelFileName)
            ?? throw new FileNotFoundException(
                $"YOLO model not found: {modelFileName}. " +
                $"Place the model file in Models/YOLO/ folder. " +
                $"Export: yolo export model=yolov8n.pt format=onnx");

        return _sessionCache.GetOrAdd(modelPath, path =>
        {
            var opts = new SessionOptions();
            opts.InterOpNumThreads = 1;
            opts.IntraOpNumThreads = Math.Max(1, Environment.ProcessorCount / 2);
            opts.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;
            return new InferenceSession(path, opts);
        });
    }

    private static string? ResolveModelPath(string fileName)
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;

        if (Path.IsPathRooted(fileName) && File.Exists(fileName))
            return fileName;

        var path = Path.Combine(baseDir, "Models", "YOLO", fileName);
        if (File.Exists(path)) return path;

        path = Path.Combine(baseDir, fileName);
        if (File.Exists(path)) return path;

        path = Path.Combine("Models", "YOLO", fileName);
        if (File.Exists(path)) return path;

        return null;
    }
}
