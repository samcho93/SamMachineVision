using System.Collections.Concurrent;
using System.Text;
using OpenCvSharp;
using Tesseract;
using MVXTester.Core.Models;
using MVXTester.Core.Registry;
using CvRect = OpenCvSharp.Rect;

namespace MVXTester.Nodes.OCR;

/// <summary>
/// Tesseract OCR text recognition node.
/// Uses Tesseract 5 engine for text detection and recognition.
/// Supports multilingual text via traineddata files.
/// Place traineddata files in Models/Tesseract/ folder.
/// Download from: github.com/tesseract-ocr/tessdata
/// </summary>
[NodeInfo("Tesseract OCR", NodeCategories.OCR,
    Description = "Detect and recognize text using Tesseract OCR (eng/kor)")]
public class TesseractOCRNode : BaseNode
{
    private InputPort<Mat> _imageInput = null!;
    private OutputPort<Mat> _resultOutput = null!;
    private OutputPort<string[]> _textsOutput = null!;
    private OutputPort<CvRect[]> _boxesOutput = null!;
    private OutputPort<double[]> _scoresOutput = null!;
    private OutputPort<int> _countOutput = null!;
    private OutputPort<string> _fullTextOutput = null!;

    private NodeProperty _language = null!;
    private NodeProperty _confidence = null!;
    private NodeProperty _pageSegMode = null!;
    private NodeProperty _iterLevel = null!;

    private static readonly ConcurrentDictionary<string, TesseractEngine> _engineCache = new();

    private static readonly Scalar BoxColor = new(0, 200, 255);
    private static readonly Scalar TextColor = new(255, 255, 255);
    private static readonly Scalar TextBgColor = new(0, 150, 200);

    /// <summary>
    /// OpenCV putText only supports ASCII. Replace non-ASCII with '?'.
    /// </summary>
    private static string ToAsciiLabel(string text, int maxLen = 25)
    {
        var sb = new StringBuilder(Math.Min(text.Length, maxLen));
        int count = 0;
        foreach (char c in text)
        {
            if (count >= maxLen) { sb.Append(".."); break; }
            sb.Append(c <= 126 && c >= 32 ? c : '?');
            count++;
        }
        return sb.ToString();
    }

