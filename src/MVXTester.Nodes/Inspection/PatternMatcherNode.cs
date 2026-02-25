using OpenCvSharp;
using MVXTester.Core.Models;
using MVXTester.Core.Registry;

namespace MVXTester.Nodes.Inspection;

[NodeInfo("Pattern Matcher", NodeCategories.Inspection,
    Description = "Finds all occurrences of a pattern in image with pass/fail")]
public class PatternMatcherNode : BaseNode
{
    private InputPort<Mat> _imageInput = null!;
    private InputPort<Mat> _templateInput = null!;
    private OutputPort<Mat> _resultOutput = null!;
    private OutputPort<Point[]> _matchesOutput = null!;
    private OutputPort<int> _countOutput = null!;
    private OutputPort<bool> _passOutput = null!;

    private NodeProperty _matchThreshold = null!;
    private NodeProperty _expectedMin = null!;
    private NodeProperty _expectedMax = null!;
    private NodeProperty _nmsOverlap = null!;

    protected override void Setup()
    {
        _imageInput = AddInput<Mat>("Image");
        _templateInput = AddInput<Mat>("Template");

        _resultOutput = AddOutput<Mat>("Result");
        _matchesOutput = AddOutput<Point[]>("Matches");
        _countOutput = AddOutput<int>("Count");
        _passOutput = AddOutput<bool>("Pass");

        _matchThreshold = AddDoubleProperty("MatchThreshold", "Match Threshold", 0.8, 0.0, 1.0,
            "Minimum match score");
        _expectedMin = AddIntProperty("ExpectedMin", "Expected Min", 1, 0, 1000,
            "Expected minimum number of matches");
        _expectedMax = AddIntProperty("ExpectedMax", "Expected Max", 100, 0, 1000,
            "Expected maximum matches");
        _nmsOverlap = AddDoubleProperty("NmsOverlap", "NMS Overlap", 0.3, 0.0, 1.0,
            "NMS overlap threshold");
    }

    public override void Process()
    {
        try
        {
            var image = GetInputValue(_imageInput);
            var template = GetInputValue(_templateInput);

            if (image == null || image.Empty())
            {
                Error = "No input image";
                return;
            }
            if (template == null || template.Empty())
            {
                Error = "No template image";
                return;
            }

            var matchThreshold = _matchThreshold.GetValue<double>();
            var expectedMin = _expectedMin.GetValue<int>();
            var expectedMax = _expectedMax.GetValue<int>();
            var nmsOverlap = _nmsOverlap.GetValue<double>();

            int tw = template.Width;
            int th = template.Height;

            // Template matching
            using var matchResult = new Mat();
            Cv2.MatchTemplate(image, template, matchResult, TemplateMatchModes.CCoeffNormed);

            // Find all locations above threshold
            var candidates = new List<(Point Location, double Score)>();
            using var thresholded = new Mat();
            Cv2.Threshold(matchResult, thresholded, matchThreshold, 1.0, ThresholdTypes.Binary);
            thresholded.ConvertTo(thresholded, MatType.CV_8U, 255);

            // Get all matching points
            var nonZero = new Mat();
            Cv2.FindNonZero(thresholded, nonZero);

            if (!nonZero.Empty())
            {
                for (int i = 0; i < nonZero.Rows; i++)
                {
                    var pt = nonZero.At<Point>(i);
                    double score = matchResult.At<float>(pt.Y, pt.X);
                    candidates.Add((pt, score));
                }
            }
            nonZero.Dispose();

            // Sort by score descending for NMS
            candidates.Sort((a, b) => b.Score.CompareTo(a.Score));

            // Non-Maximum Suppression
            var matches = new List<Point>();
            var kept = new List<bool>(new bool[candidates.Count]);
            for (int i = 0; i < kept.Count; i++) kept[i] = true;

            for (int i = 0; i < candidates.Count; i++)
            {
                if (!kept[i]) continue;
                matches.Add(candidates[i].Location);

                var rectI = new Rect(candidates[i].Location.X, candidates[i].Location.Y, tw, th);

                for (int j = i + 1; j < candidates.Count; j++)
                {
                    if (!kept[j]) continue;
                    var rectJ = new Rect(candidates[j].Location.X, candidates[j].Location.Y, tw, th);

                    double overlap = ComputeIoU(rectI, rectJ);
                    if (overlap > nmsOverlap)
                        kept[j] = false;
                }
            }

            // Pass/fail check
            int count = matches.Count;
            bool pass = count >= expectedMin && count <= expectedMax;

            // Build result image
            var result = image.Clone();
            if (result.Channels() == 1)
                Cv2.CvtColor(result, result, ColorConversionCodes.GRAY2BGR);

            // Draw match rectangles in green
            foreach (var loc in matches)
            {
                var rect = new Rect(loc.X, loc.Y, tw, th);
                Cv2.Rectangle(result, rect, new Scalar(0, 255, 0), 2);
            }

            // Draw pass/fail border
            var borderColor = pass ? new Scalar(0, 255, 0) : new Scalar(0, 0, 255);
            Cv2.Rectangle(result, new Rect(0, 0, result.Width, result.Height), borderColor, 4);

            // Status text
            var statusText = pass ? $"PASS: {count} matches" : $"FAIL: {count} matches";
            var textColor = pass ? new Scalar(0, 255, 0) : new Scalar(0, 0, 255);
            Cv2.PutText(result, statusText, new Point(10, 30),
                HersheyFonts.HersheySimplex, 0.8, textColor, 2);

            SetOutputValue(_resultOutput, result);
            SetOutputValue(_matchesOutput, matches.ToArray());
            SetOutputValue(_countOutput, count);
            SetOutputValue(_passOutput, pass);
            SetPreview(result);
            Error = null;
        }
        catch (Exception ex)
        {
            Error = $"Pattern Matcher error: {ex.Message}";
        }
    }

    private static double ComputeIoU(Rect a, Rect b)
    {
        int x1 = Math.Max(a.X, b.X);
        int y1 = Math.Max(a.Y, b.Y);
        int x2 = Math.Min(a.X + a.Width, b.X + b.Width);
        int y2 = Math.Min(a.Y + a.Height, b.Y + b.Height);

        int intersectionArea = Math.Max(0, x2 - x1) * Math.Max(0, y2 - y1);
        if (intersectionArea == 0) return 0;

        int unionArea = a.Width * a.Height + b.Width * b.Height - intersectionArea;
        return (double)intersectionArea / unionArea;
    }
}
