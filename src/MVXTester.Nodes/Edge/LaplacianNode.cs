using OpenCvSharp;
using MVXTester.Core.Models;
using MVXTester.Core.Registry;

namespace MVXTester.Nodes.Edge;

[NodeInfo("Laplacian Edge", NodeCategories.Edge, Description = "Laplacian edge detection")]
public class LaplacianNode : BaseNode
{
    private InputPort<Mat> _imageInput = null!;
    private OutputPort<Mat> _resultOutput = null!;
    private NodeProperty _ksize = null!;

    protected override void Setup()
    {
        _imageInput = AddInput<Mat>("Image");
        _resultOutput = AddOutput<Mat>("Result");
        _ksize = AddIntProperty("KSize", "Kernel Size", 3, 1, 31, "Aperture size (odd)");
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

            var ksize = _ksize.GetValue<int>();
            if (ksize % 2 == 0) ksize++;

            var result = new Mat();
            Cv2.Laplacian(image, result, MatType.CV_16S, ksize);

            var absResult = new Mat();
            Cv2.ConvertScaleAbs(result, absResult);
            result.Dispose();

            SetOutputValue(_resultOutput, absResult);
            SetPreview(absResult);
            Error = null;
        }
        catch (Exception ex)
        {
            Error = $"Laplacian error: {ex.Message}";
        }
    }
}
