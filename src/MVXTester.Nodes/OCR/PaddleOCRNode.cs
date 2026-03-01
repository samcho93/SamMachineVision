using System.Text;
using OpenCvSharp;
using MVXTester.Core.Models;
using MVXTester.Core.Registry;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace MVXTester.Nodes.OCR;

/// <summary>
/// PaddleOCR text detection and recognition node.
/// Uses PP-OCR detection (DB algorithm) + recognition (CTC decode) pipeline.
/// Supports multilingual text: download the appropriate language model and dictionary.
/// Models: huggingface.co/monkt/paddleocr-onnx
/// </summary>
[NodeInfo("PaddleOCR", NodeCategories.OCR,
    Description = "Detect and recognize text using PaddleOCR (multilingual)")]
public class PaddleOCRNode : BaseNode
{
    private InputPort<Mat> _imageInput = null!;
    private OutputPort<Mat> _resultOutput = null!;
    private OutputPort<string[]> _textsOutput = null!;
    private OutputPort<Rect[]> _boxesOutput = null!;
    private OutputPort<double[]> _scoresOutput = null!;
    private OutputPort<int> _countOutput = null!;
    private OutputPort<string> _fullTextOutput = null!;

    private NodeProperty _detThreshold = null!;
    private NodeProperty _recThreshold = null!;
    private NodeProperty _maxSideLen = null!;
    private NodeProperty _detModel = null!;
    private NodeProperty _recModel = null!;
    private NodeProperty _dictFile = null!;

    private static readonly Scalar BoxColor = new(0, 200, 255);
    private static readonly Scalar TextColor = new(255, 255, 255);
    private static readonly Scalar TextBgColor = new(0, 150, 200);

    /// <summary>
    /// OpenCV's putText only supports ASCII via P/Invoke marshaling.
    /// Non-ASCII chars (Korean, Chinese, etc.) cause "Cannot marshal: unmappable character".
    /// Full Unicode text is preserved in output ports and TextPreview (WPF handles it).
    /// </summary>
    private static string ToAsciiLabel(string text, int maxLen = 20)
    {
        var sb = new StringBuilder(Math.Min(text.Length, maxLen));
        int count = 0;
        foreach (char c in text)
        {
            if (count >= maxLen) { sb.Append(".."); break; }
            sb.Append(c <= 126 ? c : '?');
            count++;
        }
        return sb.ToString();
    }

    protected override void Setup()
    {
        _imageInput = AddInput<Mat>("Image");

        _resultOutput = AddOutput<Mat>("Result");
        _textsOutput = AddOutput<string[]>("Texts");
        _boxesOutput = AddOutput<Rect[]>("Boxes");
        _scoresOutput = AddOutput<double[]>("Scores");
        _countOutput = AddOutput<int>("Count");
        _fullTextOutput = AddOutput<string>("FullText");

        _detThreshold = AddDoubleProperty("DetThreshold", "Det Threshold", 0.3, 0.0, 1.0, "Detection binary threshold");
        _recThreshold = AddDoubleProperty("RecThreshold", "Rec Threshold", 0.5, 0.0, 1.0, "Recognition confidence threshold");
        _maxSideLen = AddIntProperty("MaxSideLen", "Max Side Length", 960, 320, 2048, "Max image side for detection");
        _detModel = AddStringProperty("DetModel", "Det Model", "ppocr_det.onnx", "Detection model filename");
        _recModel = AddStringProperty("RecModel", "Rec Model", "ppocr_rec.onnx", "Recognition model filename");
        _dictFile = AddStringProperty("DictFile", "Dictionary", "ppocr_keys.txt", "Character dictionary filename");
    }

