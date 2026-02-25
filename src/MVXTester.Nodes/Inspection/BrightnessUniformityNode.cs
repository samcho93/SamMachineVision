using OpenCvSharp;
using MVXTester.Core.Models;
using MVXTester.Core.Registry;

namespace MVXTester.Nodes.Inspection;

[NodeInfo("Brightness Uniformity", NodeCategories.Inspection, Description = "Checks image brightness uniformity across a grid for quality inspection")]
public class BrightnessUniformityNode : BaseNode
{
    private InputPort<Mat> _imageInput = null!;
    private OutputPort<Mat> _resultOutput = null!;
    private OutputPort<double> _meanBrightnessOutput = null!;
    private OutputPort<double> _stdDevOutput = null!;
    private OutputPort<double> _minBrightnessOutput = null!;
    private OutputPort<double> _maxBrightnessOutput = null!;
    private OutputPort<bool> _isUniformOutput = null!;

    private NodeProperty _gridCols = null!;
    private NodeProperty _gridRows = null!;
    private NodeProperty _maxStdDev = null!;

    protected override void Setup()
    {
        _imageInput = AddInput<Mat>("Image");
        _resultOutput = AddOutput<Mat>("Result");
        _meanBrightnessOutput = AddOutput<double>("MeanBrightness");
        _stdDevOutput = AddOutput<double>("StdDev");
        _minBrightnessOutput = AddOutput<double>("MinBrightness");
        _maxBrightnessOutput = AddOutput<double>("MaxBrightness");
        _isUniformOutput = AddOutput<bool>("IsUniform");

        _gridCols = AddIntProperty("GridCols", "Grid Columns", 4, 1, 20, "Grid columns for analysis");
        _gridRows = AddIntProperty("GridRows", "Grid Rows", 4, 1, 20, "Grid rows for analysis");
        _maxStdDev = AddDoubleProperty("MaxStdDev", "Max Std Dev", 30.0, 0.0, 128.0, "Maximum allowed std dev for uniform (pass threshold)");
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

            var gridCols = _gridCols.GetValue<int>();
            var gridRows = _gridRows.GetValue<int>();
            var maxStdDev = _maxStdDev.GetValue<double>();

            if (gridCols < 1) gridCols = 1;
            if (gridRows < 1) gridRows = 1;

            // Step 1: Convert to grayscale
            var gray = new Mat();
            if (image.Channels() > 1)
                Cv2.CvtColor(image, gray, ColorConversionCodes.BGR2GRAY);
            else
                image.CopyTo(gray);

            int imgWidth = gray.Width;
            int imgHeight = gray.Height;
            int cellWidth = imgWidth / gridCols;
            int cellHeight = imgHeight / gridRows;

            // Step 2: Compute mean brightness per cell
            var cellMeans = new double[gridRows, gridCols];
            var allMeans = new List<double>();

            for (int row = 0; row < gridRows; row++)
            {
                for (int col = 0; col < gridCols; col++)
                {
                    int x = col * cellWidth;
                    int y = row * cellHeight;
                    // For the last col/row, extend to image edge
                    int w = (col == gridCols - 1) ? imgWidth - x : cellWidth;
                    int h = (row == gridRows - 1) ? imgHeight - y : cellHeight;

                    if (w <= 0 || h <= 0) continue;

                    var roi = new Rect(x, y, w, h);
                    using var cellMat = new Mat(gray, roi);
                    var mean = Cv2.Mean(cellMat);
                    cellMeans[row, col] = mean.Val0;
                    allMeans.Add(mean.Val0);
                }
            }
            gray.Dispose();

            // Step 3: Compute statistics
            double overallMean = allMeans.Count > 0 ? allMeans.Average() : 0;
            double minBrightness = allMeans.Count > 0 ? allMeans.Min() : 0;
            double maxBrightness = allMeans.Count > 0 ? allMeans.Max() : 0;

            double variance = 0;
            if (allMeans.Count > 0)
            {
                foreach (var m in allMeans)
                    variance += (m - overallMean) * (m - overallMean);
                variance /= allMeans.Count;
            }
            double stdDev = Math.Sqrt(variance);

            bool isUniform = stdDev <= maxStdDev;

            // Build result visualization
            var result = image.Clone();
            if (result.Channels() == 1)
                Cv2.CvtColor(result, result, ColorConversionCodes.GRAY2BGR);

            // Draw grid overlay with colored cells
            for (int row = 0; row < gridRows; row++)
            {
                for (int col = 0; col < gridCols; col++)
                {
                    int x = col * cellWidth;
                    int y = row * cellHeight;
                    int w = (col == gridCols - 1) ? imgWidth - x : cellWidth;
                    int h = (row == gridRows - 1) ? imgHeight - y : cellHeight;

                    if (w <= 0 || h <= 0) continue;

                    var cellRect = new Rect(x, y, w, h);
                    double cellMean = cellMeans[row, col];

                    // Determine if cell is within tolerance of overall mean
                    bool cellOk = Math.Abs(cellMean - overallMean) <= maxStdDev;

                    // Draw semi-transparent overlay
                    using var overlay = result.Clone();
                    var color = cellOk ? new Scalar(0, 255, 0) : new Scalar(0, 0, 255);
                    Cv2.Rectangle(overlay, cellRect, color, -1);
                    Cv2.AddWeighted(overlay, 0.15, result, 0.85, 0, result);

                    // Draw grid lines
                    Cv2.Rectangle(result, cellRect, new Scalar(255, 255, 255), 1);

                    // Draw brightness value in center of cell
                    string valueText = $"{cellMean:F0}";
                    var textSize = Cv2.GetTextSize(valueText, HersheyFonts.HersheySimplex, 0.4, 1, out _);
                    int textX = x + (w - textSize.Width) / 2;
                    int textY = y + (h + textSize.Height) / 2;
                    Cv2.PutText(result, valueText, new Point(textX, textY),
                        HersheyFonts.HersheySimplex, 0.4, new Scalar(255, 255, 255), 1);
                }
            }

            // Draw PASS/FAIL banner at top
            string passFailText = isUniform ? "PASS - UNIFORM" : "FAIL - NON-UNIFORM";
            var bannerColor = isUniform ? new Scalar(0, 180, 0) : new Scalar(0, 0, 220);
            Cv2.Rectangle(result, new Rect(0, 0, result.Width, 40), bannerColor, -1);
            Cv2.PutText(result, passFailText, new Point(10, 28),
                HersheyFonts.HersheySimplex, 0.7, new Scalar(255, 255, 255), 2);

            // Draw stats below banner
            string statsText = $"Mean:{overallMean:F1} StdDev:{stdDev:F1} Min:{minBrightness:F1} Max:{maxBrightness:F1}";
            Cv2.PutText(result, statsText, new Point(10, 65),
                HersheyFonts.HersheySimplex, 0.5, new Scalar(0, 255, 255), 1);

            SetOutputValue(_resultOutput, result);
            SetOutputValue(_meanBrightnessOutput, overallMean);
            SetOutputValue(_stdDevOutput, stdDev);
            SetOutputValue(_minBrightnessOutput, minBrightness);
            SetOutputValue(_maxBrightnessOutput, maxBrightness);
            SetOutputValue(_isUniformOutput, isUniform);
            SetPreview(result);
            Error = null;
        }
        catch (Exception ex)
        {
            Error = $"Brightness Uniformity error: {ex.Message}";
        }
    }
}
