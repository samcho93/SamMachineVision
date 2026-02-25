using OpenCvSharp;
using MVXTester.Core.Models;
using MVXTester.Core.Registry;

namespace MVXTester.Nodes.Arithmetic;

[NodeInfo("Weighted Add", NodeCategories.Arithmetic, Description = "Weighted addition (alpha blending) of two images")]
public class WeightedAddNode : BaseNode
{
    private InputPort<Mat> _image1Input = null!;
    private InputPort<Mat> _image2Input = null!;
    private OutputPort<Mat> _resultOutput = null!;
    private NodeProperty _alpha = null!;
    private NodeProperty _beta = null!;
    private NodeProperty _gamma = null!;

    protected override void Setup()
    {
        _image1Input = AddInput<Mat>("Image1");
        _image2Input = AddInput<Mat>("Image2");
        _resultOutput = AddOutput<Mat>("Result");
        _alpha = AddDoubleProperty("Alpha", "Alpha", 0.5, 0.0, 10.0, "Weight of first image");
        _beta = AddDoubleProperty("Beta", "Beta", 0.5, 0.0, 10.0, "Weight of second image");
        _gamma = AddDoubleProperty("Gamma", "Gamma", 0.0, -255.0, 255.0, "Scalar added to each sum");
    }

    public override void Process()
    {
        try
        {
            var image1 = GetInputValue(_image1Input);
            var image2 = GetInputValue(_image2Input);

            if (image1 == null || image1.Empty() || image2 == null || image2.Empty())
            {
                Error = "Both input images required";
                return;
            }

            var alpha = _alpha.GetValue<double>();
            var beta = _beta.GetValue<double>();
            var gamma = _gamma.GetValue<double>();

            var result = new Mat();
            Cv2.AddWeighted(image1, alpha, image2, beta, gamma, result);

            SetOutputValue(_resultOutput, result);
            SetPreview(result);
            Error = null;
        }
        catch (Exception ex)
        {
            Error = $"Weighted Add error: {ex.Message}";
        }
    }
}
