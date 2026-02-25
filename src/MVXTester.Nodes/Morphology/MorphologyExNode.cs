using OpenCvSharp;
using MVXTester.Core.Models;
using MVXTester.Core.Registry;

namespace MVXTester.Nodes.Morphology;

public enum MorphOperation
{
    Open,
    Close,
    Gradient,
    TopHat,
    BlackHat
}

public enum MorphShape
{
    Rect,
    Cross,
    Ellipse
}

[NodeInfo("Morphology Ex", NodeCategories.Morphology, Description = "Advanced morphological operations")]
public class MorphologyExNode : BaseNode
{
    private InputPort<Mat> _imageInput = null!;
    private OutputPort<Mat> _resultOutput = null!;
    private NodeProperty _operation = null!;
    private NodeProperty _shape = null!;
    private NodeProperty _kernelSize = null!;
    private NodeProperty _iterations = null!;

    protected override void Setup()
    {
        _imageInput = AddInput<Mat>("Image");
        _resultOutput = AddOutput<Mat>("Result");
        _operation = AddEnumProperty("Operation", "Operation", MorphOperation.Open, "Morphological operation");
        _shape = AddEnumProperty("Shape", "Kernel Shape", MorphShape.Rect, "Structuring element shape");
        _kernelSize = AddIntProperty("KernelSize", "Kernel Size", 5, 1, 99, "Structuring element size");
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

            var operation = _operation.GetValue<MorphOperation>();
            var shape = _shape.GetValue<MorphShape>();
            var kernelSize = _kernelSize.GetValue<int>();
            var iterations = _iterations.GetValue<int>();

            var morphShape = shape switch
            {
                MorphShape.Rect => MorphShapes.Rect,
                MorphShape.Cross => MorphShapes.Cross,
                MorphShape.Ellipse => MorphShapes.Ellipse,
                _ => MorphShapes.Rect
            };

            var morphType = operation switch
            {
                MorphOperation.Open => MorphTypes.Open,
                MorphOperation.Close => MorphTypes.Close,
                MorphOperation.Gradient => MorphTypes.Gradient,
                MorphOperation.TopHat => MorphTypes.TopHat,
                MorphOperation.BlackHat => MorphTypes.BlackHat,
                _ => MorphTypes.Open
            };

            var kernel = Cv2.GetStructuringElement(morphShape, new Size(kernelSize, kernelSize));
            var result = new Mat();
            Cv2.MorphologyEx(image, result, morphType, kernel, iterations: iterations);

            kernel.Dispose();

            SetOutputValue(_resultOutput, result);
            SetPreview(result);
            Error = null;
        }
        catch (Exception ex)
        {
            Error = $"MorphologyEx error: {ex.Message}";
        }
    }
}
