using OpenCvSharp;
using MVXTester.Core.Models;
using MVXTester.Core.Registry;

namespace MVXTester.Nodes.Feature;

[NodeInfo("FAST Features", NodeCategories.Feature, Description = "FAST corner detection")]
public class FASTNode : BaseNode
{
    private InputPort<Mat> _imageInput = null!;
    private OutputPort<Mat> _resultOutput = null!;
    private NodeProperty _threshold = null!;
    private NodeProperty _nonmaxSuppression = null!;

    protected override void Setup()
    {
        _imageInput = AddInput<Mat>("Image");
        _resultOutput = AddOutput<Mat>("Result");
        _threshold = AddIntProperty("Threshold", "Threshold", 10, 0, 255, "Detection threshold");
        _nonmaxSuppression = AddBoolProperty("NonmaxSuppression", "Non-max Suppression", true, "Apply non-maximum suppression");
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

            var threshold = _threshold.GetValue<int>();
            var nonmaxSuppression = _nonmaxSuppression.GetValue<bool>();

            using var fast = FastFeatureDetector.Create(threshold, nonmaxSuppression);
            var keypoints = fast.Detect(image);

            var result = new Mat();
            Cv2.DrawKeypoints(image, keypoints, result, Scalar.All(-1), DrawMatchesFlags.DrawRichKeypoints);

            SetOutputValue(_resultOutput, result);
            SetPreview(result);
            Error = null;
        }
        catch (Exception ex)
        {
            Error = $"FAST error: {ex.Message}";
        }
    }
}
