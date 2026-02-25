using OpenCvSharp;
using MVXTester.Core.Models;
using MVXTester.Core.Registry;

namespace MVXTester.Nodes.Edge;

[NodeInfo("Scharr Edge", NodeCategories.Edge, Description = "Scharr edge detection (more accurate than Sobel for small kernels)")]
public class ScharrNode : BaseNode
{
    private InputPort<Mat> _imageInput = null!;
    private OutputPort<Mat> _resultOutput = null!;
    private NodeProperty _dx = null!;
    private NodeProperty _dy = null!;

    protected override void Setup()
    {
        _imageInput = AddInput<Mat>("Image");
        _resultOutput = AddOutput<Mat>("Result");
        _dx = AddIntProperty("Dx", "X Order", 1, 0, 1, "Order of derivative X (0 or 1)");
        _dy = AddIntProperty("Dy", "Y Order", 0, 0, 1, "Order of derivative Y (0 or 1)");
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

            if (dx == 0 && dy == 0)
            {
                Error = "At least one of Dx or Dy must be non-zero";
                return;
            }

            var result = new Mat();
            Cv2.Scharr(image, result, MatType.CV_16S, dx, dy);

            var absResult = new Mat();
            Cv2.ConvertScaleAbs(result, absResult);
            result.Dispose();

            SetOutputValue(_resultOutput, absResult);
            SetPreview(absResult);
            Error = null;
        }
        catch (Exception ex)
        {
            Error = $"Scharr error: {ex.Message}";
        }
    }
}
