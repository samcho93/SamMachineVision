using OpenCvSharp;
using MVXTester.Core.Models;
using MVXTester.Core.Registry;

namespace MVXTester.Nodes.Threshold;

[NodeInfo("Threshold", NodeCategories.Threshold, Description = "Apply fixed-level threshold")]
public class ThresholdNode : BaseNode
{
    private InputPort<Mat> _imageInput = null!;
    private OutputPort<Mat> _resultOutput = null!;
    private NodeProperty _thresh = null!;
    private NodeProperty _maxVal = null!;
    private NodeProperty _type = null!;

    protected override void Setup()
    {
        _imageInput = AddInput<Mat>("Image");
        _resultOutput = AddOutput<Mat>("Result");
        _thresh = AddDoubleProperty("Thresh", "Threshold", 128.0, 0.0, 255.0, "Threshold value");
        _maxVal = AddDoubleProperty("MaxVal", "Max Value", 255.0, 0.0, 255.0, "Maximum value for THRESH_BINARY");
        _type = AddEnumProperty("Type", "Type", ThresholdTypes.Binary, "Threshold type");
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

            var thresh = _thresh.GetValue<double>();
            var maxVal = _maxVal.GetValue<double>();
            var type = _type.GetValue<ThresholdTypes>();

            var result = new Mat();
            Cv2.Threshold(image, result, thresh, maxVal, type);

            SetOutputValue(_resultOutput, result);
            SetPreview(result);
            Error = null;
        }
        catch (Exception ex)
        {
            Error = $"Threshold error: {ex.Message}";
        }
    }
}
