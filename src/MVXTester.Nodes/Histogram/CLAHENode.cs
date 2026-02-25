using OpenCvSharp;
using MVXTester.Core.Models;
using MVXTester.Core.Registry;

namespace MVXTester.Nodes.Histogram;

[NodeInfo("CLAHE", NodeCategories.Histogram, Description = "Contrast Limited Adaptive Histogram Equalization")]
public class CLAHENode : BaseNode
{
    private InputPort<Mat> _imageInput = null!;
    private OutputPort<Mat> _resultOutput = null!;
    private NodeProperty _clipLimit = null!;
    private NodeProperty _tileGridSize = null!;

    protected override void Setup()
    {
        _imageInput = AddInput<Mat>("Image");
        _resultOutput = AddOutput<Mat>("Result");
        _clipLimit = AddDoubleProperty("ClipLimit", "Clip Limit", 2.0, 0.0, 100.0, "Threshold for contrast limiting");
        _tileGridSize = AddIntProperty("TileGridSize", "Tile Grid Size", 8, 1, 64, "Size of grid for histogram equalization");
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

            var clipLimit = _clipLimit.GetValue<double>();
            var tileGridSize = _tileGridSize.GetValue<int>();

            // Ensure grayscale
            Mat gray = image;
            bool needDispose = false;
            if (image.Channels() > 1)
            {
                gray = new Mat();
                Cv2.CvtColor(image, gray, ColorConversionCodes.BGR2GRAY);
                needDispose = true;
            }

            using var clahe = Cv2.CreateCLAHE(clipLimit, new Size(tileGridSize, tileGridSize));
            var result = new Mat();
            clahe.Apply(gray, result);

            if (needDispose) gray.Dispose();

            SetOutputValue(_resultOutput, result);
            SetPreview(result);
            Error = null;
        }
        catch (Exception ex)
        {
            Error = $"CLAHE error: {ex.Message}";
        }
    }
}
