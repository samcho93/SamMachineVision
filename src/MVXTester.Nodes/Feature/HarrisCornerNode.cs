using OpenCvSharp;
using MVXTester.Core.Models;
using MVXTester.Core.Registry;

namespace MVXTester.Nodes.Feature;

[NodeInfo("Harris Corner", NodeCategories.Feature, Description = "Harris corner detection")]
public class HarrisCornerNode : BaseNode
{
    private InputPort<Mat> _imageInput = null!;
    private OutputPort<Mat> _resultOutput = null!;
    private NodeProperty _blockSize = null!;
    private NodeProperty _ksize = null!;
    private NodeProperty _k = null!;

    protected override void Setup()
    {
        _imageInput = AddInput<Mat>("Image");
        _resultOutput = AddOutput<Mat>("Result");
        _blockSize = AddIntProperty("BlockSize", "Block Size", 2, 1, 31, "Neighborhood size");
        _ksize = AddIntProperty("KSize", "Aperture Size", 3, 1, 31, "Sobel aperture parameter");
        _k = AddDoubleProperty("K", "K", 0.04, 0.0, 1.0, "Harris detector free parameter");
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

            var blockSize = _blockSize.GetValue<int>();
            var ksize = _ksize.GetValue<int>();
            var k = _k.GetValue<double>();

            // Convert to grayscale and float
            Mat gray = image;
            bool needDispose = false;
            if (image.Channels() > 1)
            {
                gray = new Mat();
                Cv2.CvtColor(image, gray, ColorConversionCodes.BGR2GRAY);
                needDispose = true;
            }

            var grayFloat = new Mat();
            gray.ConvertTo(grayFloat, MatType.CV_32FC1);
            if (needDispose) gray.Dispose();

            var harris = new Mat();
            Cv2.CornerHarris(grayFloat, harris, blockSize, ksize, k);
            grayFloat.Dispose();

            // Normalize for visualization
            var normalized = new Mat();
            Cv2.Normalize(harris, normalized, 0, 255, NormTypes.MinMax);
            harris.Dispose();

            var result = new Mat();
            normalized.ConvertTo(result, MatType.CV_8UC1);
            normalized.Dispose();

            SetOutputValue(_resultOutput, result);
            SetPreview(result);
            Error = null;
        }
        catch (Exception ex)
        {
            Error = $"Harris Corner error: {ex.Message}";
        }
    }
}
