using OpenCvSharp;
using MVXTester.Core.Models;
using MVXTester.Core.Registry;

namespace MVXTester.Nodes.Arithmetic;

[NodeInfo("Abs Diff", NodeCategories.Arithmetic, Description = "Calculate absolute difference between two images")]
public class AbsDiffNode : BaseNode
{
    private InputPort<Mat> _image1Input = null!;
    private InputPort<Mat> _image2Input = null!;
    private OutputPort<Mat> _resultOutput = null!;

    protected override void Setup()
    {
        _image1Input = AddInput<Mat>("Image1");
        _image2Input = AddInput<Mat>("Image2");
        _resultOutput = AddOutput<Mat>("Result");
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

            var result = new Mat();
            Cv2.Absdiff(image1, image2, result);

            SetOutputValue(_resultOutput, result);
            SetPreview(result);
            Error = null;
        }
        catch (Exception ex)
        {
            Error = $"Abs Diff error: {ex.Message}";
        }
    }
}
