using OpenCvSharp;
using MVXTester.Core.Models;
using MVXTester.Core.Registry;

namespace MVXTester.Nodes.Filter;

[NodeInfo("Bilateral Filter", NodeCategories.Filter, Description = "Apply bilateral filter (edge-preserving smoothing)")]
public class BilateralFilterNode : BaseNode
{
    private InputPort<Mat> _imageInput = null!;
    private OutputPort<Mat> _resultOutput = null!;
    private NodeProperty _d = null!;
    private NodeProperty _sigmaColor = null!;
    private NodeProperty _sigmaSpace = null!;

    protected override void Setup()
    {
        _imageInput = AddInput<Mat>("Image");
        _resultOutput = AddOutput<Mat>("Result");
        _d = AddIntProperty("D", "Diameter", 9, 1, 100, "Diameter of each pixel neighborhood");
        _sigmaColor = AddDoubleProperty("SigmaColor", "Sigma Color", 75.0, 0.0, 500.0, "Filter sigma in the color space");
        _sigmaSpace = AddDoubleProperty("SigmaSpace", "Sigma Space", 75.0, 0.0, 500.0, "Filter sigma in the coordinate space");
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

            var d = _d.GetValue<int>();
            var sigmaColor = _sigmaColor.GetValue<double>();
            var sigmaSpace = _sigmaSpace.GetValue<double>();

            var result = new Mat();
            Cv2.BilateralFilter(image, result, d, sigmaColor, sigmaSpace);

            SetOutputValue(_resultOutput, result);
            SetPreview(result);
            Error = null;
        }
        catch (Exception ex)
        {
            Error = $"Bilateral Filter error: {ex.Message}";
        }
    }
}
