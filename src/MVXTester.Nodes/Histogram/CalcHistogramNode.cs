using OpenCvSharp;
using MVXTester.Core.Models;
using MVXTester.Core.Registry;

namespace MVXTester.Nodes.Histogram;

[NodeInfo("Calc Histogram", NodeCategories.Histogram, Description = "Calculate image histogram")]
public class CalcHistogramNode : BaseNode
{
    private InputPort<Mat> _imageInput = null!;
    private OutputPort<Mat> _histogramOutput = null!;

    protected override void Setup()
    {
        _imageInput = AddInput<Mat>("Image");
        _histogramOutput = AddOutput<Mat>("Histogram");
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

            // Convert to grayscale if needed
            Mat gray = image;
            bool needDispose = false;
            if (image.Channels() > 1)
            {
                gray = new Mat();
                Cv2.CvtColor(image, gray, ColorConversionCodes.BGR2GRAY);
                needDispose = true;
            }

            var hist = new Mat();
            Cv2.CalcHist(new[] { gray }, new[] { 0 }, null, hist, 1, new[] { 256 }, new[] { new Rangef(0, 256) });

            if (needDispose) gray.Dispose();

            // Create histogram visualization
            int histW = 512, histH = 400;
            int binW = histW / 256;
            var histImage = new Mat(histH, histW, MatType.CV_8UC3, Scalar.All(0));

            Cv2.Normalize(hist, hist, 0, histH, NormTypes.MinMax);

            for (int i = 1; i < 256; i++)
            {
                Cv2.Line(histImage,
                    new Point(binW * (i - 1), histH - (int)hist.Get<float>(i - 1)),
                    new Point(binW * i, histH - (int)hist.Get<float>(i)),
                    new Scalar(255, 255, 255), 2);
            }

            SetOutputValue(_histogramOutput, hist);
            SetPreview(histImage);
            histImage.Dispose();
            Error = null;
        }
        catch (Exception ex)
        {
            Error = $"Calc Histogram error: {ex.Message}";
        }
    }
}
