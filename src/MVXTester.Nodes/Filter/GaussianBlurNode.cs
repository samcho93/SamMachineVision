using OpenCvSharp;
using MVXTester.Core.Models;
using MVXTester.Core.Registry;

namespace MVXTester.Nodes.Filter;

[NodeInfo("Gaussian Blur", NodeCategories.Filter, Description = "Apply Gaussian blur filter")]
public class GaussianBlurNode : BaseNode
{
    private InputPort<Mat> _imageInput = null!;
    private OutputPort<Mat> _resultOutput = null!;
    private NodeProperty _kernelW = null!;
    private NodeProperty _kernelH = null!;
    private NodeProperty _sigmaX = null!;

    protected override void Setup()
    {
        _imageInput = AddInput<Mat>("Image");
        _resultOutput = AddOutput<Mat>("Result");
        _kernelW = AddIntProperty("KernelW", "Kernel Width", 5, 1, 99, "Kernel width (odd number)");
        _kernelH = AddIntProperty("KernelH", "Kernel Height", 5, 1, 99, "Kernel height (odd number)");
        _sigmaX = AddDoubleProperty("SigmaX", "Sigma X", 0.0, 0.0, 100.0, "Gaussian sigma X");
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

            var kw = _kernelW.GetValue<int>();
            var kh = _kernelH.GetValue<int>();
            var sigmaX = _sigmaX.GetValue<double>();

            // Ensure odd kernel sizes
            if (kw % 2 == 0) kw++;
            if (kh % 2 == 0) kh++;

            var result = new Mat();
            Cv2.GaussianBlur(image, result, new Size(kw, kh), sigmaX);

            SetOutputValue(_resultOutput, result);
            SetPreview(result);
            Error = null;
        }
        catch (Exception ex)
        {
            Error = $"Gaussian Blur error: {ex.Message}";
        }
    }
}
