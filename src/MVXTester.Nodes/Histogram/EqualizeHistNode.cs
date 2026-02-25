using OpenCvSharp;
using MVXTester.Core.Models;
using MVXTester.Core.Registry;

namespace MVXTester.Nodes.Histogram;

[NodeInfo("Equalize Histogram", NodeCategories.Histogram, Description = "Equalize image histogram")]
public class EqualizeHistNode : BaseNode
{
    private InputPort<Mat> _imageInput = null!;
    private OutputPort<Mat> _resultOutput = null!;

    protected override void Setup()
    {
        _imageInput = AddInput<Mat>("Image");
        _resultOutput = AddOutput<Mat>("Result");
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

            // Ensure grayscale
            Mat gray = image;
            bool needDispose = false;
            if (image.Channels() > 1)
            {
                gray = new Mat();
                Cv2.CvtColor(image, gray, ColorConversionCodes.BGR2GRAY);
                needDispose = true;
            }

            var result = new Mat();
            Cv2.EqualizeHist(gray, result);
            if (needDispose) gray.Dispose();

            SetOutputValue(_resultOutput, result);
            SetPreview(result);
            Error = null;
        }
        catch (Exception ex)
        {
            Error = $"Equalize Histogram error: {ex.Message}";
        }
    }
}
