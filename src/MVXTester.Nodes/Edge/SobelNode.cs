using OpenCvSharp;
using MVXTester.Core.Models;
using MVXTester.Core.Registry;

namespace MVXTester.Nodes.Edge;

[NodeInfo("Sobel Edge", NodeCategories.Edge, Description = "Sobel edge detection")]
public class SobelNode : BaseNode
{
    private InputPort<Mat> _imageInput = null!;
    private OutputPort<Mat> _resultOutput = null!;
    private NodeProperty _dx = null!;
    private NodeProperty _dy = null!;
    private NodeProperty _ksize = null!;

    protected override void Setup()
    {
        _imageInput = AddInput<Mat>("Image");
        _resultOutput = AddOutput<Mat>("Result");
        _dx = AddIntProperty("Dx", "X Order", 1, 0, 3, "Order of derivative X");
        _dy = AddIntProperty("Dy", "Y Order", 0, 0, 3, "Order of derivative Y");
        _ksize = AddIntProperty("KSize", "Kernel Size", 3, 1, 7, "Sobel kernel size (odd)");
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

            var dx = _dx.GetValue<int>();
            var dy = _dy.GetValue<int>();
            var ksize = _ksize.GetValue<int>();
            if (ksize % 2 == 0) ksize++;

            var result = new Mat();
            Cv2.Sobel(image, result, MatType.CV_16S, dx, dy, ksize);

            var absResult = new Mat();
            Cv2.ConvertScaleAbs(result, absResult);
            result.Dispose();

            SetOutputValue(_resultOutput, absResult);
            SetPreview(absResult);
            Error = null;
        }
        catch (Exception ex)
        {
            Error = $"Sobel error: {ex.Message}";
        }
    }
}