    public override void Process()
    {
        string step = "init";
        try
        {
            var image = GetInputValue(_imageInput);
            if (image == null || image.Empty())
            {
                Error = "No input image";
                return;
            }

            var detThresh = (float)_detThreshold.GetValue<double>();
            var recThresh = (float)_recThreshold.GetValue<double>();
            var maxSide = _maxSideLen.GetValue<int>();
            var detModelName = _detModel.GetValue<string>();
            var recModelName = _recModel.GetValue<string>();
            var dictFileName = _dictFile.GetValue<string>();

            // Load models and dictionary
            step = "load det model";
            var detSession = PaddleOCRHelper.GetSession(detModelName);
            step = "load rec model";
            var recSession = PaddleOCRHelper.GetSession(recModelName);
            step = "load dictionary";
            var dictionary = PaddleOCRHelper.LoadDictionary(dictFileName);

            // === Stage 1: Text Detection ===
            step = "det preprocess";
            var (detData, detH, detW) = PaddleOCRHelper.PreprocessForDetection(image, maxSide);

            step = "det run";
            var detInputName = PaddleOCRHelper.GetSafeInputName(detSession);
            var detTensor = new DenseTensor<float>(detData, new[] { 1, 3, detH, detW });
            var detInputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor(detInputName, detTensor)
            };

            using var detResults = detSession.Run(detInputs);
            step = "det output";
            var detOutput = detResults.First().AsTensor<float>();
            var detFlat = detOutput is DenseTensor<float> detDense
                ? detDense.Buffer.ToArray()
                : detOutput.ToArray();
            var detDims = detOutput.Dimensions.ToArray();

            // Output probability map: [1, 1, H, W] or [1, H, W]
            int mapH = detDims.Length == 4 ? detDims[2] : detDims[1];
            int mapW = detDims.Length == 4 ? detDims[3] : detDims[^1];

            // If output has channel dim, skip it
            float[] probMap;
            if (detDims.Length == 4 && detDims[1] == 1)
                probMap = detFlat.Skip(0).Take(mapH * mapW).ToArray();
            else
                probMap = detFlat;

            step = "DB post-process";
            var textRegions = PaddleOCRHelper.DBPostProcess(
                probMap, mapH, mapW, image.Height, image.Width,
                detThresh, boxThresh: 0.6f, unclipRatio: 1.5f);

            PaddleOCRHelper.SortInReadingOrder(textRegions);

            // === Stage 2: Text Recognition ===
            step = "rec metadata";
            var recInputName = PaddleOCRHelper.GetSafeInputName(recSession);
            // PP-OCRv5: input height=32, dynamic width
            var recDims4 = PaddleOCRHelper.GetSafeInputDimensions(
                recSession, new[] { 1, 3, 32, 320 });
            int recH = recDims4[2];
            int recMaxW = recDims4[3];

            var result = image.Clone();
            if (result.Channels() == 1)
                Cv2.CvtColor(result, result, ColorConversionCodes.GRAY2BGR);

            var textList = new List<string>();
            var boxList = new List<Rect>();
            var scoreList = new List<double>();
            var fullTextSb = new StringBuilder();

            foreach (var (box, detScore) in textRegions)
            {
                // Clamp box to image bounds
                int x1 = Math.Max(0, box.X);
                int y1 = Math.Max(0, box.Y);
                int x2 = Math.Min(image.Width, box.X + box.Width);
                int y2 = Math.Min(image.Height, box.Y + box.Height);
                if (x2 - x1 < 3 || y2 - y1 < 3) continue;

                var cropRect = new Rect(x1, y1, x2 - x1, y2 - y1);
                using var crop = new Mat(image, cropRect);

                step = "rec preprocess";
                var (recData, actualW) = PaddleOCRHelper.PreprocessForRecognition(crop, recH, recMaxW);
                var recTensor = new DenseTensor<float>(recData, new[] { 1, 3, recH, actualW });
                var recInputs = new List<NamedOnnxValue>
                {
                    NamedOnnxValue.CreateFromTensor(recInputName, recTensor)
                };

                step = "rec run";
                using var recResults = recSession.Run(recInputs);
                step = "rec output";
                var recOutput = recResults.First().AsTensor<float>();
                var recFlat = recOutput is DenseTensor<float> recDense
                    ? recDense.Buffer.ToArray()
                    : recOutput.ToArray();
                var recDims = recOutput.Dimensions.ToArray();

                // Output: [1, timesteps, numChars] or [1, numChars, timesteps]
                int dim1 = recDims[1], dim2 = recDims[2];
                int timesteps, numChars;

                int expectedChars = dictionary.Length + 1;
                if (dim2 == expectedChars || dim2 > dim1)
                {
                    timesteps = dim1;
                    numChars = dim2;
                }
                else
                {
                    timesteps = dim2;
                    numChars = dim1;
                    var transposed = new float[recFlat.Length];
                    for (int t = 0; t < timesteps; t++)
                        for (int c = 0; c < numChars; c++)
                            transposed[t * numChars + c] = recFlat[c * timesteps + t];
                    recFlat = transposed;
                }

                step = "CTC decode";
                var (text, confidence) = PaddleOCRHelper.CTCDecode(recFlat, timesteps, numChars, dictionary);

                if (string.IsNullOrWhiteSpace(text) || confidence < recThresh) continue;

                textList.Add(text);
                boxList.Add(cropRect);
                scoreList.Add(confidence);

                if (fullTextSb.Length > 0) fullTextSb.Append('\n');
                fullTextSb.Append(text);

                step = "draw box";
                Cv2.Rectangle(result, cropRect, BoxColor, 2);

                // Draw text label (ASCII only - OpenCV putText doesn't support Unicode)
                string label = ToAsciiLabel(text);
                var textSize = Cv2.GetTextSize(label, HersheyFonts.HersheySimplex, 0.45, 1, out int baseline);
                int labelY = Math.Max(cropRect.Y - 2, textSize.Height + 4);

                Cv2.Rectangle(result,
                    new Point(cropRect.X, labelY - textSize.Height - 4),
                    new Point(cropRect.X + textSize.Width + 4, labelY + baseline),
                    TextBgColor, -1);
                Cv2.PutText(result, label, new Point(cropRect.X + 2, labelY - 2),
                    HersheyFonts.HersheySimplex, 0.45, TextColor, 1, LineTypes.AntiAlias);
            }

            step = "output";
            Cv2.PutText(result, $"Texts: {textList.Count}", new Point(10, 25),
                HersheyFonts.HersheySimplex, 0.7, new Scalar(0, 255, 0), 2);

            SetOutputValue(_resultOutput, result);
            SetOutputValue(_textsOutput, textList.ToArray());
            SetOutputValue(_boxesOutput, boxList.ToArray());
            SetOutputValue(_scoresOutput, scoreList.ToArray());
            SetOutputValue(_countOutput, textList.Count);
            SetOutputValue(_fullTextOutput, fullTextSb.ToString());
            SetPreview(result);

            // Show recognized text in text preview (WPF handles Unicode)
            if (textList.Count > 0)
            {
                var previewSb = new StringBuilder();
                for (int i = 0; i < textList.Count; i++)
                    previewSb.AppendLine($"[{i + 1}] {textList[i]} ({scoreList[i]:P0})");
                SetTextPreview(previewSb.ToString());
            }
            else
            {
                SetTextPreview("No text detected");
            }

            Error = null;
        }
        catch (FileNotFoundException ex)
        {
            Error = ex.Message;
        }
        catch (Exception ex)
        {
            Error = $"PaddleOCR [{step}]: {ex.Message}";
        }
    }
}