    protected override void Setup()
    {
        _imageInput = AddInput<Mat>("Image");

        _resultOutput = AddOutput<Mat>("Result");
        _textsOutput = AddOutput<string[]>("Texts");
        _boxesOutput = AddOutput<CvRect[]>("Boxes");
        _scoresOutput = AddOutput<double[]>("Scores");
        _countOutput = AddOutput<int>("Count");
        _fullTextOutput = AddOutput<string>("FullText");

        _language = AddStringProperty("Language", "Language", "eng+kor",
            "Tesseract language code (eng, kor, eng+kor, jpn, chi_sim, etc.)");
        _confidence = AddDoubleProperty("Confidence", "Confidence", 0.5, 0.0, 1.0,
            "Minimum confidence threshold");
        _pageSegMode = AddIntProperty("PageSegMode", "Page Seg Mode", 3, 0, 13,
            "0=OSD,1=Auto+OSD,3=Auto,6=Block,7=Line,8=Word,10=Char,11=Sparse,13=RawLine");
        _iterLevel = AddIntProperty("IterLevel", "Iter Level", 1, 0, 4,
            "0=Block,1=Line,2=Word,3=Symbol");
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

            var lang = _language.GetValue<string>();
            var minConf = (float)_confidence.GetValue<double>();
            var psmValue = _pageSegMode.GetValue<int>();
            var levelValue = _iterLevel.GetValue<int>();

            // Get or create engine
            step = "load engine";
            var engine = GetEngine(lang);

            // Convert Mat to PNG bytes for Tesseract
            step = "encode image";
            Cv2.ImEncode(".png", image, out byte[] imageBytes);

            step = "process";
            using var pix = Pix.LoadFromMemory(imageBytes);
            var psm = (PageSegMode)Math.Clamp(psmValue, 0, 13);
            using var page = engine.Process(pix, psm);

            // Get full text
            string fullText = page.GetText()?.Trim() ?? "";

            // Get bounding boxes at specified level
            step = "iterate results";
            var level = (PageIteratorLevel)Math.Clamp(levelValue, 0, 4);

            var result = image.Clone();
            if (result.Channels() == 1)
                Cv2.CvtColor(result, result, ColorConversionCodes.GRAY2BGR);

            var textList = new List<string>();
            var boxList = new List<CvRect>();
            var scoreList = new List<double>();

            using var iter = page.GetIterator();
            iter.Begin();

            do
            {
                if (!iter.TryGetBoundingBox(level, out var bounds))
                    continue;

                string? text = iter.GetText(level);
                if (string.IsNullOrWhiteSpace(text)) continue;

                float conf = iter.GetConfidence(level) / 100f; // Tesseract returns 0-100
                if (conf < minConf) continue;

                text = text.Trim();
                if (text.Length == 0) continue;

                var box = new CvRect(bounds.X1, bounds.Y1, bounds.Width, bounds.Height);

                // Clamp to image bounds
                box.X = Math.Max(0, box.X);
                box.Y = Math.Max(0, box.Y);
                box.Width = Math.Min(box.Width, image.Width - box.X);
                box.Height = Math.Min(box.Height, image.Height - box.Y);
                if (box.Width < 1 || box.Height < 1) continue;

                textList.Add(text);
                boxList.Add(box);
                scoreList.Add(conf);

                // Draw bounding box
                Cv2.Rectangle(result, box, BoxColor, 2);

                // Draw label (ASCII only for OpenCV)
                string label = ToAsciiLabel(text);
                var textSize = Cv2.GetTextSize(label, HersheyFonts.HersheySimplex, 0.45, 1, out int baseline);
                int labelY = Math.Max(box.Y - 2, textSize.Height + 4);

                Cv2.Rectangle(result,
                    new Point(box.X, labelY - textSize.Height - 4),
                    new Point(box.X + textSize.Width + 4, labelY + baseline),
                    TextBgColor, -1);
                Cv2.PutText(result, label, new Point(box.X + 2, labelY - 2),
                    HersheyFonts.HersheySimplex, 0.45, TextColor, 1, LineTypes.AntiAlias);
            } while (iter.Next(level));

            step = "output";
            Cv2.PutText(result, $"Texts: {textList.Count}", new Point(10, 25),
                HersheyFonts.HersheySimplex, 0.7, new Scalar(0, 255, 0), 2);

            SetOutputValue(_resultOutput, result);
            SetOutputValue(_textsOutput, textList.ToArray());
            SetOutputValue(_boxesOutput, boxList.ToArray());
            SetOutputValue(_scoresOutput, scoreList.ToArray());
            SetOutputValue(_countOutput, textList.Count);
            SetOutputValue(_fullTextOutput, fullText);
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
            Error = $"Tesseract [{step}]: {ex.Message}";
        }
    }

    private static TesseractEngine GetEngine(string language)
    {
        return _engineCache.GetOrAdd(language, lang =>
        {
            var tessdataPath = ResolveTessdataPath()
                ?? throw new FileNotFoundException(
                    $"Tesseract data not found. Place .traineddata files in Models/Tesseract/ folder. " +
                    $"Download from: github.com/tesseract-ocr/tessdata");

            return new TesseractEngine(tessdataPath, lang, EngineMode.Default);
        });
    }

    private static string? ResolveTessdataPath()
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;

        // Check Models/Tesseract/
        var path = Path.Combine(baseDir, "Models", "Tesseract");
        if (Directory.Exists(path) && Directory.GetFiles(path, "*.traineddata").Length > 0)
            return path;

        // Check tessdata/
        path = Path.Combine(baseDir, "tessdata");
        if (Directory.Exists(path) && Directory.GetFiles(path, "*.traineddata").Length > 0)
            return path;

        // Check relative
        path = Path.Combine("Models", "Tesseract");
        if (Directory.Exists(path) && Directory.GetFiles(path, "*.traineddata").Length > 0)
            return path;

        return null;
    }

    public static void DisposeAll()
    {
        foreach (var engine in _engineCache.Values)
            engine.Dispose();
        _engineCache.Clear();
    }
}
