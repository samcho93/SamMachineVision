using OpenCvSharp;
using MVXTester.Core.Models;
using MVXTester.Core.Registry;

namespace MVXTester.Nodes.Morphology;

[NodeInfo("Erode", NodeCategories.Morphology, Description = "Erode morphological operation")]
public class ErodeNode : BaseNode
{
    private InputPort<Mat> _imageInput = null!;
    private OutputPort<Mat> _resultOutput = null!;
    private NodeProperty _kernelSize = null!;
    private NodeProperty _iterations = null!;

    protected override void Setup()
    {
        _imageInput = AddInput<Mat>("Image");
        _resultOutput = AddOutput<Mat>("Result");
        _kernelSize = AddIntProperty("KernelSize", "Kernel Size", 3, 1, 99, "Structuring element size");
        _iterations = AddIntProperty("Iterations", "Iterations", 1, 1, 100, "Number of iterations");
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

            var kernelSize = _kernelSize.GetValue<int>();
            var iterations = _iterations.GetValue<int>();

            var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(kernelSize, kernelSize));
            var result = new Mat();
            Cv2.Erode(image, result, kernel, iterations: iterations);

            kernel.Dispose();

            SetOutputValue(_resultOutput, result);
            SetPreview(result);
            Error = null;
        }
        catch (Exception ex)
        {
            Error = $"Erode error: {ex.Message}";
        }
    }
}
